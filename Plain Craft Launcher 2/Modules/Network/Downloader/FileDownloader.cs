using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using PCL.Core.App;
using PCL.Core.IO.Net;
using PCL.Core.Utils;


namespace PCL.Network;

public static class FileDownloader
{
    private const int RequestTimeoutMilliseconds = 30_000;
    private const int MaxDownloadRetries = 3;

    public static async Task DownloadAsync(string url, string localPath, bool useBrowserUserAgent = false,
        string customUserAgent = "", CancellationToken cancellationToken = default,
        bool enableParallelChunks = true, DownloadFile? trackedFile = null)
    {
        await DownloadCoreAsync([url], localPath, useBrowserUserAgent, customUserAgent, cancellationToken,
            enableParallelChunks, trackedFile).ConfigureAwait(false);
    }

    public static async Task DownloadAsync(IEnumerable<string> urls, string localPath, bool useBrowserUserAgent = false,
        string customUserAgent = "", CancellationToken cancellationToken = default,
        bool enableParallelChunks = true, DownloadFile? trackedFile = null)
    {
        await DownloadCoreAsync(urls, localPath, useBrowserUserAgent, customUserAgent, cancellationToken,
            enableParallelChunks, trackedFile).ConfigureAwait(false);
    }

    public static void DownloadByLoader(string url, string localPath, bool useBrowserUserAgent = false,
        string customUserAgent = "")
    {
        DownloadAsync(url, localPath, useBrowserUserAgent, customUserAgent).GetAwaiter().GetResult();
    }

    public static void DownloadByLoader(IEnumerable<string> urls, string localPath, bool useBrowserUserAgent = false,
        string customUserAgent = "")
    {
        DownloadAsync(urls, localPath, useBrowserUserAgent, customUserAgent).GetAwaiter().GetResult();
    }

    private static async Task DownloadCoreAsync(IEnumerable<string> urls, string localPath, bool useBrowserUserAgent,
        string customUserAgent, CancellationToken cancellationToken, bool enableParallelChunks, DownloadFile? trackedFile)
    {
        var urlList = urls.Select(url => RequestSigning.SecretCdnSign(url.Trim())).Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct().ToList();
        if (urlList.Count == 0)
            throw new ArgumentException("未提供可用的下载地址", nameof(urls));

        Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? throw new ArgumentException("下载路径无效", nameof(localPath)));

        Exception? lastException = null;
        for (var retry = 0; retry <= MaxDownloadRetries; retry++)
        {
            foreach (var url in urlList)
            {
                try
                {
                    await DownloadSingleAsync(url, localPath, useBrowserUserAgent, customUserAgent, cancellationToken,
                        enableParallelChunks, trackedFile).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    CleanupTempFiles(localPath);
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    CleanupTempFiles(localPath);
                    ModBase.Log(ex, $"[Download] 下载源失败：{url}", ModBase.LogLevel.Debug);
                }
            }

            if (retry >= MaxDownloadRetries)
                break;

            ModBase.Log(lastException, $"[Download] 重试 {retry + 1}/{MaxDownloadRetries}：{localPath}",
                ModBase.LogLevel.Debug);
            await Task.Delay(RandomUtils.NextInt(300, 500 + retry * 300), cancellationToken).ConfigureAwait(false);
        }

        throw new IOException($"下载失败：{localPath}", lastException);
    }

    private static async Task DownloadSingleAsync(string url, string localPath, bool useBrowserUserAgent,
        string customUserAgent, CancellationToken cancellationToken, bool enableParallelChunks, DownloadFile? trackedFile)
    {
        ModBase.Log($"[Download] 开始下载：{url} -> {localPath}");
        CleanupTempFiles(localPath);

        var checker = trackedFile?.Check;
        var expectedSize = string.IsNullOrEmpty(checker?.hash) ? checker?.actualSize ?? -1 : -1;
        var sequentialRequestKind = (expectedSize >= 0 && expectedSize < AdaptiveRangeDownloader.SmallFileThreshold) ||
                                     (expectedSize < 0 && !enableParallelChunks)
            ? DownloadRequestKind.SmallOrBatch
            : DownloadRequestKind.LargeOrUnknown;

        if (enableParallelChunks && await AdaptiveRangeDownloader.TryDownloadAsync(url, localPath,
                useBrowserUserAgent, customUserAgent, cancellationToken, trackedFile,
                expectedSize).ConfigureAwait(false))
        {
            await ValidateTempFileAsync(localPath, checker, cancellationToken).ConfigureAwait(false);
            PromoteTempFile(localPath);
            if (!File.Exists(localPath))
                throw new IOException($"分段下载未产生任何文件：{localPath}");
            MarkDownloadCompleted(trackedFile);
            ModBase.Log($"[Download] 分段下载成功：{localPath}");
            return;
        }

        await DownloadSequentiallyAsync(url, localPath, useBrowserUserAgent, customUserAgent, cancellationToken,
            trackedFile, sequentialRequestKind).ConfigureAwait(false);
    }

    private static async Task DownloadSequentiallyAsync(string url, string localPath, bool useBrowserUserAgent,
        string customUserAgent, CancellationToken cancellationToken, DownloadFile? trackedFile,
        DownloadRequestKind requestKind)
    {
        const int bufferSize = 64 * 1024;
        const int readTimeoutMilliseconds = 30_000;
        using var connection = await DownloadResourceManager.AcquireConnectionAsync(url, cancellationToken)
            .ConfigureAwait(false);
        using var request = CreateDownloadRequest(url, useBrowserUserAgent, customUserAgent, requestKind);
        using var response = await SendDownloadRequestAsync(url, request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"下载请求失败：{(int)response.StatusCode} {response.ReasonPhrase}");

        var responseContentLength = response.Content.Headers.ContentLength ?? -1;
        var checker = trackedFile?.Check;
        var manifestExpectedSize = checker?.actualSize ?? -1;
        if (string.IsNullOrEmpty(checker?.hash) && responseContentLength >= 0 && manifestExpectedSize >= 0 &&
            responseContentLength != manifestExpectedSize)
            throw new IOException($"下载大小与清单不一致：响应为 {responseContentLength}，清单为 {manifestExpectedSize}");

        var totalSize = responseContentLength >= 0 ? responseContentLength : manifestExpectedSize;
        if (trackedFile is not null)
        {
            trackedFile.State = PCL.Network.NetState.Downloading;
            trackedFile.TotalSize = totalSize;
            trackedFile.IsUnknownSize = totalSize <= 0;
            trackedFile.DownloadedBytes = 0;
            trackedFile.Speed = 0;
            trackedFile.ActiveThreads = 1;
        }

        long downloaded = 0;
        long lastProgressBytes = 0;
        var lastProgressTick = Stopwatch.GetTimestamp();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var output = new FileStream(localPath + ModNet.NetDownloadEnd, FileMode.Create, FileAccess.Write,
            FileShare.Read, bufferSize: bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var bufferLease = await DownloadResourceManager.ReserveBufferAsync(bufferSize, cancellationToken)
            .ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            while (true)
            {
                int read;
                readTimeout.CancelAfter(readTimeoutMilliseconds);
                try
                {
                    read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), readTimeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                                                       readTimeout.IsCancellationRequested)
                {
                    throw new TimeoutException($"下载超时（{url}）");
                }

                if (read == 0)
                    break;

                await DownloadResourceManager.ThrottleAsync(read, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                DownloadResourceManager.RecordDownloadedBytes(read);
                downloaded += read;
                UpdateSequentialProgress(trackedFile, downloaded, totalSize, ref lastProgressBytes, ref lastProgressTick);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        connection.Dispose();
        await output.DisposeAsync().ConfigureAwait(false);
        if (responseContentLength >= 0 && downloaded != responseContentLength)
            throw new IOException($"下载不完整：已写入 {downloaded}，应为响应声明的 {responseContentLength}");

        await ValidateTempFileAsync(localPath, checker, cancellationToken).ConfigureAwait(false);

        if (trackedFile is not null && totalSize <= 0)
        {
            trackedFile.TotalSize = downloaded;
            trackedFile.IsUnknownSize = false;
            trackedFile.DownloadedBytes = downloaded;
        }

        PromoteTempFile(localPath);
        if (!File.Exists(localPath))
            throw new IOException($"下载未产生任何文件：{localPath}");
        MarkDownloadCompleted(trackedFile);
        ModBase.Log($"[Download] 顺序下载成功：{localPath}");
    }

    private static async Task ValidateTempFileAsync(string localPath, ModBase.FileChecker? checker,
        CancellationToken cancellationToken)
    {
        if (checker is null)
            return;

        var tempPath = localPath + ModNet.NetDownloadEnd;
        var checkResult = string.IsNullOrEmpty(checker.hash)
            ? checker.Check(tempPath)
            : await Task.Run(() => checker.Check(tempPath), cancellationToken).ConfigureAwait(false);
        if (checkResult is not null)
            throw new IOException($"下载文件校验失败：{checkResult}");
    }

    private static void UpdateSequentialProgress(DownloadFile? trackedFile, long downloaded, long totalSize,
        ref long lastProgressBytes, ref long lastProgressTick)
    {
        if (trackedFile is null)
            return;

        var now = Stopwatch.GetTimestamp();
        if (now - lastProgressTick < Stopwatch.Frequency / 5 && (totalSize <= 0 || downloaded < totalSize))
            return;

        var elapsed = Math.Max(1L, now - lastProgressTick);
        trackedFile.DownloadedBytes = downloaded;
        trackedFile.Speed = Math.Max(0L, (long)((downloaded - lastProgressBytes) * (double)Stopwatch.Frequency / elapsed));
        trackedFile.ActiveThreads = 1;
        lastProgressBytes = downloaded;
        lastProgressTick = now;
    }

    internal static HttpRequestMessage CreateDownloadRequest(string url, bool useBrowserUserAgent,
        string customUserAgent, DownloadRequestKind requestKind)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        RequestSigning.SecretHeadersSign(url, ref request, useBrowserUserAgent, customUserAgent);
        ApplyHttpVersion(request, requestKind);
        return request;
    }

    internal static async Task<HttpResponseMessage> SendDownloadRequestAsync(string url,
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(RequestTimeoutMilliseconds);
        try
        {
            return await GetHttpClient(url)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested &&
                                                    requestTimeout.IsCancellationRequested)
        {
            throw new DownloadRequestTimeoutException($"等待下载源响应超时（30 秒）：{url}", ex);
        }
    }

    private static void ApplyHttpVersion(HttpRequestMessage request, DownloadRequestKind requestKind)
    {
        switch (Config.Download.HttpMode)
        {
            case DownloadHttpMode.Http11:
                request.Version = HttpVersion.Version11;
                request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
                return;
            case DownloadHttpMode.Http2:
                request.Version = HttpVersion.Version20;
                request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                return;
        }

        // 自动模式规则：
        // 1. 已知小于 4 MiB 的文件，以及批量任务中的顺序下载，优先 HTTP/2，
        //    让大量小文件复用连接并通过多个 Stream 并发传输。
        // 2. Range 探测、Range 分段，以及大小未知的单文件下载使用 HTTP/1.1，
        //    让大文件分段获得独立 TCP 连接；Range 不可用时也避免再次切换协议。
        // 3. HTTP/2 使用 RequestVersionOrLower，源站或代理不支持时自动回退到 HTTP/1.1。
        if (requestKind == DownloadRequestKind.SmallOrBatch)
        {
            request.Version = HttpVersion.Version20;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        }
        else
        {
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        }
    }

    private static void MarkDownloadCompleted(DownloadFile? trackedFile)
    {
        if (trackedFile is null)
            return;

        trackedFile.Speed = 0;
        trackedFile.ActiveThreads = 0;
        trackedFile.DownloadedBytes = Math.Max(trackedFile.DownloadedBytes, trackedFile.TotalSize);
    }

    private static void PromoteTempFile(string localPath)
    {
        var tempPath = localPath + ModNet.NetDownloadEnd;
        if (File.Exists(localPath) || !File.Exists(tempPath))
            return;

        for (var retry = 0; retry < 5; retry++)
        {
            try
            {
                File.Move(tempPath, localPath, true);
                return;
            }
            catch (IOException) when (retry < 4)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static void CleanupTempFiles(string localPath)
    {
        var tempPath = localPath + ModNet.NetDownloadEnd;
        TryDeleteFile(localPath);
        TryDeleteFile(tempPath);
    }

    private static void TryDeleteFile(string path)
    {
        for (var retry = 0; retry < 5; retry++)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }

    internal static HttpClient GetHttpClient(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsedUri)
            && parsedUri.Host is "edge.forgecdn.net" or "mediafilez.forgecdn.net" or "forgecdn.net" or "api.curseforge.com")
        {
            return NetworkService.GetClient(NetworkService.CurseForgeApi);
        }
        
        return NetworkService.GetClient();
    }
}

internal sealed class DownloadRequestTimeoutException(string message, Exception innerException)
    : TimeoutException(message, innerException);

internal enum DownloadRequestKind
{
    SmallOrBatch,
    LargeOrUnknown,
    RangeProbe,
    RangeSegment
}

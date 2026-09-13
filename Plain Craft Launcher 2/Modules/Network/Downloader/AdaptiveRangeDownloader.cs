using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace PCL.Network;

/// <summary>为支持 HTTP Range 的大文件提供动态分段下载与慢连接恢复。</summary>
internal sealed class AdaptiveRangeDownloader
{
    internal const long SmallFileThreshold = 4L * 1024 * 1024;
    private const long TargetSegmentSize = 8L * 1024 * 1024;
    private const int MaxSegmentCount = 1024;
    private const int MaxExpandedSegmentCount = MaxSegmentCount * 2;
    private const int BufferSize = 64 * 1024;
    private const int ReadTimeoutMilliseconds = 15_000;
    private const int SlowCheckSeconds = 8;
    private const long SlowSpeedBytesPerSecond = 50L * 1024;
    private const long ClearlyFastSegmentBytesPerSecond = 128L * 1024;
    private const long SlowSplitThreshold = 2L * 1024 * 1024;
    private const long MinimumRestartRemaining = 1L * 1024 * 1024;
    private const int MaxSegmentRetries = 2;

    private readonly string _url;
    private readonly string _tempPath;
    private readonly bool _useBrowserUserAgent;
    private readonly string _customUserAgent;
    private readonly DownloadFile? _trackedFile;

    private AdaptiveRangeDownloader(string url, string localPath, bool useBrowserUserAgent, string customUserAgent,
        DownloadFile? trackedFile)
    {
        _url = url;
        _tempPath = localPath + ModNet.NetDownloadEnd;
        _useBrowserUserAgent = useBrowserUserAgent;
        _customUserAgent = customUserAgent;
        _trackedFile = trackedFile;
    }

    /// <summary>若文件适合 Range 分段下载则完成下载并返回 true，否则返回 false 交给顺序下载器处理。</summary>
    public static async Task<bool> TryDownloadAsync(string url, string localPath, bool useBrowserUserAgent,
        string customUserAgent, CancellationToken cancellationToken, DownloadFile? trackedFile,
        long expectedSize = -1)
    {
        // 下载清单已有大小时，小文件直接走顺序请求，避免额外的 Range 探测往返。
        if (expectedSize >= 0 && expectedSize < SmallFileThreshold)
            return false;

        var downloader = new AdaptiveRangeDownloader(url, localPath, useBrowserUserAgent, customUserAgent, trackedFile);
        var totalSize = expectedSize;
        if (totalSize < 0)
        {
            var probe = await downloader.ProbeAsync(cancellationToken).ConfigureAwait(false);
            if (probe is null || probe.Value.Size < SmallFileThreshold)
                return false;
            totalSize = probe.Value.Size;
        }

        try
        {
            await downloader.DownloadAsync(totalSize, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RangeNotSupportedException ex)
        {
            ModBase.Log(ex, $"[Download] 下载源不支持可靠的 Range，改用顺序下载：{url}", ModBase.LogLevel.Debug);
            return false;
        }
    }

    private async Task<RangeProbe?> ProbeAsync(CancellationToken cancellationToken)
    {
        using var connection = await DownloadResourceManager.AcquireConnectionAsync(_url, cancellationToken)
            .ConfigureAwait(false);
        using var request = CreateRequest(HttpMethod.Get, DownloadRequestKind.RangeProbe);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        using var response = await FileDownloader.SendDownloadRequestAsync(_url, request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.PartialContent)
            return null;

        var range = response.Content.Headers.ContentRange;
        if (range?.Unit != "bytes" || range.From != 0 || range.To != 0 || range.Length is not > 0)
            return null;

        return new RangeProbe(range.Length.Value);
    }

    private async Task DownloadAsync(long totalSize, CancellationToken cancellationToken)
    {
        NotifyStarted(totalSize);
        var segments = Channel.CreateUnbounded<DownloadSegment>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        var segmentCount = EnqueueInitialSegments(segments.Writer, totalSize);
        var workerCount = Math.Min(segmentCount, Math.Clamp(ModNet.NetTaskSingleFileConnectionLimit, 1,
            ModNet.NetTaskSingleFileConnectionLimitMax));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var output = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.Read,
            bufferSize: 1, FileOptions.Asynchronous | FileOptions.RandomAccess);
        output.SetLength(totalSize);

        var session = new DownloadSession(this, segments, totalSize, segmentCount, output.SafeFileHandle,
            linkedCancellation);
        var workers = Enumerable.Range(0, workerCount).Select(_ => session.RunWorkerAsync()).ToArray();
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            segments.Writer.TryComplete();
        }

        if (session.DownloadedBytes != totalSize)
            throw new IOException($"分段下载不完整：已写入 {session.DownloadedBytes}，应为 {totalSize}");

        NotifyProgress(totalSize, 0, force: true);
    }

    private int EnqueueInitialSegments(ChannelWriter<DownloadSegment> writer, long totalSize)
    {
        var count = (int)Math.Clamp((totalSize + TargetSegmentSize - 1) / TargetSegmentSize, 1, MaxSegmentCount);
        var segmentSize = (totalSize + count - 1) / count;
        var id = 0;
        for (long start = 0; start < totalSize; start += segmentSize)
        {
            var end = Math.Min(totalSize - 1, start + segmentSize - 1);
            writer.TryWrite(new DownloadSegment(Interlocked.Increment(ref id), start, end));
        }

        return id;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, DownloadRequestKind requestKind)
    {
        var request = FileDownloader.CreateDownloadRequest(_url, _useBrowserUserAgent, _customUserAgent, requestKind);
        request.Method = method;
        request.Headers.AcceptEncoding.Clear();
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        return request;
    }

    private void NotifyStarted(long totalSize)
    {
        if (_trackedFile is null)
            return;

        _trackedFile.State = NetState.Downloading;
        _trackedFile.TotalSize = totalSize;
        _trackedFile.IsUnknownSize = false;
        _trackedFile.DownloadedBytes = 0;
        _trackedFile.Speed = 0;
        _trackedFile.ActiveThreads = 0;
    }

    private void NotifyProgress(long downloaded, int activeConnections, bool force = false)
    {
        if (_trackedFile is null)
            return;

        _trackedFile.State = NetState.Downloading;
        _trackedFile.DownloadedBytes = downloaded;
        _trackedFile.ActiveThreads = activeConnections;
    }

    private readonly record struct RangeProbe(long Size);

    private sealed class DownloadSegment(int id, long start, long end)
    {
        public int Id { get; } = id;
        public long Start { get; } = start;
        public long End { get; set; } = end;
        public long Downloaded { get; set; }
        public int FailureCount { get; set; }
        public long LastRateSampleTick { get; set; }
        public long Remaining => End - Start - Downloaded + 1;
        public long CurrentOffset => Start + Downloaded;
    }

    private sealed class DownloadSession
    {
        private readonly AdaptiveRangeDownloader _owner;
        private readonly Channel<DownloadSegment> _segments;
        private readonly long _totalSize;
        private readonly SafeFileHandle _fileHandle;
        private readonly CancellationTokenSource _cancellation;
        private readonly ConcurrentDictionary<int, RateSample> _rates = new();
        // 尾段可能已没有其他活跃连接，保留近期完成分段作为比较基线。
        private readonly ConcurrentQueue<double> _completedRates = new();
        private readonly object _progressLock = new();
        private int _outstandingSegments;
        private int _nextSegmentId;
        private int _activeConnections;
        private long _downloadedBytes;
        private long _lastProgressBytes;
        private long _lastProgressTick = Stopwatch.GetTimestamp();
        private Exception? _failure;

        public DownloadSession(AdaptiveRangeDownloader owner, Channel<DownloadSegment> segments, long totalSize,
            int segmentCount, SafeFileHandle fileHandle, CancellationTokenSource cancellation)
        {
            _owner = owner;
            _segments = segments;
            _totalSize = totalSize;
            _outstandingSegments = segmentCount;
            _nextSegmentId = segmentCount;
            _fileHandle = fileHandle;
            _cancellation = cancellation;
        }

        public long DownloadedBytes => Interlocked.Read(ref _downloadedBytes);

        public async Task RunWorkerAsync()
        {
            try
            {
                while (await _segments.Reader.WaitToReadAsync(_cancellation.Token).ConfigureAwait(false))
                {
                    while (_segments.Reader.TryRead(out var segment))
                    {
                        try
                        {
                            await DownloadSegmentAsync(segment, _cancellation.Token).ConfigureAwait(false);
                            if (Interlocked.Decrement(ref _outstandingSegments) == 0)
                                _segments.Writer.TryComplete();
                        }
                        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            if (TryRecover(segment, ex))
                            {
                                ModBase.Log(ex, $"[Download] 分段下载缓慢或失败，正在重试：{_owner._url}", ModBase.LogLevel.Debug);
                                await Task.Delay(Random.Shared.Next(250, 1001), _cancellation.Token).ConfigureAwait(false);
                                continue;
                            }

                            Interlocked.CompareExchange(ref _failure, ex, null);
                            _segments.Writer.TryComplete(ex);
                            _cancellation.Cancel();
                            throw;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_failure is not null)
            {
                throw _failure;
            }
        }

        private bool TryRecover(DownloadSegment segment, Exception exception)
        {
            if (exception is not (SlowSegmentException or IOException or HttpRequestException or TaskCanceledException or
                DownloadRequestTimeoutException))
                return false;
            if (++segment.FailureCount > MaxSegmentRetries || segment.Remaining <= 0)
                return false;

            if (segment.Remaining >= SlowSplitThreshold && Volatile.Read(ref _nextSegmentId) < MaxExpandedSegmentCount)
            {
                var splitStart = segment.CurrentOffset + segment.Remaining / 2;
                var split = new DownloadSegment(Interlocked.Increment(ref _nextSegmentId), splitStart, segment.End);
                segment.End = splitStart - 1;
                _segments.Writer.TryWrite(segment);
                _segments.Writer.TryWrite(split);
                Interlocked.Increment(ref _outstandingSegments);
            }
            else
            {
                _segments.Writer.TryWrite(segment);
            }

            return true;
        }

        private async Task DownloadSegmentAsync(DownloadSegment segment, CancellationToken cancellationToken)
        {
            using var connection = await DownloadResourceManager.AcquireConnectionAsync(_owner._url, cancellationToken)
                .ConfigureAwait(false);
            Interlocked.Increment(ref _activeConnections);
            var startedAt = Stopwatch.GetTimestamp();
            var attemptBytes = 0L;
            try
            {
                using var request = _owner.CreateRequest(HttpMethod.Get, DownloadRequestKind.RangeSegment);
                request.Headers.Range = new RangeHeaderValue(segment.CurrentOffset, segment.End);
                using var response = await FileDownloader.SendDownloadRequestAsync(_owner._url, request,
                    cancellationToken).ConfigureAwait(false);
                ValidateRangeResponse(response, segment);

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using var bufferLease = await DownloadResourceManager.ReserveBufferAsync(BufferSize, cancellationToken)
                    .ConfigureAwait(false);
                var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                try
                {
                    while (segment.Remaining > 0)
                    {
                        int read;
                        readTimeout.CancelAfter(ReadTimeoutMilliseconds);
                        try
                        {
                            read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), readTimeout.Token)
                                    .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                                                               readTimeout.IsCancellationRequested)
                        {
                            throw new SlowSegmentException("分段在等待数据时超时");
                        }

                        if (read == 0)
                            break;
                        read = (int)Math.Min(read, segment.Remaining);

                        await DownloadResourceManager.ThrottleAsync(read, cancellationToken).ConfigureAwait(false);
                        await RandomAccess.WriteAsync(_fileHandle, buffer.AsMemory(0, read), segment.CurrentOffset,
                            cancellationToken).ConfigureAwait(false);
                        DownloadResourceManager.RecordDownloadedBytes(read);

                        segment.Downloaded += read;
                        attemptBytes += read;
                        var downloaded = Interlocked.Add(ref _downloadedBytes, read);
                        ReportRateAndProgress(segment, attemptBytes, startedAt, downloaded);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                if (segment.Remaining > 0)
                    throw new IOException($"分段 {segment.Id} 提前结束，还剩 {segment.Remaining} 字节");

                AddCompletedRate(attemptBytes, startedAt);
            }
            finally
            {
                _rates.TryRemove(segment.Id, out _);
                var activeConnections = Interlocked.Decrement(ref _activeConnections);
                ReportProgress(Interlocked.Read(ref _downloadedBytes), activeConnections, force: true);
            }
        }

        private void ReportRateAndProgress(DownloadSegment segment, long attemptBytes, long startedAt, long downloaded)
        {
            var now = Stopwatch.GetTimestamp();
            var elapsedSeconds = Math.Max(0.001, (double)(now - startedAt) / Stopwatch.Frequency);
            if (now - segment.LastRateSampleTick >= Stopwatch.Frequency / 2)
            {
                segment.LastRateSampleTick = now;
                _rates[segment.Id] = new RateSample(attemptBytes / elapsedSeconds, now);

                if (IsSignificantlySlow(segment, attemptBytes, startedAt, now))
                    throw new SlowSegmentException("分段速度明显低于其他活跃连接");
            }

            ReportProgress(downloaded, Volatile.Read(ref _activeConnections));
        }

        private bool IsSignificantlySlow(DownloadSegment segment, long attemptBytes, long startedAt, long now)
        {
            if (ModNet.NetTaskSpeedLimitHigh > 0 || segment.Remaining < MinimumRestartRemaining)
                return false;

            var elapsedSeconds = (double)(now - startedAt) / Stopwatch.Frequency;
            if (elapsedSeconds < SlowCheckSeconds)
                return false;

            var rates = _rates.Where(pair => pair.Key != segment.Id &&
                                             now - pair.Value.UpdatedAt <= Stopwatch.Frequency * 2 &&
                                             pair.Value.BytesPerSecond > 0)
                .Select(pair => pair.Value.BytesPerSecond)
                .ToArray();
            if (rates.Length == 0)
                rates = _completedRates.Where(rate => rate > 0).ToArray();
            if (rates.Length == 0)
                return false;

            Array.Sort(rates);
            var median = rates[rates.Length / 2];
            var fastest = rates[^1];
            var ownRate = attemptBytes / elapsedSeconds;

            // 确保至少一个分片的速度更高，当所有连接都被同一个源限速时，不会让所有分片一起反复重启
            var hasClearlyFasterSegment = fastest >= ClearlyFastSegmentBytesPerSecond && ownRate * 4 < fastest;
            var relativelySlow = median >= ClearlyFastSegmentBytesPerSecond && ownRate * 4 < median;
            var absolutelySlow = ownRate < SlowSpeedBytesPerSecond && hasClearlyFasterSegment;
            return relativelySlow || absolutelySlow;
        }

        private void AddCompletedRate(long attemptBytes, long startedAt)
        {
            var elapsedSeconds = Math.Max(0.001, (double)(Stopwatch.GetTimestamp() - startedAt) / Stopwatch.Frequency);
            _completedRates.Enqueue(attemptBytes / elapsedSeconds);
            while (_completedRates.Count > 8)
                _completedRates.TryDequeue(out _);
        }

        private void ReportProgress(long downloaded, int activeConnections, bool force = false)
        {
            lock (_progressLock)
            {
                var now = Stopwatch.GetTimestamp();
                if (!force && now - _lastProgressTick < Stopwatch.Frequency / 5)
                    return;

                var elapsed = Math.Max(1L, now - _lastProgressTick);
                var speed = Math.Max(0L, (long)((downloaded - _lastProgressBytes) * (double)Stopwatch.Frequency / elapsed));
                _owner.NotifyProgress(downloaded, activeConnections, force);
                if (_owner._trackedFile is not null)
                    _owner._trackedFile.Speed = speed;

                _lastProgressBytes = downloaded;
                _lastProgressTick = now;
            }
        }

        private void ValidateRangeResponse(HttpResponseMessage response, DownloadSegment segment)
        {
            if (response.StatusCode != HttpStatusCode.PartialContent)
                throw new RangeNotSupportedException($"服务器未按 Range 返回分段，状态码：{(int)response.StatusCode}");

            var range = response.Content.Headers.ContentRange;
            if (range?.Unit != "bytes" || range.From != segment.CurrentOffset || range.To != segment.End ||
                range.Length != _totalSize)
                throw new RangeNotSupportedException("服务器返回的 Content-Range 与请求分段不一致");
        }

        private readonly record struct RateSample(double BytesPerSecond, long UpdatedAt);
    }

    private sealed class SlowSegmentException(string message) : IOException(message);
    private sealed class RangeNotSupportedException(string message) : Exception(message);
}

using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Network.Loaders;

public class LoaderDownload : ModLoader.LoaderBase
{
    public ModBase.SafeList<PCL.Network.DownloadFile> files;
    private int _fileRemain;
    private readonly object _fileRemainLock = new();
    private CancellationTokenSource? _cancellationTokenSource;
    public int FailCount { get; set; }

    public override double Progress
    {
        get => State >= ModBase.LoadState.Finished ? 1 : (files.Any() ? files.Average(file => file.Progress) : 0);
        set => throw new Exception("文件下载不允许指定进度");
    }

    public LoaderDownload(string name, List<PCL.Network.DownloadFile> fileTasks)
    {
        base.name = name;
        files = new ModBase.SafeList<PCL.Network.DownloadFile>(fileTasks ?? new List<PCL.Network.DownloadFile>());
    }

    public void RefreshStat() { }

    public override void Start(object input = null, bool isForceRestart = false)
    {
        if (input is List<PCL.Network.DownloadFile> inputFiles)
            files = new ModBase.SafeList<PCL.Network.DownloadFile>(inputFiles);

        lock (lockState)
        {
            if (State == ModBase.LoadState.Loading)
                return;
            State = ModBase.LoadState.Loading;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        lock (_fileRemainLock)
        {
            _fileRemain = files.Count;
        }

        ModNet.NetManager.Start(this);

        ModBase.RunInNewThread(() => Run(_cancellationTokenSource.Token), $"DL/{Uuid}");
    }

    private void Run(CancellationToken cancellationToken)
    {
        try
        {
            if (!files.Any())
            {
                OnFinish();
                return;
            }

            var exceptions = new ConcurrentQueue<Exception>();
            using var semaphore = new SemaphoreSlim(GetMaxParallelFiles());
            var tasks = files.Select(async file =>
            {
                var entered = false;
                try
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    entered = true;
                    await ProcessFileAsync(file, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    file.AddError(ex);
                    file.State = PCL.Network.NetState.Interrupted;
                    exceptions.Enqueue(ex);
                    _cancellationTokenSource?.Cancel();
                }
                finally
                {
                    if (entered)
                        semaphore.Release();
                }
            }).ToList();

            Task.WhenAll(tasks).GetAwaiter().GetResult();
            if (!exceptions.IsEmpty)
                OnFail(exceptions.ToList());
        }
        catch (OperationCanceledException)
        {
            Abort();
        }
        catch (Exception ex)
        {
            OnFail(new List<Exception> { ex });
        }
    }

    private int GetMaxParallelFiles()
    {
        return Math.Max(1, Math.Min(files.Count,
            Math.Clamp(ModNet.NetTaskConnectionLimit, 1, ModNet.NetTaskConnectionLimitMax)));
    }

    private async Task ProcessFileAsync(PCL.Network.DownloadFile file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        file.RegisterLoader(this);

        if (State >= ModBase.LoadState.Finished)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(file.LocalPath) ?? throw new IOException("下载路径无效"));
        var checker = file.Check;
        if (checker?.canUseExistsFile == true && File.Exists(file.LocalPath))
        {
            var checkResult = string.IsNullOrEmpty(checker.hash)
                ? checker.Check(file.LocalPath)
                : await Task.Run(() => checker.Check(file.LocalPath), cancellationToken).ConfigureAwait(false);
            if (checkResult is null)
            {
                file.IsCopy = true;
                file.State = PCL.Network.NetState.Finished;
                try { file.TotalSize = new FileInfo(file.LocalPath).Length; }
                catch (IOException) { file.TotalSize = -1; }
                file.DownloadedBytes = file.TotalSize;
                file.Speed = 0;
                file.ActiveThreads = 0;
                OnFileFinish(file);
                return;
            }
        }

        file.State = PCL.Network.NetState.Connecting;
        // 批量任务中未知大小的文件直接下载，避免小文件逐个产生一次 Range 探测。
        var expectedSize = file.Check?.actualSize ?? -1;
        var enableParallelChunks = files.Count <= 1 || expectedSize >= AdaptiveRangeDownloader.SmallFileThreshold;
        await FileDownloader.DownloadAsync(file.Urls, file.LocalPath, file.UseBrowserUserAgent, file.CustomUserAgent,
            cancellationToken, enableParallelChunks, file).ConfigureAwait(false);
        try { file.TotalSize = new FileInfo(file.LocalPath).Length; }
        catch (IOException) { file.TotalSize = -1; }
        file.IsUnknownSize = file.TotalSize < 0;
        file.DownloadedBytes = Math.Max(0, file.TotalSize);
        file.Speed = 0;
        file.ActiveThreads = 0;
        file.State = PCL.Network.NetState.Finished;
        OnFileFinish(file);
    }

    public void OnFileFinish(PCL.Network.DownloadFile file)
    {
        lock (_fileRemainLock)
        {
            _fileRemain -= 1;
            if (_fileRemain > 0)
                return;
        }

        OnFinish();
    }

    public void OnFinish()
    {
        RaisePreviewFinish();
        lock (lockState)
        {
            if (State > ModBase.LoadState.Loading)
                return;
            State = ModBase.LoadState.Finished;
        }

        ModNet.NetManager.Finish(this);
    }

    public void OnFileFail(PCL.Network.DownloadFile file)
    {
        var errors = file.Errors;
        OnFail(errors.Count > 0
            ? errors.ToList()
            : new List<Exception> { new Exception($"文件下载失败：{file.LocalPath}") });
    }

    public void OnFail(List<Exception> exList)
    {
        lock (lockState)
        {
            if (State > ModBase.LoadState.Loading)
                return;
            Error = exList.FirstOrDefault() ?? new Exception("未知下载错误");
            State = ModBase.LoadState.Failed;
        }

        FailCount += exList.Count;
        foreach (var file in files.Where(file => file.State < PCL.Network.NetState.Finished))
        {
            file.State = PCL.Network.NetState.Interrupted;
            file.Speed = 0;
            file.ActiveThreads = 0;
            file.AddErrors(exList);
        }

        ModNet.NetManager.Finish(this);
    }

    public override void Abort()
    {
        lock (lockState)
        {
            if (State >= ModBase.LoadState.Finished)
                return;
            State = ModBase.LoadState.Aborted;
        }

        _cancellationTokenSource?.Cancel();
        foreach (var file in files.Where(file => file.State < PCL.Network.NetState.Finished))
        {
            file.State = PCL.Network.NetState.Interrupted;
            file.Speed = 0;
            file.ActiveThreads = 0;
        }

        ModNet.NetManager.Finish(this);
    }
}

namespace PCL.Network;

public sealed class NetManager
{
    private static readonly Lazy<NetManager> _instance = new(() => new NetManager());
    public static NetManager Instance => _instance.Value;

    public Dictionary<string, DownloadFile> Files { get; } = new();
    public object LockFiles { get; } = new();
    public ModBase.SafeList<PCL.Network.Loaders.LoaderDownload> Tasks { get; } = new();
    public object LockRemain { get; } = new();
    public int FileRemain
    {
        get
        {
            lock (LockFiles)
                return Files.Values.Count(file => file.State != NetState.Finished);
        }
    }
    public object LockDone { get; } = new();
    public long DownloadDone
    {
        get
        {
            lock (LockDone)
                return field;
        }
        set
        {
            lock (LockDone)
                field = value;
        }
    }

    public long Speed
    {
        get => DownloadResourceManager.DownloadSpeed;
    }

    public int ConnectionCount
    {
        get => DownloadResourceManager.ActiveConnectionCount;
    }

    [Obsolete("请使用 ConnectionCount。")]
    public int ThreadCount => ConnectionCount;

    public void Start(PCL.Network.Loaders.LoaderDownload task)
    {
        lock (LockFiles)
        {
            Tasks.Remove(task);
            Tasks.Add(task);
            foreach (var file in task.files)
                Files[file.LocalPath] = file;
        }
    }

    public void Finish(PCL.Network.Loaders.LoaderDownload task)
    {
        lock (LockFiles)
        {
            Tasks.Remove(task);
            foreach (var file in task.files)
                Files.Remove(file.LocalPath);
        }
    }
}

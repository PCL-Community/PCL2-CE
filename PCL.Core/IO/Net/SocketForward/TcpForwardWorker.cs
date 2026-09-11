using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Logging;

namespace PCL.Core.IO.Net.SocketForward;
public sealed class TcpForwardWorker : IDisposable
{
    private readonly TcpForwardConfig _cfg;
    private volatile CancellationTokenSource _cts;
    private readonly SemaphoreSlim _connectionSemaphore;
    private Task? _workerTask;
    private readonly ConcurrentDictionary<Guid, Task> _subWorkerTask = [];
    private const string ModuleName = "TcpForward";

    internal TcpForwardWorker(TcpForwardConfig cfg)
    {
        _cfg = cfg;
        _cts = new CancellationTokenSource();
        _connectionSemaphore = new SemaphoreSlim(_cfg.MaxConnection, _cfg.MaxConnection);
    }

    public IPEndPoint? LocalEndPoint { get; private set; }

    public int ActiveConnectionCount => _cfg.MaxConnection - _connectionSemaphore.CurrentCount;
    private readonly Lock _operationLock = new();

    public void Start()
    {
        lock (_operationLock)
        {
            if (_workerTask is { IsCompleted: false }) return;

            _cts = new CancellationTokenSource();
            _workerTask = _WorkerFunc();
            _workerTask.ContinueWith(x =>
            {
                if (x.IsFaulted)
                    LogWrapper.Error(x.Exception, ModuleName, "工作线程出现错误");
            });
        }
    }

    public void Stop()
    {
        lock (_operationLock)
        {
            if (_workerTask is not { IsCompleted: false }) return;
            _cts.Cancel();
            var oldCts = _cts;
            // ReSharper disable once MethodSupportsCancellation
            _ = Task.WhenAll([_workerTask, .. _subWorkerTask.Values]).ContinueWith(x =>
            {
                oldCts.Dispose();
                if (x.IsFaulted) LogWrapper.Error(x.Exception, ModuleName, "后台关闭工作线程时遇到错误抛出");
            });
            LogWrapper.Info(ModuleName, "TCP 端口转发已停止，转发线程将在后台陆续关闭");
            _subWorkerTask.Clear();
        }
    }

    private async Task _WorkerFunc()
    {
        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        listener.NoDelay = true;
        listener.ReceiveBufferSize = (int)_cfg.BufferSize;
        listener.SendBufferSize = (int)_cfg.BufferSize;

        if (!IPAddress.TryParse(_cfg.LocalHost, out var localAddress))
            throw new InvalidOperationException("出现意料之外的本地监听地址");
        listener.Bind(new IPEndPoint(localAddress, _cfg.LocalPort));
        listener.Listen();

        // 暴露给外部用
        if (listener.LocalEndPoint is not IPEndPoint endPoint) throw new InvalidCastException("出现了意外的转换操作");
        LocalEndPoint = endPoint;
        
        LogWrapper.Info(ModuleName, $"TCP 端口转发已启动，监听 {endPoint}，目标 tcp://{_cfg.RemoteHost}:{_cfg.RemotePort}");
        
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await listener.AcceptAsync(_cts.Token).ConfigureAwait(false);

                if (await _connectionSemaphore.WaitAsync(0).ConfigureAwait(false)) // 是否还能创建新连接
                {
                    // 投递给转发线程
                    var taskGuid = Guid.NewGuid();
                    _subWorkerTask.TryAdd(taskGuid, _HandleConnectionAsync(clientSocket, _cts.Token)
                        .ContinueWith(x =>
                        {
                            if (x.IsFaulted) LogWrapper.Error(x.Exception, ModuleName, "连接处理线程出现错误");
                            try
                            {
                                _subWorkerTask.TryRemove(taskGuid, out _);
                                _connectionSemaphore.Release();
                            } catch (ObjectDisposedException) {/* ignore */}
                        }));
                }
                else
                {
                    clientSocket.CloseGracefully();
                    LogWrapper.Warn(ModuleName, $"已达到最大连接数限制({_cfg.MaxConnection})，拒绝新连接");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogWrapper.Error(ex, ModuleName, $"接受连接时发生错误");
                await Task.Delay(500).ConfigureAwait(false);
            }
        }
    }

    private async Task _HandleConnectionAsync(Socket clientSocket, CancellationToken cancellationToken)
    {
        var connectionId = Guid.NewGuid();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Socket? targetSocket = null;

        try
        {
            LogWrapper.Info(ModuleName, $"接受来自 {clientSocket.RemoteEndPoint} 的连接");

            // 连接到目标服务器
            targetSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            targetSocket.NoDelay = true;
            targetSocket.ReceiveBufferSize = (int)_cfg.BufferSize;
            targetSocket.SendBufferSize = (int)_cfg.BufferSize;

            await targetSocket.ConnectAsync(_cfg.RemoteHost!, _cfg.RemotePort, cancellationToken).ConfigureAwait(false);

            LogWrapper.Info(ModuleName, $"开始 TCP 转发 {clientSocket.RemoteEndPoint} <-> {targetSocket.RemoteEndPoint}({connectionId})");

            // 使用高性能的 SocketAsyncEventArgs 进行双向转发
            var forwardTask1 = _ForwardDataAsync(clientSocket, targetSocket, _cfg.BufferSize, cts.Token);
            var forwardTask2 = _ForwardDataAsync(targetSocket, clientSocket, _cfg.BufferSize, cts.Token);

            // 等待任意一个方向的数据转发完成
            await Task.WhenAny(forwardTask1, forwardTask2).ConfigureAwait(false);
            await cts.CancelAsync().ConfigureAwait(false);

            LogWrapper.Debug(ModuleName, $"TCP 转发 {connectionId} 已结束");
        }
        catch (OperationCanceledException)
        {
            // 取消操作，正常退出
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, ModuleName, $"处理连接 {connectionId} 时发生错误");
        }
        finally
        {
            clientSocket.CloseGracefully();
            targetSocket?.CloseGracefully();
        }
    }

    private static async Task _ForwardDataAsync(Socket source, Socket destination, uint bufferSize, CancellationToken cancellationToken)
    {
        using var bufferOwner = MemoryPool<byte>.Shared.Rent((int)bufferSize);
        var buffer = bufferOwner.Memory;
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await source.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0) break; // 连接已关闭

                var bytesSend = 0;
                while (bytesRead > bytesSend)
                {
                    var currentSend= await destination.SendAsync(buffer[bytesSend..bytesRead], SocketFlags.None, cancellationToken)
                        .ConfigureAwait(false);
                    if (currentSend == 0) break; // 对端关闭
                    bytesSend += currentSend;
                }

                if (bytesRead != bytesSend) break; // 外层关闭
            }
        }
        catch {/* 忽略错误 */}
    }

    private bool _disposed;

    public void Dispose()
    {
        _Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void _Dispose(bool disposing)
    {
        if (!disposing) return;
        if (_disposed) return;
        Stop();
        _connectionSemaphore.Dispose();
        _disposed = true;
    }

    ~TcpForwardWorker()
    {
        _Dispose(false);
    }
}
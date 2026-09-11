using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO.Net;

namespace PCL.Core.Link;

public class BroadcastLocal(string description, int localPort) : IDisposable
{
    private Socket? _broadcastSocket;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public void Start()
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();
        _isRunning = true;

        // 启动 UDP 广播任务
        _ = Task.Run(() => _RunUdpBroadcastAsync(_cts.Token), _cts.Token);

        Console.WriteLine($"开始向本地 Minecraft 客户端广播，端口: {localPort}");
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _cts?.Cancel();
        _isRunning = false;

        _broadcastSocket?.CloseGracefully();

        Console.WriteLine("停止向本地 Minecraft 客户端广播");
    }

    private async Task _RunUdpBroadcastAsync(CancellationToken cancellationToken)
    {
        try
        {
            _broadcastSocket = new Socket(SocketType.Dgram, ProtocolType.Udp)
            {
                DualMode = true
            };

            var buffer = Encoding.UTF8.GetBytes($"[MOTD]{description}[/MOTD][AD]{localPort}[/AD]");
            // 向本地地址发送，而不是广播地址
            var localEndpoint = new IPEndPoint(IPAddress.Loopback, 4445);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _broadcastSocket.SendToAsync(new ArraySegment<byte>(buffer), SocketFlags.None, localEndpoint);
                    await Task.Delay(1500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"UDP 本地广播错误: {ex.Message}");
                    await Task.Delay(5000, cancellationToken); // 出错后等待 5 秒再重试
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UDP 本地广播任务发生错误: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _broadcastSocket.CloseGracefully();
        GC.SuppressFinalize(this);
    }
}
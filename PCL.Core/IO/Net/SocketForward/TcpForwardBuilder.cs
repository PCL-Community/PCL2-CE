using System;
using System.Net;
using PCL.Core.Utils.Exts;

namespace PCL.Core.IO.Net.SocketForward;

public class TcpForwardBuilder
{
    private readonly TcpForwardConfig _cfg = new();

    public TcpForwardWorker Build()
    {
        ArgumentNullException.ThrowIfNull(_cfg.RemoteHost);
        ArgumentOutOfRangeException.ThrowIfZero(_cfg.RemotePort);
        
        if (_cfg.LocalHost.IsNullOrWhiteSpace()) _cfg.LocalHost = IPAddress.Loopback.ToString();
        
        return new TcpForwardWorker(_cfg);
    }

    public TcpForwardBuilder BindLocalRandom()
    {
        _cfg.LocalHost = IPAddress.Loopback.ToString();
        _cfg.LocalPort = 0;
        
        return this;
    }

    public TcpForwardBuilder BindLocal(ushort port)
    {
        ArgumentOutOfRangeException.ThrowIfZero(port);
        
        _cfg.LocalHost = IPAddress.Loopback.ToString();
        _cfg.LocalPort = port;

        return this;
    }

    public TcpForwardBuilder SetRemote(string host, ushort port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfZero(port);

        _cfg.RemoteHost = host;
        _cfg.RemotePort = port;

        return this;
    }

    public TcpForwardBuilder SetRemote(IPEndPoint remote)
    {
        ArgumentNullException.ThrowIfNull(remote);

        _cfg.RemoteHost = remote.Address.ToString();
        _cfg.RemotePort = (ushort) remote.Port;
        
        return this;
    }

    public TcpForwardBuilder SetRemote(IPAddress host, ushort port)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentOutOfRangeException.ThrowIfZero(port);

        _cfg.RemoteHost = host.ToString();
        _cfg.RemotePort = port;

        return this;
    }

    public TcpForwardBuilder SetBufferSize(uint size)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, TcpForwardConfig.MaxBufferSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, TcpForwardConfig.MinBufferSize);

        _cfg.BufferSize = size;

        return this;
    }

    public TcpForwardBuilder SetMaxAllowedActiveConnection(ushort maxConnectionCount)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maxConnectionCount);

        _cfg.MaxConnection = maxConnectionCount;

        return this;
    }
}
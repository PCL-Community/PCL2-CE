using System.Net.Sockets;

namespace PCL.Core.IO.Net;

public static class SocketExtensions
{
    public static void CloseGracefully(this Socket? socket)
    {
        if (socket is null) return;

        if (socket is { IsBound: false, Connected: false })
        {
            socket.Dispose();
            return;
        }
        
        if (socket.Connected)
        {
            try
            {
                    socket.Shutdown(SocketShutdown.Both);
            }
            catch { /* 忽略关闭时的任何错误 */ }
        }
        
        try
        {
            socket.Close();
        }
        catch { /* 忽略关闭时的任何错误 */ }
        
    }
}
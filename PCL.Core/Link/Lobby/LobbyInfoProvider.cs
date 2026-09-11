using System;
using System.Numerics;
using PCL.Core.App;
using PCL.Core.IO.Net;
using PCL.Core.IO.Net.SocketForward;
using PCL.Core.Link.Natayark;
using PCL.Core.Logging;
using PCL.Core.Utils;
using PCL.Core.Utils.Exts;

namespace PCL.Core.Link.Lobby;

public static class LobbyInfoProvider
{
    public static bool IsLobbyAvailable { get; set; } = false;
    public static bool AllowCustomName { get; set; } = false;
    public static bool RequiresLogin { get; set; } = true;
    public static bool RequiresRealName { get; set; } = true;
    public static int ProtocolVersion { get; set; } = 6;

    public static BroadcastLocal? McBroadcast { get; internal set; }
    public static TcpForwardWorker? McForward { get; internal set; }

    public class LobbyInfo
    {
        public required string OriginalCode { get; init; }
        public required LobbyType Type { get; init; }
        public required string NetworkName { get; init; }
        public required string NetworkSecret { get; init; }

        /// <summary>
        /// 远程 IP 地址，需要先解析大厅类型再填充
        /// </summary>
        public string? Ip { get; init; }

        /// <summary>
        /// 目标游戏端口
        /// </summary>
        public required int Port { get; init; }
    }

    public enum LobbyType
    {
        // ReSharper disable once InconsistentNaming
        PCLCE,
        Terracotta
    }

    /// <summary>
    /// 目标大厅
    /// </summary>
    public static LobbyInfo? TargetLobby { get; set; }
    public static int JoinerLocalPort { get; set; }

    /// <summary>
    /// 解析大厅编号，并返回 LobbyInfo 对象。若解析失败则返回 null。
    /// </summary>
    public static LobbyInfo? ParseCode(string code)
    {
        code = code.Trim().ToUpper();
        if (string.IsNullOrWhiteSpace(code) || code.Length < 9 || !code.IsASCII())
        {
            LogWrapper.Error("Link", "无效的大厅编号: " + code);
            return null;
        }

        if (code.Split("-".ToCharArray()).Length != 5) // PCL CE 大厅
        {
            try
            {
                var info = code.FromB32ToB10();
                return new LobbyInfo
                {
                    OriginalCode = code,
                    NetworkName = info[..8],
                    NetworkSecret = info[8..10],
                    Port = int.Parse(info[10..]),
                    Type = LobbyType.PCLCE,
                    Ip = "10.114.51.41"
                };
            }
            catch (Exception ex)
            {
                LogWrapper.Error(ex, "Link", "大厅编号解析失败，可能是无效的 PCL CE 大厅编号: " + code);
            }
        }
        else // 陶瓦
        {
            var matches = code.RegexSearch(RegexPatterns.TerracottaId);
            if (matches.Count == 0)
            {
                LogWrapper.Error("Link", "大厅编号解析失败，可能是无效的陶瓦大厅编号: " + code);
                return null;
            }

            foreach (var match in matches)
            {
                var codeString = match.Replace("I", "1").Replace("O", "0").Replace("-", "");
                BigInteger value = 0;
                var checking = 0;
                const string baseChars = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
                for (var i = 0; i <= 23; i++) 
                { 
                    var j = baseChars.IndexOf(codeString[i]);
                    value += BigInteger.Parse(j.ToString()) * BigInteger.Pow(34, i);
                    checking = (j + checking) % 34;
                }

                if (checking != baseChars.IndexOf(codeString[24])) { return null; }
                var port = (int)(value % 65536);
                if (port < 100) { return null; }
                return new LobbyInfo
                {
                    OriginalCode = code,
                    NetworkName = codeString.Substring(0, 15).ToLower(),
                    NetworkSecret = codeString.Substring(15, 10).ToLower(),
                    Port = port,
                    Type = LobbyType.Terracotta,
                    Ip = "10.144.144.1"
                };
            }
        }
        return null;
    }

    /// <summary>
    /// 获取用于联机显示的用户名
    /// </summary>
    public static string? GetUsername() => AllowCustomName
        ? Config.Link.Username.ReplaceNullOrEmpty(NatayarkProfileManager.NaidProfile.Username)
        : NatayarkProfileManager.NaidProfile.Username;
}

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using PCL.Core.App;
using PCL.Core.Minecraft.Profile;
using PCL.Core.App.Localization;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Lobby;
using PCL.Core.Link.McPing;
using PCL.Core.Link.McPing.Model;
using PCL.Core.Link.Natayark;
using PCL.Core.Logging;
using PCL.Core.UI;
using PCL.Core.Utils.OS;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

public static class ModLink
{
    #region 大厅操作

    public static bool LobbyPrecheck()
    {
        if (!LobbyInfoProvider.IsLobbyAvailable)
        {
            HintService.Hint(Lang.Text("Link.Mod.LobbyUnavailable"), HintType.Error);
            return false;
        }

        if (ProfileService.Current is not null)
            if (ProfileService.Current.UserName.Contains('|'))
            {
                HintService.Hint(Lang.Text("Link.Mod.InvalidPlayerId"));
                return false;
            }

        if (LobbyInfoProvider.RequiresLogin)
        {
            if (string.IsNullOrWhiteSpace(States.Link.NaidRefreshToken))
            {
                HintService.Hint(Lang.Text("Link.Mod.LoginFirst"), HintType.Error);
                return false;
            }

            try
            {
                NatayarkProfileManager.GetNaidDataAsync((string)States.Link.NaidRefreshToken, true)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                ModBase.Log("[Link] 刷新 Natayark ID 信息失败，需要重新登录");
                HintService.Hint(Lang.Text("Link.Mod.ReLoginRequired"), HintType.Error);
                return false;
            }

            var waitCount = 0;
            while (string.IsNullOrWhiteSpace(NatayarkProfileManager.NaidProfile.Username))
            {
                if (waitCount > 30)
                    break;
                Thread.Sleep(500);
                waitCount += 1;
            }

            if (string.IsNullOrWhiteSpace(NatayarkProfileManager.NaidProfile.Username))
            {
                HintService.Hint(Lang.Text("Link.Mod.NaidFetchFailed"), HintType.Error);
                return false;
            }

            if (LobbyInfoProvider.RequiresRealName && !NatayarkProfileManager.NaidProfile.IsRealNamed)
            {
                HintService.Hint(Lang.Text("Link.Mod.RealNameRequired"), HintType.Error);
                return false;
            }

            if (NatayarkProfileManager.NaidProfile.Status != 0)
            {
                HintService.Hint(Lang.Text("Link.Mod.AccountBanned"), HintType.Error);
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(Config.Link.Username) && string.IsNullOrWhiteSpace(NatayarkProfileManager.NaidProfile.Username))
        {
            HintService.Hint(Lang.Text("Link.Mod.UsernameOrLogin"), HintType.Error);
            return false;
        }

        if (ETController.Precheck() == 1)
        {
            HintService.Hint(Lang.Text("Link.Mod.DownloadingDeps"));
            DownloadEasyTier();
            return false;
        }

        if (dlEasyTierLoader is not null)
        {
            if (dlEasyTierLoader.State == ModBase.LoadState.Loading)
            {
                HintService.Hint(Lang.Text("Link.Mod.EasyTierNotReady"));
                return false;
            }

            if (dlEasyTierLoader.State == ModBase.LoadState.Failed ||
                dlEasyTierLoader.State == ModBase.LoadState.Aborted)
            {
                HintService.Hint(Lang.Text("Link.Mod.DownloadingEasyTier"));
                DownloadEasyTier();
                return false;
            }
        }

        return true;
    }

    #endregion

    #region 端口查找

    public class PortFinder
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern int GetExtendedTcpTable(nint pTcpTable, ref int dwOutBufLen, bool bOrder, int ulAf,
            int tableClass, int reserved);

        public static List<int> GetProcessPort(int dwProcessId)
        {
            var ports = new List<int>();
            var tcpTable = nint.Zero;
            var dwSize = 0;
            int dwRetVal;

            if (dwProcessId == 0) return ports;

            dwRetVal = GetExtendedTcpTable(nint.Zero, ref dwSize, true, 2, 3, 0);
            if (dwRetVal != 0 && dwRetVal != 122) // 122 表示缓冲区不足
                return ports;

            tcpTable = Marshal.AllocHGlobal(dwSize);
            try
            {
                if (GetExtendedTcpTable(tcpTable, ref dwSize, true, 2, 3, 0) != 0) return ports;

                var tablePtr = tcpTable;
                var dwNumEntries = Marshal.ReadInt32(tablePtr);
                tablePtr = nint.Add(tablePtr, 4);

                for (int i = 0, loopTo = dwNumEntries - 1; i <= loopTo; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(tablePtr);
                    if (row.dwOwningPid == dwProcessId)
                        ports.Add((row.dwLocalPort >> 8) | ((row.dwLocalPort & 0xFF) << 8)); // 转换端口号
                    tablePtr = nint.Add(tablePtr, Marshal.SizeOf<MIB_TCPROW_OWNER_PID>());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tcpTable);
            }

            return ports;
        }

        // 定义需要的结构和常量
        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW_OWNER_PID
        {
            public int dwState;
            public int dwLocalAddr;
            public int dwLocalPort;
            public int dwRemoteAddr;
            public int dwRemotePort;
            public int dwOwningPid;
        }
    }

    #endregion

    #region Minecraft 实例探测

    public static async Task<List<Tuple<int, McPingResult, string>>> MCInstanceFindingAsync()
    {
        // Java 进程 PID 查询
        var pIDLookupResult = new List<string>();
        var javaNames = new List<string>();
        javaNames.Add("java");
        javaNames.Add("javaw");

        foreach (var TargetJava in javaNames)
        {
            var javaProcesses = Process.GetProcessesByName(TargetJava);
            ModBase.Log($"[MCDetect] 找到 {TargetJava} 进程 {javaProcesses.Length} 个");

            if (javaProcesses is null || javaProcesses.Length == 0)
            {
            }
            else
            {
                foreach (var p in javaProcesses)
                {
                    ModBase.Log("[MCDetect] 检测到 Java 进程，PID: " + p.Id);
                    pIDLookupResult.Add(p.Id.ToString());
                }
            }
        }

        var res = new List<Tuple<int, McPingResult, string>>();
        try
        {
            if (pIDLookupResult.Count == 0)
                return res;
            var lookupList = new List<Tuple<int, int>>();
            foreach (var pid in pIDLookupResult)
            {
                var infos = new List<Tuple<int, int>>();
                var ports = PortFinder.GetProcessPort(int.Parse(pid));
                foreach (var port in ports)
                    infos.Add(new Tuple<int, int>(port, int.Parse(pid)));
                lookupList.AddRange(infos);
            }

            ModBase.Log($"[MCDetect] 获取到端口数量 {lookupList.Count}");
            // 并行查找本地，超时 3s 自动放弃
            var checkTasks = lookupList.Select(
            lookup => Task.Run(async () =>
            {
                ModBase.Log($"[MCDetect] 找到疑似端口，开始验证：{lookup}");
                using (var test = McPingServiceFactory.CreateService("127.0.0.1", lookup.Item1, 3000))
                {
                    try
                    {
                        var info = await test.PingAsync();
                        var launcher = GetLauncherBrand(lookup.Item2);
                        if (!string.IsNullOrWhiteSpace(info?.Version.Name))
                        {
                            ModBase.Log($"[MCDetect] 端口 {lookup} 为有效 Minecraft 世界");
                            res.Add(new Tuple<int, McPingResult, string>(lookup.Item1, info, launcher));
                            return Task.CompletedTask;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex.InnerException is ObjectDisposedException)
                        {
                            ModBase.Log($"[McDetect] {lookup} 验证超时，已强制断开连接，将尝试旧版检测");
                        }
                        else
                        {
                            ModBase.Log(ex, $"[McDetect] {lookup} 验证出错，将尝试旧版检测");
                        }
                    }
                }
                using (var test = McPingServiceFactory.CreateLegacyService("127.0.0.1", lookup.Item1, 3000))
                {
                    try
                    {
                        var info = await test.PingAsync();
                        if (!string.IsNullOrWhiteSpace(info?.Version.Name))
                        {
                            ModBase.Log($"[MCDetect] 端口 {lookup} 为有效 Minecraft 世界");
                            res.Add(new Tuple<int, McPingResult, string>(lookup.Item1, info, string.Empty));
                            return Task.CompletedTask;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex.InnerException is ObjectDisposedException)
                        {
                            ModBase.Log($"[McDetect] {lookup} 验证超时，已强制断开连接");
                        }
                        else
                        {
                            ModBase.Log(ex, $"[McDetect] {lookup} 验证出错");
                        }
                    }
                }
                return Task.CompletedTask;
            })).ToArray();
            await Task.WhenAll(checkTasks);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[MCDetect] 获取端口信息错误");
        }

        return res;
    }

    public static string GetLauncherBrand(int pid)
    {
        try
        {
            var cmd = ProcessInterop.GetCommandLine(pid);
            if (cmd.Contains("-Dminecraft.launcher.brand="))
                return cmd.AfterFirst("-Dminecraft.launcher.brand=").BeforeFirst("-").TrimEnd('\'', ' ');

            return cmd.AfterFirst("--versionType ").BeforeFirst("-").TrimEnd('\'', ' ');
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[MCDetect] 检测 PID {pid} 进程的启动参数失败");
            return "";
        }
    }

    #endregion

    #region EasyTier

    public static ModLoader.LoaderCombo<JsonObject> dlEasyTierLoader;

    public static int DownloadEasyTier()
    {
        var dlTargetPath = $"{ModBase.pathTemp}EasyTier\\EasyTier-{ETInfoProvider.ETVersion}.zip";

        Basics.RunInNewThread(() =>
        {
            try
            {
                // Initialize loaders
                var loaders = new List<ModLoader.LoaderBase>();

                // Setup download addresses
                var architecture = SystemInfo.IsArm64System ? "arm64" : "x86_64";
                var addresses = new List<string>
                {
                    $"https://staticassets.naids.com/resources/pclce/static/easytier/easytier-windows-{architecture}-v{ETInfoProvider.ETVersion}.zip",
                    $"https://s3.pysio.online/pcl2-ce/static/easytier/easytier-windows-{architecture}-v{ETInfoProvider.ETVersion}.zip"
                };

                // 1. Download EasyTier
                loaders.Add(new LoaderDownload(Lang.Text("Link.Mod.Task.DownloadEasyTier"), new List<DownloadFile>
                {
                    new(addresses.ToArray(), dlTargetPath, new ModBase.FileChecker(1024 * 64))
                }) { ProgressWeight = 15 });

                // 2. Extract files
                loaders.Add(new ModLoader.LoaderTask<int, int>(Lang.Text("Link.Mod.Task.ExtractFiles"), _ =>
                    ModBase.ExtractFile(dlTargetPath,
                        Path.Combine(Paths.SharedLocalData, "EasyTier", ETInfoProvider.ETVersion))
                ) { block = true });

                // 3. Cleanup
                loaders.Add(new ModLoader.LoaderTask<int, int>(Lang.Text("Link.Mod.Task.CleanCache"), _ =>
                {
                    File.Delete(dlTargetPath);
                    CleanupEasyTierCache();
                }));

                // 4. Update UI hint
            loaders.Add(new ModLoader.LoaderTask<int, int>(Lang.Text("Link.Mod.Task.RefreshUi"), _ =>
                HintWrapper.Show(Lang.Text("Link.Mod.DownloadComplete"), HintTheme.Success)
                ) { show = false });

                // Start loader combo
                dlEasyTierLoader = new ModLoader.LoaderCombo<JsonObject>(Lang.Text("Link.Mod.Task.InitLobby"), loaders);
                dlEasyTierLoader.Start();

                // Taskbar and UI notification
                ModLoader.LoaderTaskbarAdd(dlEasyTierLoader);
                ModMain.frmMain.BtnExtraDownload.ShowRefresh();
                ModMain.frmMain.BtnExtraDownload.Ribble();
            }
            catch (Exception ex)
            {
                // Error handling with concise English logs
                LogWrapper.Warn(ex, "Failed to download EasyTier dependency files");
                HintWrapper.Show(Lang.Text("Link.Mod.DownloadEasyTierFailed"), HintTheme.Error);
            }
        });

        return 0;
    }

    private static void CleanupEasyTierCache()
    {
        var subDirs = Directory.GetDirectories(Path.Combine(Paths.SharedLocalData, "EasyTier"));
        foreach (var folderPath in subDirs)
        {
            var name = Path.GetFileName(folderPath);
            if (!name.Equals(ETInfoProvider.ETVersion))
                try
                {
                    Directory.Delete(folderPath, true);
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "[Link] 清理旧版本 EasyTier 出错");
                }
        }
    }

    #endregion
}

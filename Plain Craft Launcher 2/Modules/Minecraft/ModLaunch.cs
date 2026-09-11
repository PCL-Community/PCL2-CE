using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Minecraft;
using PCL.Core.Minecraft.Launch.Utils;
using PCL.Core.Utils;
using PCL.Core.Utils.OS;
using PCL.Core.Utils.Secret;
using PCL.Network;
using PCL.Core.IO.Net.Http;
using PCL.Core.Minecraft.IdentityModel;
using PCL.Core.Minecraft.Profile;
using PCL.Core.Minecraft.Profile.Authentication;
using PCL.Core.Minecraft.Profile.Models;
using System.Globalization;
using PCL.Core.Logging;

namespace PCL;

public static class ModLaunch
{
    public const string mesaLoaderWindowsVersion = "26.0.4";

    #region 预检测

    private static void McLaunchPrecheck()
    {
        if (Config.Debug.AddRandomDelay)
            Thread.Sleep(RandomUtils.NextInt(100, 2000));
        // 检查路径
        if (ModInstanceList.McMcInstanceSelected.PathIndie.Contains("!") ||
            ModInstanceList.McMcInstanceSelected.PathIndie.Contains(";"))
            throw new Exception(Lang.Text("Minecraft.Launch.Precheck.InvalidPathChars", ModInstanceList.McMcInstanceSelected.PathIndie));
        if (ModInstanceList.McMcInstanceSelected.PathInstance.Contains("!") ||
            ModInstanceList.McMcInstanceSelected.PathInstance.Contains(";"))
            throw new Exception(Lang.Text("Minecraft.Launch.Precheck.InvalidPathChars", ModInstanceList.McMcInstanceSelected.PathInstance));
        if (ModBase.IsUtf8CodePage() && !States.Hint.NonAsciiGamePath &&
            !ModInstanceList.McMcInstanceSelected.PathInstance.IsASCII())
        {
            var userChoice = ModMain.MyMsgBox(
                Lang.Text("Minecraft.Launch.Precheck.NonAsciiPath.Message", ModInstanceList.McMcInstanceSelected.Name),
                Lang.Text("Minecraft.Launch.Precheck.NonAsciiPath.Title"), Lang.Text("Minecraft.Launch.Precheck.NonAsciiPath.Continue"), Lang.Text("Minecraft.Launch.Precheck.NonAsciiPath.Back"), Lang.Text("Common.Hint.DoNotShowAgain"));
            if (userChoice == 2) throw new Exception("$$");
            if (userChoice == 3) States.Hint.NonAsciiGamePath = true;
        }

        // 检查实例
        if (ModInstanceList.McMcInstanceSelected is null)
            throw new Exception(Lang.Text("Minecraft.Launch.Precheck.NoInstance"));
        ModInstanceList.McMcInstanceSelected.Load();
        if (ModInstanceList.McMcInstanceSelected.state == McInstanceState.Error)
            throw new Exception(Lang.Text("Minecraft.Launch.Precheck.InstanceError", ModInstanceList.McMcInstanceSelected.Desc));
        // 检查输入信息
        var checkResult = "";
        ModBase.RunInUiWait(() => checkResult = ProfileUi.IsProfileValid());
        var selectedProfile = ProfileService.Current;
        if (selectedProfile is null) // 没选档案
        {
            checkResult = Lang.Text("Minecraft.Launch.Precheck.NoProfile");
        }
        else if (ModInstanceList.McMcInstanceSelected.Info.HasLabyMod ||
                 Config.InstanceAuth.LoginRequirementSolution[ModInstanceList.McMcInstanceSelected?.PathInstance] == 1) // 要求正版验证
        {
            if (selectedProfile.ProfileType != ProfileType.Microsoft) checkResult = Lang.Text("Minecraft.Launch.Precheck.RequireMicrosoft");
        }
        else if (Config.InstanceAuth.LoginRequirementSolution[ModInstanceList.McMcInstanceSelected?.PathInstance] == 2) // 要求第三方验证
        {
            if (selectedProfile.ProfileType is not (ProfileType.Authlib or ProfileType.YggdrasilConnect))
                checkResult = Lang.Text("Minecraft.Launch.Precheck.RequireThirdParty");
            else if (selectedProfile.Server?.BeforeLast("/authserver") !=
                     Config.InstanceAuth.AuthServerAddress[ModInstanceList.McMcInstanceSelected?.PathInstance])
                checkResult = Lang.Text("Minecraft.Launch.Precheck.AuthServerMismatch");
        }
        else if (Config.InstanceAuth.LoginRequirementSolution[ModInstanceList.McMcInstanceSelected?.PathInstance] == 3) // 要求正版验证或第三方验证
        {
            if (selectedProfile.ProfileType == ProfileType.Offline)
                checkResult = Lang.Text("Minecraft.Launch.Precheck.RequireMicrosoftOrThirdParty");
            else if ((selectedProfile.ProfileType is ProfileType.Authlib or ProfileType.YggdrasilConnect) &&
                     selectedProfile.Server?.BeforeLast("/authserver") !=
                     Config.InstanceAuth.AuthServerAddress[ModInstanceList.McMcInstanceSelected?.PathInstance])
                checkResult = Lang.Text("Minecraft.Launch.Precheck.AuthServerMismatch");
        }

        if (!string.IsNullOrEmpty(checkResult))
            throw new ArgumentException(checkResult);

#if BETA
        if (currentLaunchOptions?.SaveBatch is null) // 保存脚本时不提示
        {
            ModBase.RunInNewThread(() =>
            {
                switch (States.System.LaunchCount)
                {
                    case 10:
                    case 20:
                    case 40:
                    case 60:
                    case 80:
                    case 100:
                    case 120:
                    case 150:
                    case 200:
                    case 250:
                    case 300:
                    case 350:
                    case 400:
                    case 500:
                    case 600:
                    case 700:
                    case 800:
                    case 900:
                    case 1000:
                    case 1200:
                    case 1400:
                    case 1600:
                    case 1800:
                    case 2000:
                        if (ModMain.MyMsgBox(
                                Lang.Text("Minecraft.Launch.Donate.Message", States.System.LaunchCount),
                                Lang.Text("Minecraft.Launch.Donate.Title", States.System.LaunchCount),
                                Lang.Text("Minecraft.Launch.Donate.Support"),
                                Lang.Text("Minecraft.Launch.Donate.Decline")) == 1)
                        {
                            ModBase.OpenWebsite("https://afdian.com/a/LTCat");
                        }
                        break;
                }
            }, "Donate");
        }
#endif
        
        #if DEBUG || DEBUGCI
        return;
        #endif

        // 正版购买提示
        if (!ProfileService.HasMicrosoftProfile)
        {
            if (Lang.IsFeaturesUnrestricted)
            {
                if (ModMain.MyMsgBox(
                        Lang.Text("Minecraft.Launch.PurchaseHint.Message"),
                        Lang.Text("Minecraft.Launch.PurchaseHint.Title"), Lang.Text("Minecraft.Launch.PurchaseHint.Purchase"), Lang.Text("Minecraft.Launch.PurchaseHint.Later")) ==
                    1)
                    ModBase.OpenWebsite(
                        "https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj");
            }
            else
            {                
                switch (ModMain.MyMsgBox(Lang.Text("Minecraft.Launch.AccountVerification.Message"), 
                            Lang.Text("Minecraft.Launch.AccountVerification.Title"), 
                            Lang.Text("Minecraft.Launch.AccountVerification.Purchase"), 
                            Lang.Text("Minecraft.Launch.AccountVerification.Demo"), 
                            Lang.Text("Minecraft.Launch.AccountVerification.Back"),
                            button1Action: () =>
                                ModBase.OpenWebsite(
                                    "https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj")))
                {
                    case 2:
                    {
                        HintService.Hint(Lang.Text("Minecraft.Launch.DemoMode"), HintType.Error);
                        currentLaunchOptions.ExtraArgs.Add("--demo");
                        break;
                    }
                    case 3:
                    {
                        throw new Exception("$$");
                    }
                }

            }

        }
    }

    #endregion

    #region 开始

    public static bool isLaunching;
    public static McLaunchOptions currentLaunchOptions;

    public class McLaunchOptions
    {
        /// <summary>
        ///     额外的启动参数。
        /// </summary>
        public List<string> ExtraArgs = new();

        /// <summary>
        ///     强行指定启动的 MC 实例。
        ///     默认值：Nothing。使用 McInstanceCurrent。
        /// </summary>
        public McInstance instance = null;

        /// <summary>
        ///     是否为 “测试游戏” 按钮启动的游戏。
        ///     如果是，则显示游戏实时日志。
        /// </summary>
        public bool IsTest = false;

        /// <summary>
        ///     将启动脚本保存到该地址，然后取消启动。这同时会改变启动时的提示等。
        ///     默认值：Nothing。不保存。
        /// </summary>
        public string SaveBatch = null;

        /// <summary>
        ///     强制指定在启动后进入的服务器 IP。
        ///     默认值：Nothing。使用实例设置的值。
        /// </summary>
        public string ServerIp = null;

        /// <summary>
        ///     指定在启动之后进入的存档名称。
        ///     默认值：Nothing。使用实例设置的值。
        /// </summary>
        public string WorldName = null;
    }

    /// <summary>
    ///     尝试启动 Minecraft。必须在 UI 线程调用。
    ///     返回是否实际开始了启动（如果没有，则一定弹出了错误提示）。
    /// </summary>
    public static bool McLaunchStart(McLaunchOptions options = null)
    {
        isLaunching = true;
        currentLaunchOptions = options ?? new McLaunchOptions();
        // 预检查
        if (!ModBase.RunInUi())
            throw new Exception("McLaunchStart 必须在 UI 线程调用！");
        if (mcLaunchLoader.State == ModBase.LoadState.Loading)
        {
            HintService.Hint(Lang.Text("Minecraft.Launch.Error.AlreadyLaunching"), HintType.Error);
            isLaunching = false;
            return false;
        }

        // 强制切换需要启动的实例
        if (currentLaunchOptions.instance is not null &&
            ModInstanceList.McMcInstanceSelected != currentLaunchOptions.instance)
        {
            McLaunchLog("在启动前切换到实例 " + currentLaunchOptions.instance.Name);
            // 检查实例
            currentLaunchOptions.instance.Load();
            if (currentLaunchOptions.instance.state == McInstanceState.Error)
            {
                HintService.Hint(Lang.Text("Minecraft.Launch.Error.CannotLaunch", currentLaunchOptions.instance.Desc), HintType.Error);
                isLaunching = false;
                return false;
            }

            // 切换实例
            ModInstanceList.McMcInstanceSelected = currentLaunchOptions.instance;
            States.Game.SelectedInstance = ModInstanceList.McMcInstanceSelected.Name;
            ModMain.frmLaunchLeft.RefreshButtonsUI();
            ModMain.frmLaunchLeft.RefreshPage(false);
        }

        ModMain.frmMain.AprilGiveup();
        // 禁止进入实例选择页面（否则就可以在启动中切换 McInstanceCurrent 了）
        ModMain.frmMain.pageStack =
            ModMain.frmMain.pageStack.Where(p => p.page != FormMain.PageType.InstanceSelect).ToList();
        // 实际启动加载器
        mcLaunchLoader.Start(options, true);
        return true;
    }

    /// <summary>
    ///     记录启动日志。
    /// </summary>
    public static void McLaunchLog(string text)
    {
        text = McLogFilter.FilterUserName(McLogFilter.FilterAccessToken(text, '*'), '*');
        ModBase.RunInUi(() =>
            ModMain.frmLaunchRight.LabLog.Text += "\r\n" + "[" + TimeUtils.GetTimeNow() + "] " + text);
        ModBase.Log("[Launch] " + text);
    }

    // 启动状态切换
    public static ModLoader.LoaderTask<McLaunchOptions, object> mcLaunchLoader = new("Loader Launch", McLaunchStart)
        { OnStateChanged = a => McLaunchState((dynamic)a) };

    public static ModLoader.LoaderCombo<object> mcLaunchLoaderReal;
    public static Process mcLaunchProcess;
    public static ModWatcher.Watcher mcLaunchWatcher;

    private static void McLaunchState(ModLoader.LoaderTask<McLaunchOptions, object> loader)
    {
        switch (mcLaunchLoader.State)
        {
            case ModBase.LoadState.Finished:
            case ModBase.LoadState.Failed:
            case ModBase.LoadState.Waiting:
            case ModBase.LoadState.Aborted:
            {
                ModMain.frmLaunchLeft.PageChangeToLogin();
                break;
            }
            case ModBase.LoadState.Loading:
            {
                // 在预检测结束后再触发动画
                ModMain.frmLaunchRight.LabLog.Text = "";
                break;
            }
        }
    }

    /// <summary>
    ///     指定启动中断时的提示文本。若不为 Nothing 则会显示为绿色。
    /// </summary>
    private static string abortHint;

    // 实际的启动方法
    private static void McLaunchStart(ModLoader.LoaderTask<McLaunchOptions, object> loader)
    {
        // 开始动画
        ModBase.RunInUiWait(ModMain.frmLaunchLeft.PageChangeToLaunching);
        // 预检测（预检测的错误将直接抛出）
        try
        {
            McLaunchPrecheck();
            McLaunchLog("预检测已通过");
        }
        catch (Exception ex)
        {
            if (!ex.Message.StartsWithF("$$"))
                HintService.Hint(Lang.Text("Minecraft.Launch.Precheck.Failed.WithDetail", ex.Message), HintType.Error);
            throw;
        }

        // 正式加载
        try
        {
            // 构造主加载器
            var loaders = new List<ModLoader.LoaderBase>
            {
                new ModLoader.LoaderTask<int, int>(Lang.Text("Minecraft.Launch.Stage.GetJava"), McLaunchJava) { ProgressWeight = 4d, block = false },
                mcLoginLoader,
                new ModLoader.LoaderCombo<string>(Lang.Text("Minecraft.Launch.Stage.CompleteFiles"),
                        ModDownload.DlClientFix(ModInstanceList.McMcInstanceSelected, false,
                            ModDownload.AssetsIndexExistsBehaviour.DownloadInBackground))
                    { ProgressWeight = 15d, show = false },
                new ModLoader.LoaderTask<string, List<ModLibrary.McLibToken>>(Lang.Text("Minecraft.Launch.Stage.GetArguments"), McLaunchArgumentMain)
                    { ProgressWeight = 2d },
                new ModLoader.LoaderTask<List<ModLibrary.McLibToken>, int>(Lang.Text("Minecraft.Launch.Stage.ExtractNatives"), McLaunchNatives)
                    { ProgressWeight = 2d },
                new ModLoader.LoaderTask<int, int>(Lang.Text("Minecraft.Launch.Stage.PreLaunch"), _ => McLaunchPrerun()) { ProgressWeight = 1d },
                new ModLoader.LoaderTask<int, int>(Lang.Text("Minecraft.Launch.Stage.CustomCommand"), McLaunchCustom) { ProgressWeight = 1d },
                new ModLoader.LoaderTask<int, Process>(Lang.Text("Minecraft.Launch.Stage.StartProcess"), McLaunchRun) { ProgressWeight = 2d },
                new ModLoader.LoaderTask<Process, int>(Lang.Text("Minecraft.Launch.Stage.WaitWindow"), McLaunchWait) { ProgressWeight = 1d },
                new ModLoader.LoaderTask<int, int>(Lang.Text("Minecraft.Launch.Stage.End"), _ => McLaunchEnd()) { ProgressWeight = 1d }
            }; // .ProgressWeight = 15, .Block = False

            var launchLoader = new ModLoader.LoaderCombo<object>(Lang.Text("Minecraft.Launch.Stage.Root"), loaders) { show = false };
            if (mcLoginLoader.State == ModBase.LoadState.Finished)
                mcLoginLoader.State = ModBase.LoadState.Waiting; // 要求重启登录主加载器，它会自行决定是否启动副加载器
            // 等待加载器执行并更新 UI
            mcLaunchLoaderReal = launchLoader;
            abortHint = null;
            launchLoader.Start();
            // 任务栏进度条
            ModLoader.LoaderTaskbarAdd(launchLoader);
            while (launchLoader.State == ModBase.LoadState.Loading)
            {
                ModMain.frmLaunchLeft.Dispatcher.Invoke(ModMain.frmLaunchLeft.LaunchingRefresh);
                Thread.Sleep(100);
            }

            ModMain.frmLaunchLeft.Dispatcher.Invoke(ModMain.frmLaunchLeft.LaunchingRefresh);
            // 成功与失败处理
            switch (launchLoader.State)
            {
                case ModBase.LoadState.Finished:
                {
                    HintService.Hint(Lang.Text("Minecraft.Launch.Success", ModInstanceList.McMcInstanceSelected.Name), HintType.Success);
                    break;
                }
                case ModBase.LoadState.Aborted:
                {
                    if (abortHint is null)
                        HintService.Hint(currentLaunchOptions?.SaveBatch is null ? Lang.Text("Minecraft.Launch.Cancelled") : Lang.Text("Minecraft.Launch.ExportScript.Cancelled"));
                    else
                        HintService.Hint(abortHint, HintType.Success);

                    break;
                }
                case ModBase.LoadState.Failed:
                {
                    throw launchLoader.Error;
                }

                default:
                {
                    throw new Exception(Lang.Text("Minecraft.Launch.Error.InvalidState", ModBase.GetStringFromEnum(launchLoader.State)));
                }
            }

            isLaunching = false;
        }
        catch (Exception ex)
        {
            var currentEx = ex;
            while (currentEx is not null)
            {
                if (currentEx.Message.StartsWithF("$"))
                {
                    // 若有以 $ 开头的错误信息，则以此为准显示提示
                    // 若错误信息为 $$，则不提示
                    if (currentEx.Message != "$$")
                        ModMain.MyMsgBox(
                            Lang.Text("Minecraft.Launch.Error.SpecialMessage.WithDetail",
                                currentEx.Message.TrimStart('$')),
                            currentLaunchOptions?.SaveBatch is null
                                ? Lang.Text("Launch.Error.Title")
                                : Lang.Text("Launch.Error.ExportScriptTitle"));
                    throw;
                }

                if (currentEx.InnerException is null)
                    break;

                // 检查下一级错误
                currentEx = currentEx.InnerException;
            }

            // 没有特殊处理过的错误信息
            McLaunchLog("错误：" + ex);
            ModBase.Log(
                ex,
                currentLaunchOptions?.SaveBatch is null
                    ? "Minecraft launch failed"
                    : "Export script failed",
                ModBase.LogLevel.Msgbox,
                currentLaunchOptions?.SaveBatch is null
                    ? Lang.Text("Launch.Error.Title")
                    : Lang.Text("Launch.Error.ExportScriptTitle"),
                userSummary: currentLaunchOptions?.SaveBatch is null
                    ? Lang.Text("Minecraft.Launch.Error.LaunchFailed")
                    : Lang.Text("Minecraft.Launch.Error.ExportScriptFailed"));
            throw;
        }
    }

    #endregion

    #region 档案验证

    #region 主模块

    // 登录方式
    public enum McLoginType
    {
        Legacy = 1,
        Auth = 2,
        Ms = 3
    }

    // 各个登录方式的对应数据
    public abstract class McLoginData
    {
        /// <summary>
        ///     登录方式。
        /// </summary>
        public McLoginType LoginType;

        public override bool Equals(object obj)
        {
            return obj is not null && obj.GetHashCode() == GetHashCode();
        }
    }

    #region 第三方验证类型

    public class McLoginServer : McLoginData
    {
        /// <summary>
        ///     登录服务器基础地址。
        /// </summary>
        public string BaseUrl;

        /// <summary>
        ///     登录方式的描述字符串，如 “正版”、“统一通行证”。
        /// </summary>
        public string Description;

        /// <summary>
        ///     是否在本次登录中强制要求玩家重新选择角色，目前仅对 Authlib-Injector 生效。
        /// </summary>
        public bool ForceReselectProfile = false;

        /// <summary>
        ///     是否已经存在该验证信息，用于判断是否为新增档案。
        /// </summary>
        public bool IsExist = false;

        /// <summary>
        ///     登录密码。
        /// </summary>
        public string Password;

        /// <summary>
        ///     登录用户名。
        /// </summary>
        public string UserName;
        public string DiscoveryAddress;
        public ProfileType ProviderType = ProfileType.Authlib;

        public McLoginServer(McLoginType type)
        {
            this.LoginType = type;
        }

        public override int GetHashCode()
        {
            return (int)Math.Round(ModBase.GetHash(UserName + Password + BaseUrl + (int)LoginType) %
                                   (decimal)int.MaxValue);
        }
    }

    #endregion

    #region 正版验证类型

    public class McLoginMs : McLoginData
    {
        public McLoginMs()
        {
            LoginType = McLoginType.Ms;
        }

        public override int GetHashCode()
        {
            return (int)Math.Round(ModBase.GetHash(LoginType.ToString()) % (decimal)int.MaxValue);
        }
    }

    #endregion

    #region 离线验证类型

    public class McLoginLegacy : McLoginData
    {
        /// <summary>
        ///     若采用正版皮肤，则为该皮肤名。
        /// </summary>
        public string SkinName;

        /// <summary>
        ///     皮肤种类。
        /// </summary>
        public int SkinType;

        /// <summary>
        ///     登录用户名。
        /// </summary>
        public string UserName;

        /// <summary>
        ///     UUID。
        /// </summary>
        public string Uuid;

        public McLoginLegacy()
        {
            LoginType = McLoginType.Legacy;
        }

        public override int GetHashCode()
        {
            return (int)Math.Round(
                ModBase.GetHash(UserName + SkinType + SkinName + (int)LoginType) % (decimal)int.MaxValue);
        }
    }

    #endregion

    // 登录返回结果
    public struct McLoginResult
    {
        public string Name;
        public string Uuid;
        public string AccessToken;
        public string Type;
        public string ClientToken;

        /// <summary>
        ///     进行微软登录时返回的 profile 信息。
        /// </summary>
        public string ProfileJson;
    }

    // 登录主模块加载器
    public static ModLoader.LoaderTask<McLoginData, McLoginResult> mcLoginLoader =
        new(Lang.Text("Minecraft.Launch.Stage.Login"), McLoginStart, McLoginInput, ThreadPriority.BelowNormal)
            { reloadTimeout = 1, ProgressWeight = 15d, block = false };

    public static McLoginData McLoginInput()
    {
        McLoginData loginData = null;
        try
        {
            loginData = ProfileUi.GetLoginData();
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Minecraft.Launch.Login.Error.Input"),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Minecraft.Launch.Login.Error.Input"));
        }

        return loginData;
    }

    private static void McLoginStart(ModLoader.LoaderTask<McLoginData, McLoginResult> data)
    {
        ModBase.Log("[Profile] 开始加载选定档案");
        // 校验登录信息
        var checkResult = ProfileUi.IsProfileValid();
        if (!string.IsNullOrEmpty(checkResult))
            throw new ArgumentException(checkResult);
        // 获取对应加载器
        ModLoader.LoaderBase loader = null;
        switch (data.input.LoginType)
        {
            case McLoginType.Ms:
            {
                loader = mcLoginMsLoader;
                break;
            }
            case McLoginType.Legacy:
            {
                loader = mcLoginLegacyLoader;
                break;
            }
            case McLoginType.Auth:
            {
                loader = mcLoginAuthLoader;
                break;
            }
        }

        // 尝试加载
        loader.WaitForExit(data.input, mcLoginLoader, data.isForceRestarting);
        data.output = (McLoginResult)((dynamic)loader).output;
        ModBase.RunInUi(() => ModMain.frmLaunchLeft.RefreshPage(false)); // 刷新自动填充列表
        ModBase.Log("[Profile] 选定档案加载完成");
    }

    #endregion

    // 各个登录方式的主对象与输入构造
    public static ModLoader.LoaderTask<McLoginMs, McLoginResult> mcLoginMsLoader =
        new("Loader Login Ms", McLoginMsStart) { reloadTimeout = 1 };

    public static ModLoader.LoaderTask<McLoginLegacy, McLoginResult> mcLoginLegacyLoader =
        new("Loader Login Legacy", McLoginLegacyStart);

    public static ModLoader.LoaderTask<McLoginServer, McLoginResult> mcLoginAuthLoader =
        new("Loader Login Auth", McLoginServerStartNew) { reloadTimeout = 1000 * 60 * 10 };

    #region 正版验证

    private static void McLoginMsStart(ModLoader.LoaderTask<McLoginMs, McLoginResult> data)
    {
        var existing = ProfileService.Current?.ProfileType == ProfileType.Microsoft ? ProfileService.Current : null;
        LogWrapper.Info("Profile",$"验证方式：正版（{(existing is null ? "尚未登录" : existing.UserName)}）");
        data.Progress = 0.05d;
        McProfile stored;
        try
        {
            stored = ProfileService.AuthenticateAsync(ProfileType.Microsoft, new AuthenticationRequest
            {
                ForceRefresh = data.isForceRestarting,
                DeviceCodeHandler = ProfileUi.ShowDeviceCodeLoginAsync,
                RefreshFailureHandler = _ConfirmMicrosoftRefreshFailureAsync
            }, existing, select: true, data.AbortedToken).GetAwaiter().GetResult();
        }
        catch (IdentityModelAuthenticationException ex) when (
            ProfileUi.HandleMicrosoftXstsError(ex) ||
            ProfileUi.HandleMicrosoftNotOwnedError(ex) ||
            ProfileUi.HandleMicrosoftCreateProfileError(ex))
        {
            throw new IdentityModelException("$$", ex);
        }
        ThrowIfAborted(data);
        ProfileService.IsCreatingProfile = false;

        data.output = new McLoginResult
        {
            AccessToken = stored.AccessToken,
            Name = stored.UserName,
            Uuid = stored.Uuid,
            Type = "Microsoft",
            ClientToken = stored.ClientToken,
            ProfileJson = stored.RawJson
        };

        data.Progress = 0.98d;
        LogWrapper.Info("Profile","正版验证完成");
    }

    /// <summary>
    ///     检查是否被中断
    /// </summary>
    private static void ThrowIfAborted(ModLoader.LoaderTask<McLoginMs, McLoginResult> data)
    {
        if (data.IsAborted)
            throw new ThreadInterruptedException();
    }

    private static Task<bool> _ConfirmMicrosoftRefreshFailureAsync(Exception exception, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        LogWrapper.Info("Profile","获取正版 OAuth Token 失败：" + exception);
        var reuseCachedProfile = false;
        ModBase.RunInUiWait(() =>
        {
            if (!isLaunching) return;
            reuseCachedProfile = ModMain.MyMsgBox(
                Lang.Text("Minecraft.Launch.Login.RefreshAccountFailed.Message"),
                Lang.Text("Minecraft.Launch.Login.RefreshAccountFailed.Title"), Lang.Text("Minecraft.Launch.Login.Continue"),
                Lang.Text("Common.Action.Cancel")) == 1;
        });
        return Task.FromResult(reuseCachedProfile);
    }


    #endregion

    #region 第三方验证

    private static void McLoginServerStartNew(ModLoader.LoaderTask<McLoginServer, McLoginResult> data)
    {
        var input = data.input;
        LogWrapper.Info("Profile","验证方式：" + input.Description);
        data.Progress = 0.1d;
        var existing = input.IsExist && !ProfileService.IsCreatingProfile ? ProfileService.Current : null;
        var stored = ProfileService.AuthenticateAsync(input.ProviderType, new AuthenticationRequest
        {
            Server = input.BaseUrl,
            DiscoveryAddress = input.DiscoveryAddress,
            Username = input.UserName,
            Password = input.Password,
            ForceRefresh = data.isForceRestarting,
            ForceReselectProfile = input.ForceReselectProfile,
            DeviceCodeHandler = input.ProviderType == ProfileType.YggdrasilConnect
                ? ProfileUi.ShowDeviceCodeLoginAsync
                : null,
            ProfileSelector = (candidates, _) => Task.FromResult(_SelectAuthProfile(candidates))
        }, existing, select: true, data.AbortedToken).GetAwaiter().GetResult();
        if (data.IsAborted) throw new ThreadInterruptedException();

        data.output = new McLoginResult
        {
            AccessToken = stored.AccessToken,
            ClientToken = stored.ClientToken,
            Uuid = stored.Uuid,
            Name = stored.UserName,
            Type = "Auth"
        };
        ProfileService.IsCreatingProfile = false;
        data.Progress = 0.98d;
        LogWrapper.Info("Profile","第三方验证完成");
    }

    private static AuthenticationCandidate? _SelectAuthProfile(IReadOnlyList<AuthenticationCandidate> candidates)
    {
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];
        AuthenticationCandidate? selected = null;
        ModBase.RunInUiWait(() =>
        {
            var controls = candidates.Select(item => (IMyRadio)new MyRadioBox { Text = item.Name }).ToList();
            var index = ModMain.MyMsgBoxSelect(controls, Lang.Text("Minecraft.Launch.Login.Auth.SelectProfile"));
            if (index is >= 0 and < 100000) selected = candidates[index.Value];
        });
        return selected;
    }

    #endregion

    #region 离线验证

    private static void McLoginLegacyStart(ModLoader.LoaderTask<McLoginLegacy, McLoginResult> data)
    {
        var input = data.input;
        LogWrapper.Info("Profile",$"验证方式：离线（{input.UserName}, {input.Uuid}）");
        data.Progress = 0.1d;
        {
            ref var withBlock = ref data.output;
            withBlock.Name = input.UserName;
            withBlock.Uuid = input.Uuid;
            withBlock.Type = "Legacy";
        }
        // 将结果扩展到所有项目中
        data.output.AccessToken = data.output.Uuid;
        data.output.ClientToken = data.output.Uuid;
    }

    #endregion

    #endregion

    #region Java 处理

    public static JavaEntry mcLaunchJavaSelected;

    private static void McLaunchJava(ModLoader.LoaderTask<int, int> task)
    {
        var minVer = new Version(0, 0, 0, 0);
        var maxVer = new Version(999, 999, 999, 999);

        // MC 大版本检测
        if ((!ModInstanceList.McMcInstanceSelected.Info.Valid &&
             ModInstanceList.McMcInstanceSelected.releaseTime >= new DateTime(2024, 4, 2)) ||
            (ModInstanceList.McMcInstanceSelected.Info.Valid &&
             ModInstanceList.McMcInstanceSelected.Info.vanilla >= new Version(20, 0, 5)))
        {
            // 1.20.5+ (24w14a+)：至少 Java 21
            if (ModBase.modeDebug)
                ModBase.Log("[Launch] [Debug] MC 1.20.5+ (24w14a+) 要求至少 Java 21");
            minVer = new Version(21, 0, 0, 0);
        }
        else if ((!ModInstanceList.McMcInstanceSelected.Info.Valid &&
                  ModInstanceList.McMcInstanceSelected.releaseTime >= new DateTime(2021, 11, 16)) ||
                 (ModInstanceList.McMcInstanceSelected.Info.Valid &&
                  ModInstanceList.McMcInstanceSelected.Info.vanilla.Major >= 18))
        {
            // 1.18 pre2+：至少 Java 17
            if (ModBase.modeDebug)
                ModBase.Log("[Launch] [Debug] MC 1.18 pre2+ 要求至少 Java 17");
            minVer = new Version(17, 0, 0, 0);
        }
        else if ((!ModInstanceList.McMcInstanceSelected.Info.Valid &&
                  ModInstanceList.McMcInstanceSelected.releaseTime >= new DateTime(2021, 5, 11)) ||
                 (ModInstanceList.McMcInstanceSelected.Info.Valid &&
                  ModInstanceList.McMcInstanceSelected.Info.vanilla.Major >= 17))
        {
            // 1.17+ (21w19a+)：至少 Java 16
            if (ModBase.modeDebug)
                ModBase.Log("[Launch] [Debug] MC 1.17+ (21w19a+) 要求至少 Java 16");
            minVer = new Version(16, 0, 0, 0);
        }
        else if (ModInstanceList.McMcInstanceSelected.releaseTime.Year >= 2017) // Minecraft 1.12 与 1.11 的分界线正好是 2017 年，太棒了
        {
            // 1.12+：至少 Java 8
            if (ModBase.modeDebug)
                ModBase.Log("[Launch] [Debug] MC 1.12+ 要求至少 Java 8");
            minVer = new Version(1, 8, 0, 0);
        }
        else if (ModInstanceList.McMcInstanceSelected.releaseTime <= new DateTime(2013, 5, 1) &&
                 ModInstanceList.McMcInstanceSelected.releaseTime.Year >= 2001) // 避免某些版本写个 1960 年
        {
            // 1.5.2-：最高 Java 8
            if (ModBase.modeDebug)
                ModBase.Log("[Launch] [Debug] MC 1.5.2- 要求最高 Java 12");
            maxVer = new Version(1, 8, 999, 999);
        }

        // 原版 26+：获取 Mojang 要求的 Java 版本
        string recommendedComponent = null;
        var recommendedCode =
            ModInstanceList.McMcInstanceSelected.JsonObject?["javaVersion"]?["majorVersion"]?.ToObject<int>() ??
            ModInstanceList.McMcInstanceSelected.JsonVersion?["java_version"]?.ToObject<int>() ?? 0;
        if (recommendedCode >= 22)
        {
            McLaunchLog("Mojang 要求至少使用 Java " + recommendedCode);
            minVer = new Version(recommendedCode, 0, 0, 0);
            recommendedComponent =
                ModInstanceList.McMcInstanceSelected.JsonObject?["javaVersion"]?["component"]?.ToString() ??
                ModInstanceList.McMcInstanceSelected.JsonVersion?["java_component"]?.ToString();
            if (string.IsNullOrEmpty(recommendedComponent))
                recommendedComponent = null;
        }

        // OptiFine 检测
        if (ModInstanceList.McMcInstanceSelected.Info.HasOptiFine && ModInstanceList.McMcInstanceSelected.Info.Valid) // 不管非标准版本
        {
            if (ModInstanceList.McMcInstanceSelected.Info.vanilla.Major < 7)
            {
                // <1.7：至多 Java 8
                maxVer = new Version(1, 8, 999, 999);
            }
            else if (ModInstanceList.McMcInstanceSelected.Info.vanilla.Major >= 8 &&
                     ModInstanceList.McMcInstanceSelected.Info.vanilla.Major < 12)
            {
                // 1.8 - 1.11：必须恰好 Java 8
                minVer = new Version(1, 8, 0, 0);
                maxVer = new Version(1, 8, 999, 999);
            }
            else if (ModInstanceList.McMcInstanceSelected.Info.vanilla.Major == 12)
            {
                // 1.12：最高 Java 8
                maxVer = new Version(1, 8, 999, 999);
            }
        }

        // Forge 检测
        if (ModInstanceList.McMcInstanceSelected.Info.HasForge)
        {
            if (ModInstanceList.McMcInstanceSelected.Info.vanilla >= new Version(6, 0, 1) &&
                ModInstanceList.McMcInstanceSelected.Info.vanilla <= new Version(7, 0, 2))
            {
                // 1.6.1 - 1.7.2：必须 Java 7
                minVer = new Version(1, 7, 0, 0) > minVer ? new Version(1, 7, 0, 0) : minVer;
                maxVer = new Version(1, 7, 999, 999) < maxVer ? new Version(1, 7, 999, 999) : maxVer;
            }
            else if (ModInstanceList.McMcInstanceSelected.Info.vanilla.Major <= 12 ||
                     !ModInstanceList.McMcInstanceSelected.Info.Valid) // 非标准版本
            {
                // <=1.12：Java 8
                maxVer = new Version(1, 8, 999, 999);
            }
            else if (ModInstanceList.McMcInstanceSelected.Info.vanilla.Major <= 14)
            {
                // 1.13 - 1.14：Java 8 - 10
                minVer = new Version(1, 8, 0, 0) > minVer ? new Version(1, 8, 0, 0) : minVer;
                maxVer = new Version(1, 10, 999, 999) < maxVer ? new Version(1, 10, 999, 999) : maxVer;
            }
            else if (ModInstanceList.McMcInstanceSelected.Info.vanilla.Major == 15)
            {
                // 1.15：Java 8 - 15
                minVer = new Version(1, 8, 0, 0) > minVer ? new Version(1, 8, 0, 0) : minVer;
                maxVer = new Version(1, 15, 999, 999) < maxVer ? new Version(1, 15, 999, 999) : maxVer;
            }
            else if (McVersionComparer.CompareVersionGe(ModInstanceList.McMcInstanceSelected.Info.Forge, "34.0.0") &&
                     McVersionComparer.CompareVersionGe("36.2.25", ModInstanceList.McMcInstanceSelected.Info.Forge))
            {
                // 1.16，Forge 34.X ~ 36.2.25：最高 Java 8u321
                maxVer = new Version(1, 8, 0, 320) < maxVer ? new Version(1, 8, 0, 321) : maxVer;
            }
            else if (ModInstanceList.McMcInstanceSelected.Info.vanilla.Major >= 18 &&
                     ModInstanceList.McMcInstanceSelected.Info.vanilla.Major < 19 &&
                     ModInstanceList.McMcInstanceSelected.Info.HasOptiFine) // #305
            {
                // 1.18：若安装了 OptiFine，最高 Java 18
                maxVer = new Version(1, 18, 999, 999) < maxVer ? new Version(1, 18, 999, 999) : maxVer;
            }
        }

        // Cleanroom 检测
        if (ModInstanceList.McMcInstanceSelected.Info.HasCleanroom)
        {
            if (!Version.TryParse(ModInstanceList.McMcInstanceSelected.Info.Cleanroom.Split('-')[0], out Version cleanroomVersion))
                throw new FormatException("无法解析 Cleanroom 版本号：" + ModInstanceList.McMcInstanceSelected.Info.Cleanroom);
            if (cleanroomVersion < new Version(0, 5, 0, 0))
            {
                if (ModBase.modeDebug) ModBase.Log("[Launch] [Debug] Cleanroom 版本低于 0.5，要求至少 Java 21");
                minVer = new Version(21, 0, 0, 0) > minVer ? new Version(21, 0, 0, 0) : minVer;
            }
            else
            {
                if (ModBase.modeDebug) ModBase.Log("[Launch] [Debug] Cleanroom 版本高于 0.5，要求至少 Java 25");
                minVer = new Version(25, 0, 0, 0) > minVer ? new Version(25, 0, 0, 0) : minVer;
            }
        }

        // Fabric 检测
        if (ModInstanceList.McMcInstanceSelected.Info.HasFabric && ModInstanceList.McMcInstanceSelected.Info.Valid) // 不管非标准版本
        {
            if (ModInstanceList.McMcInstanceSelected.Info.vanilla.Major >= 15 &&
                ModInstanceList.McMcInstanceSelected.Info.vanilla.Major <= 16)
                // 1.15 - 1.16：Java 8+
                minVer = new Version(1, 8, 0, 0) > minVer ? new Version(1, 8, 0, 0) : minVer;
            else if (ModInstanceList.McMcInstanceSelected.Info.vanilla.Major >= 18)
                // 1.18+：Java 17+
                minVer = new Version(1, 17, 0, 0) > minVer ? new Version(1, 17, 0, 0) : minVer;
        }

        // LiteLoader 检测
        if (ModInstanceList.McMcInstanceSelected.Info.HasLiteLoader && ModInstanceList.McMcInstanceSelected.Info.Valid)
        {
            // 最高 Java 8
            if (ModBase.modeDebug)
                ModBase.Log("[Launch] [Debug] LiteLoader 要求最高 Java 8");
            maxVer = new Version(8, 999, 999, 999) < maxVer ? new Version(8, 999, 999, 999) : maxVer;
        }

        // LabyMod 检测
        if (ModInstanceList.McMcInstanceSelected.Info.HasLabyMod)
        {
            if (ModBase.modeDebug)
                ModBase.Log("[Launch] [Debug] LabyMod 要求至少 Java 21");
            minVer = new Version(21, 0, 0, 0) > minVer ? new Version(21, 0, 0, 0) : minVer;
            maxVer = new Version(999, 999, 999, 999);
        }

        // JSON 中要求的版本
        if (ModInstanceList.McMcInstanceSelected.JsonObject["javaVersion"] is not null)
        {
            var majorVersion = ModBase.Val(ModInstanceList.McMcInstanceSelected.JsonObject["javaVersion"]["majorVersion"]);
            if (ModBase.modeDebug)
                ModBase.Log("[Launch] [Debug] JSON 中参数要求至少 Java " + majorVersion);
            if (majorVersion <= 8d)
                minVer = new Version(1, (int)Math.Round(majorVersion), 0, 0) > minVer
                    ? new Version(1, (int)Math.Round(majorVersion), 0, 0)
                    : minVer;
            else
                minVer = new Version((int)Math.Round(majorVersion), 0, 0, 0) > minVer
                    ? new Version((int)Math.Round(majorVersion), 0, 0, 0)
                    : minVer;

            if (maxVer < minVer)
                maxVer = new Version(999, 999, 999, 999);
        }

        lock (ModJava.javaLock)
        {
            // 选择 Java
            McLaunchLog("Java 版本需求：最低 " + minVer + "，最高 " + maxVer);
            mcLaunchJavaSelected = ModJava.JavaSelect("$$", minVer, maxVer, ModInstanceList.McMcInstanceSelected);
            if (task.IsAborted)
                return;
            if (mcLaunchJavaSelected is not null)
            {
                McLaunchLog("选择的 Java：" + mcLaunchJavaSelected);
                return;
            }

            // 无合适的 Java
            if (task.IsAborted)
                return; // 中断加载会导致 JavaSelect 异常地返回空值，误判找不到 Java
            McLaunchLog("无合适的 Java，需要确认是否自动下载");
            string javaCode;
            if (minVer >= new Version(1, 9))
            {
                javaCode = minVer.Major.ToString();
            }
            else if (maxVer < new Version(1, 8))
            {
                if (ModInstanceList.McMcInstanceSelected.Info.HasForge)
                    ModMain.MyMsgBox(
                        Lang.Text("Minecraft.Launch.Java.NeedLegacyJavaFixerOrJava7"),
                        Lang.Text("Minecraft.Launch.Java.NotFound.Title"));
                else
                    ModMain.MyMsgBox(
                        Lang.Text("Minecraft.Launch.Java.NeedJava7"),
                        Lang.Text("Minecraft.Launch.Java.NotFound.Title"));
                throw new Exception("$$");
            }
            else if (minVer > new Version(1, 8, 0, 140) && maxVer < new Version(1, 8, 0, 321))
            {
                ModMain.MyMsgBox(
                    Lang.Text("Minecraft.Launch.Java.NeedJava8U141ToU320"),
                    Lang.Text("Minecraft.Launch.Java.NotFound.Title"));
                throw new Exception("$$");
            }
            else if (minVer > new Version(1, 8, 0, 140))
            {
                ModMain.MyMsgBox(
                    Lang.Text("Minecraft.Launch.Java.NeedJava8U141OrLater"),
                    Lang.Text("Minecraft.Launch.Java.NotFound.Title"));
                throw new Exception("$$");
            }
            else
            {
                javaCode = 8.ToString();
            }

            if (!ModJava.JavaDownloadConfirm($"Java {javaCode}"))
                throw new Exception("$$");
            // 开始自动下载
            var javaLoader = ModJava.GetJavaDownloadLoader();
            try
            {
                javaLoader.Start(recommendedComponent ?? javaCode, true); // 在 Java 22+ 时优先使用 Mojang 提供的 Component 字段
                while (javaLoader.State == ModBase.LoadState.Loading && !task.IsAborted)
                {
                    task.Progress = javaLoader.Progress;
                    Thread.Sleep(10);
                }
            }
            finally
            {
                javaLoader.Abort(); // 确保取消时中止 Java 下载
            }

            // 检查下载结果
            mcLaunchJavaSelected = ModJava.JavaSelect("$$", minVer, maxVer, ModInstanceList.McMcInstanceSelected);
            if (task.IsAborted)
                return;
            if (mcLaunchJavaSelected is not null)
            {
                McLaunchLog("选择的 Java：" + mcLaunchJavaSelected);
            }
            else
            {
                HintService.Hint(Lang.Text("Minecraft.Launch.Error.NoJava"), HintType.Error);
                throw new Exception("$$");
            }
        }
    }

    #endregion

    #region 启动参数

    internal static void SecretLaunchJvmArgs(ref List<string> dataList)
    {
        var dataJvmCustom = Config.Instance.JvmArgs[ModInstanceList.McMcInstanceSelected?.PathInstance];
        dataList.Insert(0,
            string.IsNullOrEmpty(dataJvmCustom)
                ? Config.Launch.JvmArgs
                : dataJvmCustom); // 可变 JVM 参数
        switch (Config.Launch.PreferredIpStack)
        {
            case JvmPreferredIpStack.PreferV4:
            {
                dataList.Add("-Djava.net.preferIPv4Stack=true");
                dataList.Add("-Djava.net.preferIPv4Addresses=true");
                break;
            }
            case JvmPreferredIpStack.PreferV6:
            {
                dataList.Add("-Djava.net.preferIPv6Stack=true");
                dataList.Add("-Djava.net.preferIPv6Addresses=true");
                break;
            }
        }

        double availableGb = KernelInterop.GetAvailablePhysicalMemoryBytes() / 1073741824.0;
        ModLaunch.McLaunchLog($"当前剩余内存：{availableGb.ToString("N1", CultureInfo.InvariantCulture)}G");
        double totalRamMb = PageInstanceSetup.GetRam(ModInstanceList.McMcInstanceSelected) * 1024d;
        var maxHeapArg = Math.Floor(totalRamMb).ToString(CultureInfo.InvariantCulture);
        dataList.Add("-Xmn" + Math.Floor(totalRamMb * 0.15).ToString(CultureInfo.InvariantCulture) + "m");
        dataList.Add("-Xmx" + maxHeapArg + "m");
        // 若 dataList 中已存在 -Xms（例如用户自定义参数已设）则跳过，避免重复/冲突。
        if (!dataList.Any(d => d.Contains("-Xms", StringComparison.OrdinalIgnoreCase)))
        {
            if (Config.Launch.LockMemory)
            {
                // #3282: 固定堆大小时追加 -Xms 使其等于 -Xmx（复用同一数值以保持一致），隐式禁用内存归还降低延迟抖动、利于 ZGC。
                dataList.Add("-Xms" + maxHeapArg + "m");
            }
            else
            {
                // #3511: 支持自定义初始堆大小（-Xms），可低于最大堆。
                var initialRam = PageInstanceSetup.GetInitialRam(ModInstanceList.McMcInstanceSelected);
                if (initialRam.HasValue)
                    dataList.Add("-Xms" + Math.Floor(initialRam.Value * 1024d).ToString(CultureInfo.InvariantCulture) + "m");
            }
        }
        if (!dataList.Any(d => d.Contains("-Dlog4j2.formatMsgNoLookups=true")))
            dataList.Add("-Dlog4j2.formatMsgNoLookups=true");
    }

    public class LaunchArgument
    {
        private readonly List<string> _features = new();

        public LaunchArgument(McInstance minecraft)
        {
            var curArgu = string.Empty;
            if (minecraft.IsOldJson)
                _features = minecraft.JsonObject["minecraftArguments"].ToString().Split(' ').ToList();
            else
                foreach (var item in minecraft.JsonObject["arguments"]["game"].AsArray())
                    if (item.GetValueKind() == JsonValueKind.String)
                        _features.Add(item.ToString());
                    else if (item.GetValueKind() == JsonValueKind.Object)
                    {
                        var valueNode = item["value"];
                        if (valueNode.GetValueKind() == JsonValueKind.Array)
                            _features.AddRange(valueNode.AsArray().Select(x => x.ToString()));
                        else if (valueNode.GetValueKind() == JsonValueKind.String)
                            _features.Add(valueNode.ToString());
                    }
        }

        public object HasArguments(string key)
        {
            return _features.Contains(key);
        }
    }

    private static string mcLaunchArgument;

    /// <summary>
    ///     释放 Java Wrapper 并返回完整文件路径。
    /// </summary>
    public static string ExtractJavaWrapper()
    {
        var wrapperPath = Path.Combine(ModBase.pathPure, "JavaWrapper.jar");
        ModBase.Log("[Java] 选定的 Java Wrapper 路径：" + wrapperPath);
        lock (extractJavaWrapperLock) // 避免 OptiFine 和 Forge 安装时同时释放 Java Wrapper 导致冲突
        {
            try
            {
                WriteJavaWrapper(wrapperPath);
            }
            catch (Exception ex)
            {
                if (File.Exists(wrapperPath))
                {
                    // 因为未知原因 Java Wrapper 可能变为只读文件（#4243）
                    ModBase.Log(ex, "Java Wrapper 文件释放失败，但文件已存在，将在删除后尝试重新生成", ModBase.LogLevel.Developer);
                    try
                    {
                        File.Delete(wrapperPath);
                        WriteJavaWrapper(wrapperPath);
                    }
                    catch (Exception ex2)
                    {
                        ModBase.Log(ex2, "Java Wrapper 文件重新释放失败，将尝试更换文件名重新生成", ModBase.LogLevel.Developer);
                        wrapperPath = Path.Combine(ModBase.pathPure, "JavaWrapper2.jar");
                        try
                        {
                            WriteJavaWrapper(wrapperPath);
                        }
                        catch (Exception ex3)
                        {
                            throw new FileNotFoundException("释放 Java Wrapper 最终尝试失败", ex3);
                        }
                    }
                }
                else
                {
                    throw new FileNotFoundException("释放 Java Wrapper 失败", ex);
                }
            }
        }

        return wrapperPath;
    }

    private static readonly object extractJavaWrapperLock = new();

    private static void WriteJavaWrapper(string path)
    {
        ModBase.WriteFile(path, ModBase.GetResourceStream("Resources/java-wrapper.jar"));
    }

    /// <summary>
    ///     释放 linkd 并返回完整文件路径。
    /// </summary>
    public static string ExtractLinkD()
    {
        var linkDPath = Path.Combine(ModBase.pathPure, "linkd.exe");
        lock (extractLinkDLock) // 避免 OptiFine 和 Forge 安装时同时释放 Java Wrapper 导致冲突
        {
            try
            {
                WriteLinkD(linkDPath);
            }
            catch (Exception ex)
            {
                if (File.Exists(linkDPath))
                {
                    ModBase.Log(ex, "linkd 文件释放失败，但文件已存在，将在删除后尝试重新生成", ModBase.LogLevel.Developer);
                    try
                    {
                        File.Delete(linkDPath);
                        WriteLinkD(linkDPath);
                    }
                    catch (Exception ex2)
                    {
                        throw new FileNotFoundException("释放 linkd 失败", ex2);
                    }
                }
                else
                {
                    throw new FileNotFoundException("释放 linkd 失败", ex);
                }
            }
        }

        return linkDPath;
    }

    private static readonly object extractLinkDLock = new();

    private static void WriteLinkD(string path)
    {
        ModBase.WriteFile(path, ModBase.GetResourceStream("Resources/linkd.exe"));
    }

    /// <summary>
    /// 判断是否使用 LegacyFix。
    /// </summary>
    private static bool McLaunchNeedsLegacyFix(McInstance mc)
    {
        if (Config.Launch.DisableLF || Config.Instance.DisableLF[mc.PathInstance])
        {
            ModBase.Log("[Launch] LegacyFix 已被禁用");
            return false;
        }
        if (mc.releaseTime < new DateTime(2013, 6, 25) && mc.releaseTime.Year > 2000)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 获取实例所依赖的 LWJGL 版本
    /// </summary>
    private static string McLaunchGetLwjglVersion(McInstance mc)
    {
        foreach (ModLibrary.McLibToken library in ModLibrary.McLibListGet(mc, false))
        {
            if (string.IsNullOrWhiteSpace(library.OriginalName))
                continue;

            string[] parts = library.OriginalName.Split(':');
            if (parts.Length >= 3 &&
                parts[0].Equals("org.lwjgl", StringComparison.OrdinalIgnoreCase) &&
                parts[1].Equals("lwjgl", StringComparison.OrdinalIgnoreCase))
            {
                return parts[2];
            }
        }

        return null;
    }

    /// <summary>
    /// 判断是否启用了针对 Minecraft 26.1 的性能问题补丁
    /// </summary>
    private static bool McLaunchUsesLwjglUnsafeAgent(McInstance mc)
    {
        if (McLaunchGetLwjglVersion(mc) == "3.4.1")
        {
            bool globalDisabled = Config.Launch.DisableLwjglUnsafeAgent;
            bool instanceDisabled = Config.Instance.DisableLwjglUnsafeAgent[mc.PathInstance];

            return !globalDisabled && !instanceDisabled;
        }
        else
        {
            return false;
        }
    }

    // 主方法，合并 Jvm、Game、Replace 三部分的参数数据
    private static void McLaunchArgumentMain(ModLoader.LoaderTask<string, List<ModLibrary.McLibToken>> loader)
    {
        McLaunchLog("开始获取 Minecraft 启动参数");
        // 获取基准字符串与参数信息
        string arguments;
        if (ModInstanceList.McMcInstanceSelected.JsonObject["arguments"] is not null &&
            ModInstanceList.McMcInstanceSelected.JsonObject["arguments"]["jvm"] is not null)
        {
            McLaunchLog("获取新版 JVM 参数");
            arguments = McLaunchArgumentsJvmNew(ModInstanceList.McMcInstanceSelected);
            McLaunchLog("新版 JVM 参数获取成功：");
            McLaunchLog(arguments);
        }
        else
        {
            McLaunchLog("获取旧版 JVM 参数");
            arguments = McLaunchArgumentsJvmOld(ModInstanceList.McMcInstanceSelected);
            McLaunchLog("旧版 JVM 参数获取成功：");
            McLaunchLog(arguments);
        }

        if (!string.IsNullOrEmpty(
                (string)ModInstanceList.McMcInstanceSelected.JsonObject["minecraftArguments"])) // 有的实例 JSON 中是空字符串
        {
            McLaunchLog("获取旧版 Game 参数");
            arguments += " " + McLaunchArgumentsGameOld(ModInstanceList.McMcInstanceSelected);
            McLaunchLog("旧版 Game 参数获取成功");
        }

        if (ModInstanceList.McMcInstanceSelected.JsonObject["arguments"] is not null &&
            ModInstanceList.McMcInstanceSelected.JsonObject["arguments"]["game"] is not null)
        {
            McLaunchLog("获取新版 Game 参数");
            arguments += " " + McLaunchArgumentsGameNew(ModInstanceList.McMcInstanceSelected);
            McLaunchLog("新版 Game 参数获取成功");
        }

        // 编码参数（#4700、#5892、#5909）
        if (mcLaunchJavaSelected.Installation.MajorVersion > 8)
        {
            if (!arguments.Contains("-Dstdout.encoding="))
                arguments = "-Dstdout.encoding=UTF-8 " + arguments;
            if (!arguments.Contains("-Dstderr.encoding="))
                arguments = "-Dstderr.encoding=UTF-8 " + arguments;
        }

        if (mcLaunchJavaSelected.Installation.MajorVersion >= 18)
            if (!arguments.Contains("-Dfile.encoding="))
                arguments = "-Dfile.encoding=COMPAT " + arguments;
        // MJSB
        arguments = arguments.Replace(" -Dos.name=Windows 10", " -Dos.name=\"Windows 10\"");
        // 全屏
        if (Config.Launch.GameWindowMode == 0)
            arguments += " --fullscreen";
        // 由 Option 传入的额外参数
        foreach (var arg in currentLaunchOptions.ExtraArgs)
            arguments += " " + arg.Trim();
        // 自定义参数
        var argumentGame = Config.Instance.GameArgs[ModInstanceList.McMcInstanceSelected?.PathInstance];
        arguments = arguments + " " + (string.IsNullOrEmpty(argumentGame) ? Config.Launch.GameArgs : argumentGame);
        // 替换参数
        var replaceArguments = McLaunchArgumentsReplace(ModInstanceList.McMcInstanceSelected, ref loader);
        if (string.IsNullOrWhiteSpace(replaceArguments["${version_type}"]))
        {
            // 若自定义信息为空，则去掉该部分
            arguments = arguments.Replace(" --versionType ${version_type}", "");
            replaceArguments["${version_type}"] = "\"\"";
        }

        var finalArguments = "";
        foreach (var argumentRaw in arguments.Split(" "))
        {
            var argument = argumentRaw;
            foreach (var entry in replaceArguments)
                argument = argument.Replace(entry.Key, entry.Value);
            if ((argument.Contains(" ") || argument.Contains(@":\")) && !argument.EndsWithF("\""))
                argument = $"\"{argument}\"";
            finalArguments += argument + " ";
        }

        finalArguments = finalArguments.TrimEnd();
        // 进存档
        var worldName = currentLaunchOptions.WorldName;
        if (worldName is not null) finalArguments += $" --quickPlaySingleplayer \"{worldName}\"";
        // 进服
        var server = string.IsNullOrEmpty(currentLaunchOptions.ServerIp)
            ? Config.Instance.ServerToEnter[ModInstanceList.McMcInstanceSelected?.PathInstance]
            : currentLaunchOptions.ServerIp;
        if (string.IsNullOrWhiteSpace(worldName) && !string.IsNullOrWhiteSpace(server))
        {
            if (ModInstanceList.McMcInstanceSelected.releaseTime > new DateTime(2023, 4, 4))
            {
                // QuickPlay
                finalArguments += $" --quickPlayMultiplayer \"{server}\"";
            }
            else
            {
                // 老版本
                if (server.Contains(":"))
                    // 包含端口号
                    finalArguments += " --server " + server.Split(":")[0] + " --port " + server.Split(":")[1];
                else
                    // 不包含端口号
                    finalArguments += " --server " + server + " --port 25565";
                if (ModInstanceList.McMcInstanceSelected.Info.HasOptiFine)
                    HintService.Hint(Lang.Text("Minecraft.Launch.Error.OptiFineAutoJoinWarning"), HintType.Error);
            }
        }

        // 输出
        McLaunchLog("Minecraft 启动参数：");
        McLaunchLog(finalArguments);
        mcLaunchArgument = finalArguments;
    }

    // Jvm 部分（第一段）
    private static string McLaunchArgumentsJvmOld(McInstance instance)
    {
        // 存储以空格为间隔的启动参数列表
        var dataList = new List<string>();

        // 输出固定参数
        dataList.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");
        var argumentJvm = Config.Instance.JvmArgs[ModInstanceList.McMcInstanceSelected?.PathInstance];
        if (string.IsNullOrEmpty(argumentJvm))
            argumentJvm = Config.Launch.JvmArgs;
        if (!argumentJvm.Contains("-Dlog4j2.formatMsgNoLookups=true"))
            argumentJvm += " -Dlog4j2.formatMsgNoLookups=true";
        argumentJvm = argumentJvm.Replace(" -XX:MaxDirectMemorySize=256M", ""); // #3511 的清理
        dataList.Insert(0, argumentJvm); // 可变 JVM 参数
        dataList.Add("-Xmn" +
                     Math.Floor(PageInstanceSetup.GetRam(ModInstanceList.McMcInstanceSelected,
                         !mcLaunchJavaSelected.Installation.Is64Bit) * 1024d * 0.15d) + "m");
        var maxHeapArg = Math.Floor(PageInstanceSetup.GetRam(ModInstanceList.McMcInstanceSelected,
            !mcLaunchJavaSelected.Installation.Is64Bit) * 1024d);
        dataList.Add("-Xmx" + maxHeapArg + "m");
        // 若 dataList 中已存在 -Xms（例如用户自定义参数已设）则跳过，避免重复/冲突。
        if (!dataList.Any(d => d.Contains("-Xms", StringComparison.OrdinalIgnoreCase)))
        {
            if (Config.Launch.LockMemory)
            {
                // #3282: 固定堆大小时追加 -Xms 使其等于 -Xmx（复用同一数值以保持一致），隐式禁用内存归还降低延迟抖动、利于 ZGC。
                dataList.Add("-Xms" + maxHeapArg + "m");
            }
            else
            {
                // #3511: 支持自定义初始堆大小（-Xms），可低于最大堆。
                var initialRam = PageInstanceSetup.GetInitialRam(ModInstanceList.McMcInstanceSelected,
                    !mcLaunchJavaSelected.Installation.Is64Bit);
                if (initialRam.HasValue)
                    dataList.Add("-Xms" + Math.Floor(initialRam.Value * 1024d).ToString(CultureInfo.InvariantCulture) + "m");
            }
        }
        dataList.Add("\"-Djava.library.path=" + GetNativesFolder() + "\"");
        dataList.Add("-cp ${classpath}"); // 把支持库添加进启动参数表

        // Authlib-Injector
        if (mcLoginLoader.output.Type == "Auth")
        {
            if (mcLaunchJavaSelected.Installation.MajorVersion >= 6)
                dataList.Add("-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT"); // 信任系统根证书（Meloong-Git/#5252）
            var server = mcLoginAuthLoader.input.BaseUrl.Replace("/authserver", "");
            try
            {
                var response = Requester.FetchString(server);
                dataList.Insert(0,
                    "-javaagent:\"" + Path.Combine(ModBase.pathPure, "authlib-injector.jar") + "\"=" + server +
                    " -Dauthlibinjector.side=client" + " -Dauthlibinjector.yggdrasil.prefetched=" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(response)));
            }
            catch (WebException ex)
            {
                throw new Exception(
                    Lang.Text("Minecraft.Launch.Error.CannotConnectAuthServerWithDetail", server ?? null) + ex.InnerException, ex);
            }
            catch (Exception ex)
            {
                throw new Exception(Lang.Text("Minecraft.Launch.Error.CannotConnectAuthServer", server ?? null), ex);
            }
        }

        if (Config.Instance.UseDebugLof4j2Config[instance.PathIndie])
        {
            if (ModInstanceList.McMcInstanceSelected.releaseTime.Year >= 2017)
                dataList.Insert(0, "-Dlog4j.configurationFile=\"" + LaunchEnvUtils.ExtractDebugLog4j2Config() + "\"");
            else
                dataList.Insert(0,
                    "-Dlog4j.configurationFile=\"" + LaunchEnvUtils.ExtractLegacyDebugLog4j2Config() + "\"");
        }

        // 渲染器
        var renderer = 0;
        var instanceRenderer = Config.Instance.Renderer[ModInstanceList.McMcInstanceSelected?.PathInstance];
        if (instanceRenderer != 0)
            renderer = instanceRenderer - 1;
        else
            renderer = Config.Launch.Renderer;
        var mesaLoaderWindowsTargetFile =
            Path.Combine(ModBase.pathPure, "mesa-loader-windows", mesaLoaderWindowsVersion, "Loader.jar");

        if (renderer != 0)
            dataList.Insert(0,
                "-javaagent:\"" + mesaLoaderWindowsTargetFile + "\"=" +
                (renderer == 1 ? "llvmpipe" : renderer == 2 ? "d3d12" : "zink"));

        // 设置代理
        if (Config.Instance.UseProxy[instance.PathIndie] && Config.Network.HttpProxy.Type.Equals(2) &&
            !string.IsNullOrWhiteSpace(Config.Network.HttpProxy.CustomAddress))
            try
            {
                var proxyAddress = new Uri(Config.Network.HttpProxy.CustomAddress);
                dataList.Add(
                    $"-D{(proxyAddress.Scheme.StartsWithF("https:") ? "https" : "http")}.proxyHost={proxyAddress.AbsoluteUri}");
                dataList.Add(
                    $"-D{(proxyAddress.Scheme.StartsWithF("https:") ? "https" : "http")}.proxyPort={proxyAddress.Port}");
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Minecraft.Launch.Error.Proxy"),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Minecraft.Launch.Error.Proxy"));
            }

        // 添加 LegacyFix 相关参数
        if (McLaunchNeedsLegacyFix(instance))
        {
            var legacyFixPath = Path.Combine(ModBase.pathPure, "legacyfix.jar");
            dataList.Add("-javaagent:\"" + legacyFixPath + "\"");

            // Beta 1.6 以前版本需要添加的参数
            if (instance.releaseTime < new DateTime(2011, 5, 25))
            {
                dataList.Add("-Djava.util.Arrays.useLegacyMergeSort=true");
            }
        }

        // 添加 Java Wrapper 作为主 Jar
        if (ModBase.IsUtf8CodePage() && !Config.Launch.DisableJlw &&
            !Config.Instance.DisableJlw[ModInstanceList.McMcInstanceSelected?.PathInstance])
        {
            if (mcLaunchJavaSelected.Installation.MajorVersion >= 9)
                dataList.Add("--add-exports cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED");
            dataList.Add("-Doolloo.jlw.tmpdir=\"" + ModBase.pathPure.TrimEnd('\\') + "\"");
            dataList.Add("-jar \"" + ExtractJavaWrapper() + "\"");
        }

        // 添加 MainClass
        if (instance.JsonObject["mainClass"] is null) throw new Exception(Lang.Text("Minecraft.Launch.Error.MissingMainClass"));

        dataList.Add((string)instance.JsonObject["mainClass"]);

        return dataList.Join(" ");
    }

    private static string McLaunchArgumentsJvmNew(McInstance instance)
    {
        var dataList = new List<string>();

        // 获取 Json 中的 DataList
        var currentInstance = instance;
        while (true)
        {
            if (currentInstance.JsonObject["arguments"] is not null &&
                currentInstance.JsonObject["arguments"]["jvm"] is not null)
                foreach (var subJson in currentInstance.JsonObject["arguments"]["jvm"].AsArray())
                    if (subJson.GetValueKind() == JsonValueKind.String)
                    {
                        // 字符串类型
                        dataList.Add(subJson.ToString());
                    }
                    // 非字符串类型
                    else if (ModLibrary.McJsonRuleCheck(subJson["rules"]))
                    {
                        // 满足准则
                        if (subJson["value"].GetValueKind() == JsonValueKind.String)
                            dataList.Add(subJson["value"].ToString());
                        else
                            foreach (var value in subJson["value"].AsArray())
                                dataList.Add(value.ToString());
                    }

            if (string.IsNullOrEmpty(currentInstance.InheritInstanceName))
                break;

            currentInstance = new McInstance(currentInstance.InheritInstanceName);
        }

        // 内存、Log4j 防御参数等
        SecretLaunchJvmArgs(ref dataList);

        // Authlib-Injector
        if (mcLoginLoader.output.Type == "Auth")
        {
            if (mcLaunchJavaSelected.Installation.MajorVersion >= 6)
                dataList.Add("-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT"); // 信任系统根证书（Meloong-Git/#5252）
            var server = mcLoginAuthLoader.input.BaseUrl.Replace("/authserver", "");
            try
            {
                var response = ModNet.NetGetCodeByRequestRetry(server, Encoding.UTF8)?.ToString();
                dataList.Insert(0,
                    "-javaagent:\"" + Path.Combine(ModBase.pathPure, "authlib-injector.jar") + "\"=" + server +
                    " -Dauthlibinjector.side=client" + " -Dauthlibinjector.yggdrasil.prefetched=" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(response)));
            }
            catch (Exception ex)
            {
                throw new Exception(Lang.Text("Minecraft.Launch.Error.CannotConnectAuthServer", server ?? null), ex);
            }
        }
        
        // LWJGL Unsafe Agent
        if (McLaunchUsesLwjglUnsafeAgent(ModInstanceList.McMcInstanceSelected))
        {
            ModBase.Log($"获取到的 LWJGL 版本：{McLaunchGetLwjglVersion(ModInstanceList.McMcInstanceSelected)}");
            dataList.Insert(0, $"-javaagent:\"{ModBase.pathPure}lwjgl-unsafe-agent.jar\"");
        }

        if (Config.Instance.UseDebugLof4j2Config[instance.PathIndie])
        {
            if (ModInstanceList.McMcInstanceSelected.releaseTime.Year >= 2017)
                dataList.Insert(0, "-Dlog4j.configurationFile=\"" + LaunchEnvUtils.ExtractDebugLog4j2Config() + "\"");
            else
                dataList.Insert(0,
                    "-Dlog4j.configurationFile=\"" + LaunchEnvUtils.ExtractLegacyDebugLog4j2Config() + "\"");
        }

        // 渲染器
        var renderer = 0;
        var instanceRenderer = Config.Instance.Renderer[ModInstanceList.McMcInstanceSelected?.PathInstance];
        if (instanceRenderer != 0)
            renderer = instanceRenderer - 1;
        else
            renderer = Config.Launch.Renderer;
        var mesaLoaderWindowsTargetFile =
            Path.Combine(ModBase.pathPure, "mesa-loader-windows", mesaLoaderWindowsVersion, "Loader.jar");

        if (renderer != 0)
            dataList.Insert(0,
                "-javaagent:\"" + mesaLoaderWindowsTargetFile + "\"=" +
                (renderer == 1 ? "llvmpipe" : renderer == 2 ? "d3d12" : "zink"));

        // 设置代理
        if (Config.Instance.UseProxy[instance.PathIndie] && Config.Network.HttpProxy.Type.Equals(2) &&
            !string.IsNullOrWhiteSpace(Config.Network.HttpProxy.CustomAddress))
            try
            {
                var proxyAddress = new Uri(Config.Network.HttpProxy.CustomAddress);
                dataList.Add(
                    $"-D{(proxyAddress.Scheme.StartsWithF("https:") ? "https" : "http")}.proxyHost={proxyAddress.AbsoluteUri}");
                dataList.Add(
                    $"-D{(proxyAddress.Scheme.StartsWithF("https:") ? "https" : "http")}.proxyPort={proxyAddress.Port}");
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Minecraft.Launch.Error.Proxy"),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Minecraft.Launch.Error.Proxy"));
            }

        // 添加 Java Wrapper 作为主 Jar
        if (ModBase.IsUtf8CodePage() && !Config.Launch.DisableJlw &&
            !Config.Instance.DisableJlw[ModInstanceList.McMcInstanceSelected?.PathInstance])
        {
            if (mcLaunchJavaSelected.Installation.MajorVersion >= 9)
                dataList.Add("--add-exports cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED");
            dataList.Add("-Doolloo.jlw.tmpdir=\"" + ModBase.pathPure.TrimEnd('\\') + "\"");
            dataList.Add("-jar \"" + ExtractJavaWrapper() + "\"");
        }


        // 将 "-XXX" 与后面 "XXX" 合并到一起
        // 如果不合并，会导致 Forge 1.17 启动无效，它有两个 --add-exports，进一步导致其中一个在后面被去重
        var deDuplicateDataList = new List<string>();
        for (int i = 0, loopTo = dataList.Count - 1; i <= loopTo; i++)
        {
            var currentEntry = dataList[i];
            if (dataList[i].StartsWithF("-"))
                while (i < dataList.Count - 1)
                {
                    if (dataList[i + 1].StartsWithF("-")) break;

                    i += 1;
                    currentEntry += " " + dataList[i];
                }

            deDuplicateDataList.Add(currentEntry.Trim().Replace("McEmu= ", "McEmu="));
        }

        // #3511 的清理
        deDuplicateDataList.Remove("-XX:MaxDirectMemorySize=256M");

        // 去重
        var result = deDuplicateDataList.Distinct().ToList().Join(" ");

        // 添加 MainClass
        if (instance.JsonObject["mainClass"] is null) throw new Exception(Lang.Text("Minecraft.Launch.Error.MissingMainClass"));

        result += " " + instance.JsonObject["mainClass"];

        return result;
    }

    // Game 部分（第二段）
    private static string McLaunchArgumentsGameOld(McInstance version)
    {
        var dataList = new List<string>();

        // 本地化 Minecraft 启动信息
        var basicString = version.JsonObject["minecraftArguments"].ToString();
        if (!basicString.Contains("--height"))
            basicString += " --height ${resolution_height} --width ${resolution_width}";
        dataList.Add(basicString);

        var result = dataList.Join(" ");

        // 特别改变 OptiFineTweaker
        if ((version.Info.HasForge || version.Info.HasLiteLoader) && version.Info.HasOptiFine)
        {
            // 把 OptiFineForgeTweaker 放在最后，不然会导致崩溃！
            if (result.Contains("--tweakClass optifine.OptiFineForgeTweaker"))
            {
                ModBase.Log("[Launch] 发现正确的 OptiFineForge TweakClass，目前参数：" + result);
                result = result.Replace(" --tweakClass optifine.OptiFineForgeTweaker", "")
                             .Replace("--tweakClass optifine.OptiFineForgeTweaker ", "") +
                         " --tweakClass optifine.OptiFineForgeTweaker";
            }

            if (result.Contains("--tweakClass optifine.OptiFineTweaker"))
            {
                ModBase.Log("[Launch] 发现错误的 OptiFineForge TweakClass，目前参数：" + result);
                result = result.Replace(" --tweakClass optifine.OptiFineTweaker", "")
                             .Replace("--tweakClass optifine.OptiFineTweaker ", "") +
                         " --tweakClass optifine.OptiFineForgeTweaker";
                try
                {
                    ModBase.WriteFile(Path.Combine(version.PathInstance, version.Name + ".json"),
                        ModBase.ReadFile(Path.Combine(version.PathInstance, version.Name + ".json"))
                            .Replace("optifine.OptiFineTweaker", "optifine.OptiFineForgeTweaker"));
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "替换 OptiFineForge TweakClass 失败");
                }
            }
        }

        return result;
    }

    private static string McLaunchArgumentsGameNew(McInstance instance)
    {
        string mcLaunchArgumentsGameNewRet = default;
        var dataList = new List<string>();

        // 获取 Json 中的 DataList
        var currentInstance = instance;
        while (true)
        {
            if (currentInstance.JsonObject["arguments"] is not null &&
                currentInstance.JsonObject["arguments"]["game"] is not null)
                foreach (var subJson in currentInstance.JsonObject["arguments"]["game"].AsArray())
                    if (subJson.GetValueKind() == JsonValueKind.String)
                    {
                        // 字符串类型
                        dataList.Add(subJson.ToString());
                    }
                    // 非字符串类型
                    else if (ModLibrary.McJsonRuleCheck(subJson["rules"]))
                    {
                        // 满足准则
                        if (subJson["value"].GetValueKind() == JsonValueKind.String)
                            dataList.Add(subJson["value"].ToString());
                        else
                            foreach (var value in subJson["value"].AsArray())
                                dataList.Add(value.ToString());
                    }

            if (string.IsNullOrEmpty(currentInstance.InheritInstanceName))
                break;

            currentInstance = new McInstance(currentInstance.InheritInstanceName);
        }

        // 将 "-XXX" 与后面 "XXX" 合并到一起
        // 如果不进行合并 Impact 会启动无效，它有两个 --tweakclass
        var deDuplicateDataList = new List<string>();
        for (int i = 0, loopTo = dataList.Count - 1; i <= loopTo; i++)
        {
            var currentEntry = dataList[i];
            if (dataList[i].StartsWithF("-"))
                while (i < dataList.Count - 1)
                {
                    if (dataList[i + 1].StartsWithF("-")) break;

                    i += 1;
                    currentEntry += " " + dataList[i];
                }

            deDuplicateDataList.Add(currentEntry);
        }

        // 去重
        mcLaunchArgumentsGameNewRet = deDuplicateDataList.Distinct().ToList().Join(" ");

        // 特别改变 OptiFineTweaker
        if ((instance.Info.HasForge || instance.Info.HasLiteLoader) && instance.Info.HasOptiFine)
        {
            // 把 OptiFineForgeTweaker 放在最后，不然会导致崩溃！
            if (mcLaunchArgumentsGameNewRet.Contains("--tweakClass optifine.OptiFineForgeTweaker"))
            {
                ModBase.Log("[Launch] 发现正确的 OptiFineForge TweakClass，目前参数：" + mcLaunchArgumentsGameNewRet);
                mcLaunchArgumentsGameNewRet =
                    mcLaunchArgumentsGameNewRet.Replace(" --tweakClass optifine.OptiFineForgeTweaker", "")
                        .Replace("--tweakClass optifine.OptiFineForgeTweaker ", "") +
                    " --tweakClass optifine.OptiFineForgeTweaker";
            }

            if (mcLaunchArgumentsGameNewRet.Contains("--tweakClass optifine.OptiFineTweaker"))
            {
                ModBase.Log("[Launch] 发现错误的 OptiFineForge TweakClass，目前参数：" + mcLaunchArgumentsGameNewRet);
                mcLaunchArgumentsGameNewRet =
                    mcLaunchArgumentsGameNewRet.Replace(" --tweakClass optifine.OptiFineTweaker", "")
                        .Replace("--tweakClass optifine.OptiFineTweaker ", "") +
                    " --tweakClass optifine.OptiFineForgeTweaker";
                try
                {
                    ModBase.WriteFile(Path.Combine(instance.PathInstance, instance.Name + ".json"),
                        ModBase.ReadFile(Path.Combine(instance.PathInstance, instance.Name + ".json"))
                            .Replace("optifine.OptiFineTweaker", "optifine.OptiFineForgeTweaker"));
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "替换 OptiFineForge TweakClass 失败");
                }
            }
        }

        return mcLaunchArgumentsGameNewRet;
    }

    // 替换 Arguments
    private static Dictionary<string, string> McLaunchArgumentsReplace(McInstance instance,
        ref ModLoader.LoaderTask<string, List<ModLibrary.McLibToken>> loader)
    {
        var gameArguments = new Dictionary<string, string>();

        // 基础参数
        gameArguments.Add("${classpath_separator}", ";");
        gameArguments.Add("${natives_directory}", ModBase.ShortenPath(GetNativesFolder()));
        gameArguments.Add("${library_directory}", ModBase.ShortenPath(ModFolder.mcFolderSelected + "libraries"));
        gameArguments.Add("${libraries_directory}", ModBase.ShortenPath(ModFolder.mcFolderSelected + "libraries"));
        gameArguments.Add("${launcher_name}", "PCLCE");
        gameArguments.Add("${launcher_version}", ModBase.versionCode.ToString());
        gameArguments.Add("${version_name}", instance.Name);
        var argumentInfo = Config.Instance.TypeInfo[ModInstanceList.McMcInstanceSelected?.PathInstance];
        gameArguments.Add("${version_type}",
            string.IsNullOrEmpty(argumentInfo)
                ? Config.Launch.TypeInfo
                : argumentInfo);
        gameArguments.Add("${game_directory}",
            ModBase.ShortenPath(ModInstanceList.McMcInstanceSelected.PathIndie[..^1]));
        gameArguments.Add("${assets_root}", ModBase.ShortenPath(ModFolder.mcFolderSelected + "assets"));
        gameArguments.Add("${user_properties}", "{}");
        gameArguments.Add("${auth_player_name}", mcLoginLoader.output.Name);
        gameArguments.Add("${auth_uuid}", mcLoginLoader.output.Uuid);
        gameArguments.Add("${auth_access_token}", mcLoginLoader.output.AccessToken);
        gameArguments.Add("${access_token}", mcLoginLoader.output.AccessToken);
        gameArguments.Add("${auth_session}", mcLoginLoader.output.AccessToken);
        gameArguments.Add("${user_type}", "msa"); // #1221

        // 窗口尺寸参数
        Size gameSize;
        switch (Config.Launch.GameWindowMode)
        {
            case GameWindowSizeMode.Launcher: // 与启动器尺寸一致
            {
                Size result;
                ModBase.RunInUiWait(() => result = new Size(ModBase.GetPixelSize(ModMain.frmMain.PanForm.ActualWidth),
                    ModBase.GetPixelSize(ModMain.frmMain.PanForm.ActualHeight)));
                gameSize = result;
                gameSize.Height -= 29.5d * ModBase.dpi / 96d; // 标题栏高度
                break;
            }
            case GameWindowSizeMode.Custom: // 自定义
            {
                gameSize = new Size(Math.Max(100, (double)Config.Launch.GameWindowWidth),
                    Math.Max(100, (double)Config.Launch.GameWindowHeight));
                break;
            }

            default:
            {
                gameSize = new Size(854d, 480d);
                break;
            }
        }

        if (ModInstanceList.McMcInstanceSelected.Info.Drop <= 120 && mcLaunchJavaSelected.Installation.MajorVersion <= 8 &&
            mcLaunchJavaSelected.Installation.Version.Revision >= 200 &&
            mcLaunchJavaSelected.Installation.Version.Revision <= 321 &&
            !ModInstanceList.McMcInstanceSelected.Info.HasOptiFine && !ModInstanceList.McMcInstanceSelected.Info.HasForge)
        {
            // 修复 #3463：1.12.2-，JRE 8u200~321 下窗口大小为设置大小的 DPI% 倍
            McLaunchLog($"已应用窗口大小过大修复（{mcLaunchJavaSelected.Installation.Version.Revision}）");
            gameSize.Width /= ModBase.dpi / 96d;
            gameSize.Height /= ModBase.dpi / 96d;
        }

        gameArguments.Add("${resolution_width}", Math.Round(gameSize.Width).ToString(CultureInfo.InvariantCulture));
        gameArguments.Add("${resolution_height}", Math.Round(gameSize.Height).ToString(CultureInfo.InvariantCulture));

        // Assets 相关参数
        gameArguments.Add("${game_assets}",
            ModBase.ShortenPath(ModFolder.mcFolderSelected +
                                @"assets\virtual\legacy")); // 1.5.2 的 pre-1.6 资源索引应与 legacy 合并
        gameArguments.Add("${assets_index_name}", ModAssets.McAssetsGetIndexName(instance));

        // 支持库参数
        var libList = ModLibrary.McLibListGet(instance, true);
        loader.output = libList;
        var cpStrings = new List<string>();
        string optiFineCp = null;

        // LegacyFix 释放
        if (McLaunchNeedsLegacyFix(instance))
        {
            var legacyFixPath = Path.Combine(ModBase.pathPure, "legacyfix.jar");
            try
            {
                ModBase.WriteFile(legacyFixPath, ModBase.GetResourceStream("Resources/legacyfix.jar"));
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "LegacyFix 释放失败");
            }
        }

        // LWJGL Unsafe Agent 释放
        if (McLaunchUsesLwjglUnsafeAgent(instance))
        {
            string agentPath = Path.Combine(ModBase.pathPure, "lwjgl-unsafe-agent.jar");
            try
            {
                ModBase.WriteFile(agentPath, ModBase.GetResourceStream("Resources/lwjgl-unsafe-agent.jar"));
                cpStrings.Add(agentPath);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "LWJGL Unsafe Agent 释放失败");
            }
        }

        foreach (var library in libList)
        {
            if (library.IsNatives)
                continue;
            if (ModInstanceList.McMcInstanceSelected.Info.HasCleanroom 
                && library.OriginalName is not null 
                && (library.OriginalName.Contains("org.lwjgl.lwjgl:lwjgl:2.9.4") 
                    || library.OriginalName.Contains("net.java.dev.jna:platform:3.4.0")
                    || library.OriginalName.Contains("com.ibm.icu:icu4j-core-mojang:51.2")))
                continue;
            if (library.Name is not null && library.Name == "optifine:OptiFine")
                optiFineCp = library.LocalPath;
            else
                cpStrings.Add(library.LocalPath);
        }

        foreach (var library in Config.Instance.ClasspathHead[instance.PathInstance].Split(";")) // 自定义 Classpath 头部
        {
            if (string.IsNullOrWhiteSpace(library))
                continue;
            cpStrings.Insert(0, library);
        }

        if (optiFineCp is not null)
            cpStrings.Insert(cpStrings.Count - 2, optiFineCp); // OptiFine 的总是需要放到倒数第二位
        gameArguments.Add("${classpath}", cpStrings.Select(c => ModBase.ShortenPath(c)).Join(";"));

        return gameArguments;
    }

    #endregion

    #region 解压 Natives

    private static void McLaunchNatives(ModLoader.LoaderTask<List<ModLibrary.McLibToken>, int> loader)
    {
        // 创建文件夹
        var target = GetNativesFolder() + @"\";
        Directory.CreateDirectory(target);

        // 解压文件
        McLaunchLog("正在解压 Natives 文件");
        var existFiles = new List<string>();
        foreach (var native in loader.input)
        {
            if (!native.IsNatives)
                continue;
            ZipArchive zip;
            try
            {
                zip = new ZipArchive(new FileStream(native.LocalPath, FileMode.Open));
            }
            catch (InvalidDataException ex)
            {
                ModBase.Log(ex, "打开 Natives 文件失败（" + native.LocalPath + "）");
                File.Delete(native.LocalPath);
                throw new Exception(Lang.Text("Minecraft.Launch.Error.NativesCorrupted", native.LocalPath));
            }

            foreach (var entry in zip.Entries)
            {
                var fileName = entry.FullName;
                if (fileName.EndsWithF(".dll", true))
                {
                    // 实际解压文件的步骤
                    var filePath = target + fileName;
                    existFiles.Add(filePath);
                    var originalFile = new FileInfo(filePath);
                    if (originalFile.Exists)
                    {
                        if (originalFile.Length == entry.Length)
                        {
                            if (ModBase.modeDebug)
                                McLaunchLog("无需解压：" + filePath);
                            continue;
                        }

                        // 删除原文件
                        try
                        {
                            File.Delete(filePath);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            McLaunchLog("删除原 dll 访问被拒绝，这通常代表有一个 MC 正在运行，跳过解压：" + filePath);
                            McLaunchLog("实际的错误信息：" + ex);
                            break;
                        }
                    }

                    // 解压新文件
                    ModBase.WriteFile(filePath, entry.Open());
                    McLaunchLog("已解压：" + filePath);
                }
            }

            if (zip is not null)
                zip.Dispose();
        }

        // 删除多余文件
        foreach (var fileName in Directory.GetFiles(target))
        {
            if (existFiles.Contains(fileName))
                continue;
            try
            {
                McLaunchLog("删除：" + fileName);
                File.Delete(fileName);
            }
            catch (UnauthorizedAccessException ex)
            {
                McLaunchLog("删除多余文件访问被拒绝，跳过删除步骤");
                McLaunchLog("实际的错误信息：" + ex);
                return;
            }
        }
    }

    /// <summary>
    ///     获取 Natives 文件夹路径，不以 \ 结尾。
    /// </summary>
    private static string GetNativesFolder()
    {
        var result = Path.Combine(ModInstanceList.McMcInstanceSelected.PathInstance, ModInstanceList.McMcInstanceSelected.Name + "-natives");
        if (SystemInfo.IsGBKEncoding || result.IsASCII())
            return result;
        result = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "bin", "natives");
        if (result.IsASCII())
            return result;
        return Path.Combine(SystemPaths.DriveLetter, "ProgramData", "PCL", "natives");
    }

    #endregion

    #region 启动与前后处理

    private static void McLaunchPrerun()
    {
        // 要求 Java 使用高性能显卡
        var javaExePath = mcLaunchJavaSelected.Installation.JavawExePath ??
                          mcLaunchJavaSelected.Installation.JavaExePath;
        try
        {
            ModMain.SetGPUPreference(javaExePath, Config.Launch.SetGpuPreference);
        }
        catch (Exception ex)
        {
            if (ProcessInterop.IsAdmin() || !Config.Launch.SetGpuPreference)
            {
                ModBase.Log(ex, "直接调整显卡设置失败");
            }
            else
            {
                ModBase.Log(ex, "直接调整显卡设置失败，将以管理员权限重启 PCL 再次尝试");
                try
                {
                    if (ProcessInterop.StartAsAdmin($"--gpu \"{javaExePath}\"").ExitCode ==
                        (int)ModBase.ProcessReturnValues.TaskDone)
                        McLaunchLog("以管理员权限重启 PCL 并调整显卡设置成功");
                    else
                        throw new Exception("调整过程中出现异常");
                }
                catch (Exception exx)
                {
                    ModBase.Log(
                        exx,
                        Lang.Text("Minecraft.Launch.Error.GpuSet"),
                        ModBase.LogLevel.Hint,
                        userSummary: Lang.Text("Minecraft.Launch.Error.GpuSet"));
                }
            }
        }

        // 更新 launcher_profiles.json
        do
        {
            try
            {
                // 确保可用
                if (mcLoginLoader.output.Type != "Microsoft")
                    break;
                ModFolder.McFolderLauncherProfilesJsonCreate(ModFolder.mcFolderSelected);
                // 构建需要替换的 Json 对象
                var replaceJsonString = @"
            {
              ""authenticationDatabase"": {
                ""00000111112222233333444445555566"": {
                  ""username"": """ + mcLoginLoader.output.Name.Replace("\"", "-") + @""",
                  ""profiles"": {
                    ""66666555554444433333222221111100"": {
                        ""displayName"": """ + mcLoginLoader.output.Name + @"""
                    }
                  }
                }
              },
              ""clientToken"": """ + mcLoginLoader.output.ClientToken + @""",
              ""selectedUser"": {
                ""account"": ""00000111112222233333444445555566"", 
                ""profile"": ""66666555554444433333222221111100""
              }
            }";
                var replaceJson = (JsonObject)ModBase.GetJson(replaceJsonString);
                // 更新文件
                var profiles =
                    (JsonObject)ModBase.GetJson(
                        ModBase.ReadFile(ModFolder.mcFolderSelected + "launcher_profiles.json"));
                profiles.Merge(replaceJson);
                ModBase.WriteFile(ModFolder.mcFolderSelected + "launcher_profiles.json", profiles.ToString(),
                    encoding: Encoding.GetEncoding("GB18030"));
                McLaunchLog("已更新 launcher_profiles.json");
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "更新 launcher_profiles.json 失败，将在删除文件后重试");
                try
                {
                    File.Delete(ModFolder.mcFolderSelected + "launcher_profiles.json");
                    ModFolder.McFolderLauncherProfilesJsonCreate(ModFolder.mcFolderSelected);
                    // 构建需要替换的 Json 对象
                    var replaceJsonString = @"
                    {
                      ""authenticationDatabase"": {
                        ""00000111112222233333444445555566"": {
                          ""username"": """ + mcLoginLoader.output.Name.Replace("\"", "-") + @""",
                          ""profiles"": {
                            ""66666555554444433333222221111100"": {
                                ""displayName"": """ + mcLoginLoader.output.Name + @"""
                            }
                          }
                        }
                      },
                      ""clientToken"": """ + mcLoginLoader.output.ClientToken + @""",
                      ""selectedUser"": {
                        ""account"": ""00000111112222233333444445555566"", 
                        ""profile"": ""66666555554444433333222221111100""
                      }
                    }";
                    var replaceJson = (JsonObject)ModBase.GetJson(replaceJsonString);
                    // 更新文件
                    var profiles =
                        (JsonObject)ModBase.GetJson(
                            ModBase.ReadFile(ModFolder.mcFolderSelected + "launcher_profiles.json"));
                    profiles.Merge(replaceJson);
                    ModBase.WriteFile(ModFolder.mcFolderSelected + "launcher_profiles.json", profiles.ToString(),
                        encoding: Encoding.GetEncoding("GB18030"));
                    McLaunchLog("已在删除后更新 launcher_profiles.json");
                }
                catch (Exception exx)
                {
                    ModBase.Log(
                        exx,
                        "更新 launcher_profiles.json 失败",
                        ModBase.LogLevel.Feedback,
                        userSummary: Lang.Text("Minecraft.Launch.Error.UpdateProfilesFailed"));
                }
            }
        } while (false);

        // 更新 options.txt
        var setupFileAddress = Path.Combine(ModInstanceList.McMcInstanceSelected.PathIndie, "options.txt");

        // 辅助切换游戏语言
        if (Config.Tool.AutoChangeLanguage)
        {
            if (!File.Exists(setupFileAddress))
            {
                // Yosbr Mod 兼容（#2385）：https://www.curseforge.com/minecraft/mc-mods/yosbr
                var yosbrFileAddress = Path.Combine(ModInstanceList.McMcInstanceSelected.PathIndie, "config", "yosbr", "options.txt");
                if (File.Exists(yosbrFileAddress))
                {
                    McLaunchLog("将修改 Yosbr Mod 中的 options.txt");
                    setupFileAddress = yosbrFileAddress;
                    ModBase.WriteIni(setupFileAddress, "lang", "none"); // 忽略默认语言
                }
            }

            try
            {
                // 语言
                // 1.0-     ：没有语言选项
                // 1.1 ~ 5  ：zh_CN 时正常，zh_cn 时崩溃（最后两位字母必须大写，否则将会 NPE 崩溃）
                // 1.6 ~ 10 ：zh_CN 时正常，zh_cn 时自动切换为英文
                // 1.11 ~ 12：zh_cn 时正常，zh_CN 时虽然显示了中文但语言设置会错误地显示选择英文
                // 1.13+    ：zh_cn 时正常，zh_CN 时自动切换为英文
                var currentLang = ModBase.ReadIni(setupFileAddress, "lang", "none");
                var isLanguageUnconfigured = string.Equals(currentLang, "none", StringComparison.OrdinalIgnoreCase);
                var hasExistingSaves = Directory.Exists(Path.Combine(ModInstanceList.McMcInstanceSelected.PathIndie, "saves"));
                var shouldUseDefault = isLanguageUnconfigured || !hasExistingSaves;
                var requiredLang = _ResolveMinecraftLanguage(currentLang, shouldUseDefault,
                    ModInstanceList.McMcInstanceSelected.releaseTime);

                if (currentLang == requiredLang)
                {
                    McLaunchLog($"需要的语言为 {requiredLang}，当前语言为 {currentLang}，无需修改");
                }
                else
                {
                    ModBase.WriteIni(setupFileAddress, "lang", "-"); // 触发缓存更改，避免删除后重新下载残留缓存
                    ModBase.WriteIni(setupFileAddress, "lang", requiredLang);
                    McLaunchLog($"已将语言从 {currentLang} 修改为 {requiredLang}");
                }

                // 如果是初次设置，一并按启动器语言需要修改 forceUnicodeFont，确保 CJK 字符正常显示
                if ((isLanguageUnconfigured || !hasExistingSaves) && _ShouldEnableForceUnicodeFont())
                {
                    ModBase.WriteIni(setupFileAddress, "forceUnicodeFont", "true");
                    McLaunchLog("已开启 forceUnicodeFont，确保当前启动器语言字体正常显示");
                }
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    "更新 options.txt 失败",
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Minecraft.Launch.Error.UpdateOptionsFailed"));
            }
        }

        // 窗口
        switch (Config.Launch.GameWindowMode)
        {
            case GameWindowSizeMode.Fullscreen: // 全屏
            {
                ModBase.WriteIni(setupFileAddress, "fullscreen", "true");
                break;
            }
            case GameWindowSizeMode.Default: // 默认
                // 其他
            {
                break;
            }

            default:
            {
                ModBase.WriteIni(setupFileAddress, "fullscreen", "false");
                break;
            }
        }
    }

    private static string _ResolveMinecraftLanguage(string? currentLanguage, bool shouldUseLauncherLanguage,
        DateTime? mcReleaseTime)
    {
        if (_IsMinecraftVersionUnder1Dot1(mcReleaseTime)) return "none";

        var useLegacyRegionCase = _ShouldUseLegacyMinecraftLanguageCode(mcReleaseTime);
        var languageCode = shouldUseLauncherLanguage
            ? LocalizationService.CurrentLanguage.Code
            : currentLanguage;
        return _NormalizeMinecraftLanguageCode(languageCode, useLegacyRegionCase);
    }

    private static string _NormalizeMinecraftLanguageCode(string? languageCode, bool useLegacyRegionCase)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(languageCode)
            ? "none"
            : languageCode.Replace('-', '_').Trim();
        if (string.Equals(normalizedCode, "none", StringComparison.OrdinalIgnoreCase)) return "none";

        var segments = normalizedCode.Split('_', 2, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return normalizedCode.ToLowerInvariant();

        var language = segments[0].ToLowerInvariant();
        var region = useLegacyRegionCase ? segments[1].ToUpperInvariant() : segments[1].ToLowerInvariant();
        return $"{language}_{region}";
    }

    private static bool _IsMinecraftVersionUnder1Dot1(DateTime? releaseTime)
    {
        return releaseTime.HasValue &&
               releaseTime.Value > new DateTime(2000, 1, 1) &&
               releaseTime.Value <= new DateTime(2011, 11, 18);
    }

    private static bool _ShouldUseLegacyMinecraftLanguageCode(DateTime? releaseTime)
    {
        return releaseTime.HasValue &&
               releaseTime.Value >= new DateTime(2012, 1, 12) &&
               releaseTime.Value <= new DateTime(2016, 6, 8);
    }

    private static bool _ShouldEnableForceUnicodeFont()
    {
        return LocalizationService.CurrentLanguage.FontProfile is LocalizationFontProfile.SimplifiedChinese
            or LocalizationFontProfile.TraditionalChinese
            or LocalizationFontProfile.Japanese
            or LocalizationFontProfile.Korean;
    }

    private static void McLaunchCustom(ModLoader.LoaderTask<int, int> loader)
    {
        // 获取自定义命令
        var customCommandGlobal = Config.Launch.PreLaunchCommand;
        if (!string.IsNullOrEmpty(customCommandGlobal))
            customCommandGlobal = ArgumentReplace(customCommandGlobal, true);
        var customCommandVersion = Config.Instance.PreLaunchCommand[ModInstanceList.McMcInstanceSelected?.PathInstance];
        if (!string.IsNullOrEmpty(customCommandVersion))
            customCommandVersion = ArgumentReplace(customCommandVersion, true);

        // 输出 bat
        try
        {
            var cmdString =
                $"{(mcLaunchJavaSelected.Installation.MajorVersion > 8 ? "chcp 65001>nul" + "\r\n" : "")}" +
                "@echo off" + "\r\n" + $"title 启动 - {ModInstanceList.McMcInstanceSelected.Name}" +
                "\r\n" + "echo 游戏正在启动，请稍候。" + "\r\n" +
                $"cd /D \"{ModBase.ShortenPath(ModInstanceList.McMcInstanceSelected.PathIndie)}\"" + "\r\n" +
                customCommandGlobal + "\r\n" + customCommandVersion + "\r\n" +
                $"\"{mcLaunchJavaSelected.Installation.JavaExePath}\" {mcLaunchArgument}" + "\r\n" +
                "echo 游戏已退出。" + "\r\n" + "pause";
            ModBase.WriteFile(currentLaunchOptions.SaveBatch ?? ModBase.exePath + @"PCL\LatestLaunch.bat",
                McLogFilter.FilterAccessToken(cmdString, 'F'),
                encoding: mcLaunchJavaSelected.Installation.MajorVersion > 8 ? Encoding.UTF8 : Encoding.Default);
            if (currentLaunchOptions.SaveBatch is not null)
            {
                McLaunchLog("导出启动脚本完成，强制结束启动过程");
                abortHint = Lang.Text("Minecraft.Launch.ExportScript.Success");
                ModBase.OpenExplorer(currentLaunchOptions.SaveBatch);
                loader.parent.Abort();
                return; // 导出脚本完成
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "输出启动脚本失败");
            if (currentLaunchOptions.SaveBatch is not null)
                throw; // 直接触发启动失败
        }

        // 执行自定义命令
        if (!string.IsNullOrEmpty(customCommandGlobal))
        {
            McLaunchLog("正在执行全局自定义命令：" + customCommandGlobal);
            var customProcess = new Process();
            try
            {
                customProcess.StartInfo.FileName = "cmd.exe";
                customProcess.StartInfo.Arguments = "/c \"" + customCommandGlobal + "\"";
                customProcess.StartInfo.WorkingDirectory = ModBase.ShortenPath(ModFolder.mcFolderSelected);
                customProcess.StartInfo.UseShellExecute = false;
                customProcess.StartInfo.CreateNoWindow = true;
                customProcess.Start();
                if (Config.Launch.PreLaunchCommandWait)
                    while (!customProcess.HasExited && !loader.IsAborted)
                        Thread.Sleep(10);
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Minecraft.Launch.Error.CustomCommand"),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Minecraft.Launch.Error.CustomCommand"));
            }
            finally
            {
                if (!customProcess.HasExited && loader.IsAborted)
                {
                    McLaunchLog("由于取消启动，已强制结束自定义命令 CMD 进程"); // #1183
                    customProcess.Kill();
                }
            }
        }

        if (!string.IsNullOrEmpty(customCommandVersion))
        {
            McLaunchLog("正在执行实例自定义命令：" + customCommandVersion);
            var customProcess = new Process();
            try
            {
                customProcess.StartInfo.FileName = "cmd.exe";
                customProcess.StartInfo.Arguments = "/c \"" + customCommandVersion + "\"";
                customProcess.StartInfo.WorkingDirectory = ModBase.ShortenPath(ModFolder.mcFolderSelected);
                customProcess.StartInfo.UseShellExecute = false;
                customProcess.StartInfo.CreateNoWindow = true;
                customProcess.Start();
                if (Config.Instance.PreLaunchCommandWait[ModInstanceList.McMcInstanceSelected?.PathInstance])
                    while (!customProcess.HasExited && !loader.IsAborted)
                        Thread.Sleep(10);
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Minecraft.Launch.Error.CustomCommand"),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Minecraft.Launch.Error.CustomCommand"));
            }
            finally
            {
                if (!customProcess.HasExited && loader.IsAborted)
                {
                    McLaunchLog("由于取消启动，已强制结束自定义命令 CMD 进程"); // #1183
                    customProcess.Kill();
                }
            }
        }
    }

    private static void McLaunchRun(ModLoader.LoaderTask<int, Process> loader)
    {
        var noJavaw = Config.Launch.NoJavaw &&
                      mcLaunchJavaSelected.Installation.JavawExePath is not null;

        // 启动信息
        var gameProcess = new Process();
        var startInfo = new ProcessStartInfo(noJavaw
            ? mcLaunchJavaSelected.Installation.JavaExePath
            : mcLaunchJavaSelected.Installation.JavawExePath);

        // 设置环境变量
        var paths = new List<string>(startInfo.EnvironmentVariables["Path"].Split(";"));
        paths.Add(ModBase.ShortenPath(mcLaunchJavaSelected.Installation.JavaFolder));
        startInfo.EnvironmentVariables["Path"] = paths.Distinct().ToList().Join(";");
        startInfo.EnvironmentVariables["appdata"] = ModBase.ShortenPath(ModFolder.mcFolderSelected);

        // 设置其他参数
        startInfo.WorkingDirectory = ModBase.ShortenPath(ModInstanceList.McMcInstanceSelected.PathIndie);
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = noJavaw;
        startInfo.Arguments = mcLaunchArgument;
        gameProcess.StartInfo = startInfo;

        // 开始进程
        gameProcess.Start();
        McLaunchLog("已启动游戏进程：" + startInfo.FileName);
        if (loader.IsAborted)
        {
            McLaunchLog("由于取消启动，已强制结束游戏进程"); // #1631
            gameProcess.Kill();
            return;
        }

        loader.output = gameProcess;
        mcLaunchProcess = gameProcess;
        // 进程优先级处理
        try
        {
            gameProcess.PriorityBoostEnabled = true;
            switch (Config.Launch.ProcessPriority)
            {
                case GameProcessPriority.RealTime: // 实时
                {
                    gameProcess.PriorityClass = ProcessPriorityClass.RealTime;
                    break;
                }
                case GameProcessPriority.High: // 极高
                {
                    gameProcess.PriorityClass = ProcessPriorityClass.High;
                    break;
                }
                case GameProcessPriority.AboveNormal: // 高
                {
                    gameProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
                    break;
                }
                case GameProcessPriority.BelowNormal: // 低
                {
                    gameProcess.PriorityClass = ProcessPriorityClass.BelowNormal;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Minecraft.Launch.Error.PrioritySet"),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Minecraft.Launch.Error.PrioritySet"));
        }
    }

    private static void McLaunchWait(ModLoader.LoaderTask<Process, int> loader)
    {
        // 输出信息
        McLaunchLog("");
        McLaunchLog("~ 基础参数 ~");
        McLaunchLog("PCL 版本：" + ModBase.versionBaseName + " (" + ModBase.versionCode + ")");
        McLaunchLog(
            $"游戏版本：{ModInstanceList.McMcInstanceSelected.Info.VanillaName}（{ModInstanceList.McMcInstanceSelected.Info.vanilla}，Drop {ModInstanceList.McMcInstanceSelected.Info.Drop}{(ModInstanceList.McMcInstanceSelected.Info.Reliable ? "" : "，无法完全确定")}）");
        McLaunchLog("资源版本：" + ModAssets.McAssetsGetIndexName(ModInstanceList.McMcInstanceSelected));
        McLaunchLog("实例继承：" + (string.IsNullOrEmpty(ModInstanceList.McMcInstanceSelected.InheritInstanceName)
            ? "无"
            : ModInstanceList.McMcInstanceSelected.InheritInstanceName));
        var launchRamGb = PageInstanceSetup.GetRam(ModInstanceList.McMcInstanceSelected,
            !mcLaunchJavaSelected.Installation.Is64Bit);
        McLaunchLog("分配的内存：" +
                    launchRamGb.ToString("N1", CultureInfo.InvariantCulture) + " GiB（" +
                    Math.Round(launchRamGb * 1024d).ToString("N0", CultureInfo.InvariantCulture) + " MiB）");
        McLaunchLog("MC 文件夹：" + ModFolder.mcFolderSelected);
        McLaunchLog("实例文件夹：" + ModInstanceList.McMcInstanceSelected.PathInstance);
        McLaunchLog("版本隔离：" + ((ModInstanceList.McMcInstanceSelected.PathIndie ?? "") ==
                               (ModInstanceList.McMcInstanceSelected.PathInstance ?? "")));
        McLaunchLog("HMCL 格式：" + ModInstanceList.McMcInstanceSelected.IsHmclFormatJson);
        McLaunchLog("Java 信息：" + mcLaunchJavaSelected.Installation);
        // McLaunchLog("环境变量：" & If(McLaunchJavaSelected IsNot Nothing, If(McLaunchJavaSelected.HasEnvironment, "已设置", "未设置"), "未设置"))
        McLaunchLog("Natives 文件夹：" + GetNativesFolder());
        McLaunchLog("");
        McLaunchLog("~ 档案参数 ~");
        McLaunchLog("玩家用户名：" + mcLoginLoader.output.Name);
        McLaunchLog("AccessToken：" + mcLoginLoader.output.AccessToken);
        McLaunchLog("ClientToken：" + mcLoginLoader.output.ClientToken);
        McLaunchLog("UUID：" + mcLoginLoader.output.Uuid);
        McLaunchLog("验证方式：" + mcLoginLoader.output.Type);
        McLaunchLog("");

        // 获取窗口标题
        var windowTitle = Config.Instance.Title[ModInstanceList.McMcInstanceSelected?.PathInstance];
        if (string.IsNullOrEmpty(windowTitle) &&
            !Config.Instance.UseGlobalTitle[ModInstanceList.McMcInstanceSelected?.PathInstance])
            windowTitle = Config.Launch.Title;
        windowTitle = ArgumentReplace(windowTitle, false);

        // JStack 路径
        var jStackPath = Path.Combine(mcLaunchJavaSelected.Installation.JavaFolder, "jstack.exe");

        // 初始化等待
        var watcher = new ModWatcher.Watcher(loader, ModInstanceList.McMcInstanceSelected, windowTitle,
            File.Exists(jStackPath) ? jStackPath : "", currentLaunchOptions.IsTest);
        mcLaunchWatcher = watcher;

        // 显示实时日志
        if (currentLaunchOptions.IsTest)
        {
            if (ModMain.frmLogLeft is null)
                ModBase.RunInUiWait(() => ModMain.frmLogLeft = new PageLogLeft());
            if (ModMain.frmLogRight is null)
                ModBase.RunInUiWait(() =>
                {
                    ModAnimation.AniControlEnabled += 1;
                    ModMain.frmLogRight = new PageLogRight();
                    ModAnimation.AniControlEnabled -= 1;
                });
            ModMain.frmLogLeft.Add(watcher);
            McLaunchLog("已显示游戏实时日志");
        }

        // 等待
        while (watcher.State == ModWatcher.Watcher.MinecraftState.Loading)
            Thread.Sleep(100);
        if (watcher.State == ModWatcher.Watcher.MinecraftState.Crashed) throw new Exception("$$");
    }

    private static void McLaunchEnd()
    {
        McLaunchLog("开始启动结束处理");

        // 暂停或开始音乐播放
        if (Config.Preference.Music.StopInGame)
            ModBase.RunInUi(() =>
            {
                if (ModMusic.MusicPause()) ModBase.Log("[Music] 已根据设置，在启动后暂停音乐播放");
            });
        else if (Config.Preference.Music.StartInGame)
            ModBase.RunInUi(() =>
            {
                if (ModMusic.MusicResume()) ModBase.Log("[Music] 已根据设置，在启动后开始音乐播放");
            });
        // 暂停视频背景播放
        ModVideoBack.IsGaming = true;
        ModVideoBack.VideoPause();
        // 启动器可见性
        McLaunchLog(
            "启动器可见性：" + Config.Launch.LauncherVisibility);
        switch (Config.Launch.LauncherVisibility)
        {
            case LauncherVisibility.ExitImmediately:
            {
                // 直接关闭
                McLaunchLog("已根据设置，在启动后关闭启动器");
                ModBase.RunInUi(() => ModMain.frmMain.EndProgram(false));
                break;
            }
            case LauncherVisibility.HideAndExit:
            case LauncherVisibility.HideAndReopen:
            {
                // 隐藏
                McLaunchLog("已根据设置，在启动后隐藏启动器");
                ModBase.RunInUi(() => ModMain.frmMain.Hidden = true);
                break;
            }
            case LauncherVisibility.MinimizeAndReopen:
            {
                // 最小化
                McLaunchLog("已根据设置，在启动后最小化启动器");
                ModBase.RunInUi(() => ModMain.frmMain.WindowState = WindowState.Minimized);
                break;
            }
            case LauncherVisibility.DoNothing:
            {
                break;
            }
            // 啥都不干
        }

        // 启动计数
        States.System.LaunchCount += 1;

        States.Instance.LaunchCount[ModInstanceList.McMcInstanceSelected.PathInstance] =
            States.Instance.LaunchCount[ModInstanceList.McMcInstanceSelected.PathInstance] + 1;
    }

    /// <summary>
    ///     对替换标记进行处理。会对替换内容使用 EscapeHandler 进行转义。
    /// </summary>
    private static string ArgumentReplace(string text, bool replaceTime, Func<string, string> escapeHandler = null)
    {
        // 预处理
        if (text is null)
            return null;

        string replacer(string s)
        {
            if (s is null)
                return "";
            if (escapeHandler is null)
                return s;
            if (s.Contains(@":\"))
                s = ModBase.ShortenPath(s);
            return escapeHandler(s);
        }

        ;
        // 基础
        text = text.Replace("{pcl_version}", replacer(ModBase.versionBaseName));
        text = text.Replace("{pcl_version_code}", replacer(ModBase.versionCode.ToString()));
        text = text.Replace("{pcl_version_branch}", replacer(ModBase.versionBranchName));
        text = text.Replace("{identify}", replacer(Identify.LauncherId));
        text = text.Replace("{path}", replacer(Basics.CurrentDirectory));
        text = text.Replace("{path_with_name}", replacer(Basics.ExecutablePath));
        text = text.Replace("{path_temp}", replacer(ModBase.pathTemp));
        // 时间
        if (replaceTime) // 在窗口标题中，时间会被后续动态替换，所以此时不应该替换
        {
            text = text.Replace("{date}", replacer(Lang.Date(DateTime.Now, "d")));
            text = text.Replace("{time}", replacer(Lang.Date(DateTime.Now, "T")));
        }

        // Minecraft
        text = text.Replace("{java}", replacer(mcLaunchJavaSelected?.Installation.JavaFolder));
        text = text.Replace("{minecraft}", replacer(ModFolder.mcFolderSelected));
        if (ModInstanceList.McMcInstanceSelected?.IsLoaded == true)
        {
            text = text.Replace("{version_path}", replacer(ModInstanceList.McMcInstanceSelected.PathInstance));
            text = text.Replace("{verpath}", replacer(ModInstanceList.McMcInstanceSelected.PathInstance));
            text = text.Replace("{version_indie}", replacer(ModInstanceList.McMcInstanceSelected.PathIndie));
            text = text.Replace("{verindie}", replacer(ModInstanceList.McMcInstanceSelected.PathIndie));
            text = text.Replace("{name}", replacer(ModInstanceList.McMcInstanceSelected.Name));
            if (new[] { "unknown", "old", "pending" }.Contains(
                    ModInstanceList.McMcInstanceSelected.Info.VanillaName.ToLower()))
                text = text.Replace("{version}", replacer(ModInstanceList.McMcInstanceSelected.Name));
            else
                text = text.Replace("{version}", replacer(ModInstanceList.McMcInstanceSelected.Info.VanillaName));
        }
        else
        {
            text = text.Replace("{version_path}", replacer(null));
            text = text.Replace("{verpath}", replacer(null));
            text = text.Replace("{version_indie}", replacer(null));
            text = text.Replace("{verindie}", replacer(null));
            text = text.Replace("{name}", replacer(null));
            text = text.Replace("{version}", replacer(null));
        }

        // 登录信息
        if (mcLoginLoader.State == ModBase.LoadState.Finished)
        {
            text = text.Replace("{user}", replacer(mcLoginLoader.output.Name));
            text = text.Replace("{uuid}", replacer(mcLoginLoader.output.Uuid?.ToLower()));
            switch (mcLoginLoader.input.LoginType)
            {
                case McLoginType.Legacy:
                {
                    text = text.Replace("{login}", replacer("离线"));
                    break;
                }
                case McLoginType.Ms:
                {
                    text = text.Replace("{login}", replacer("正版"));
                    break;
                }
                case McLoginType.Auth:
                {
                    text = text.Replace("{login}", replacer("Authlib-Injector"));
                    break;
                }
            }
        }
        else
        {
            text = text.Replace("{user}", replacer(null));
            text = text.Replace("{uuid}", replacer(null));
            text = text.Replace("{login}", replacer(null));
        }

        return text;
    }

    #endregion
}

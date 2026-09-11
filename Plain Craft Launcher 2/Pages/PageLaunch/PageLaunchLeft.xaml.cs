using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Minecraft.Profile;
using PCL.Core.Minecraft.Profile.Models;
using PCL.Core.Utils;
using PCL.Network;

namespace PCL;

public partial class PageLaunchLeft
{
    private double actualUsedHeight;
    private double actualUsedWidth;
    private int btnLaunchState;
    private string _btnLaunchLanguage;
    private McInstance btnLaunchVersion;
    private bool isHeightAnimating;
    public interface ILoginPage { void Reload(); }

    private enum LaunchButtonAction
    {
        Loading,
        Launch,
        Download,
        Disabled
    }

    private LaunchButtonAction _launchButtonAction;

    private static string StageWaitWindow => Lang.Text("Minecraft.Launch.Stage.WaitWindow");
    private static string StageEnd => Lang.Text("Minecraft.Launch.Stage.End");
    private static string StageRoot => Lang.Text("Minecraft.Launch.Stage.Root");

    // 加载当前实例
    private bool isLoad;

    private bool isLoadFinished;

    // 尺寸改变动画
    private bool isWidthAnimating;
    private double showProgress;

    public PageLaunchLeft()
    {
        InitializeComponent();
        Loaded += PageLaunchLeft_Loaded;
        WeakLanguageChanged.Add(this, OnLanguageChanged);
        // Handles
        BtnInstance.Click += BtnInstance_Click;
        BtnLaunch.Click += BtnLaunch_Click;
        BtnLaunch.Loaded += (_, _) => RefreshButtonsUI();
        BtnCancel.Click += BtnCancel_Click;
        BtnMore.Click += BtnMore_Click;
        PanLaunchingInfo.SizeChanged += PanLaunchingInfo_SizeChangedW;
        PanLaunchingInfo.SizeChanged += PanLaunchingInfo_SizeChangedH;
    }
    private static void OnLanguageChanged(PageLaunchLeft page) => ModBase.RunInUi(page.RefreshButtonsUI);

    public void PageLaunchLeft_Loaded(object sender, RoutedEventArgs e)
    {
        if (isLoad)
            RefreshPage(false);

        AprilPosTrans.X = 0d;
        AprilPosTrans.Y = 0d;

        if (isLoad)
            return;
        isLoad = true;
        ModAnimation.AniControlEnabled += 1;

        // 开始按钮
        ModInstanceList.mcInstanceListLoader.LoadingStateChanged += (_, _) => RefreshButtonsUI();
        ModFolder.mcFolderListLoader.LoadingStateChanged += (_, _) => RefreshButtonsUI();
        RefreshButtonsUI();

        // 初始化档案
        ProfileService.Load();
        if (ProfileService.LastUsedProfile >= 0 && ProfileService.LastUsedProfile < ProfileService.Profiles.Count)
            ProfileService.SelectAt(ProfileService.LastUsedProfile);

        // 加载实例
        ModBase.RunInNewThread(() =>
        {
            // 自动整合包安装：准备
            string packInstallPath = null;
            if (File.Exists(Path.Combine(ModBase.exePath, "modpack.zip")))
                packInstallPath = Path.Combine(ModBase.exePath, "modpack.zip");
            if (File.Exists(Path.Combine(ModBase.exePath, "modpack.mrpack")))
                packInstallPath = Path.Combine(ModBase.exePath, "modpack.mrpack");
            if (packInstallPath is not null)
            {
                ModBase.Log("[Launch] 需自动安装整合包：" + packInstallPath, ModBase.LogLevel.Debug);
                States.Game.SelectedFolder = @"$.minecraft\";
                if (!Directory.Exists(ModBase.exePath + @".minecraft\"))
                {
                    Directory.CreateDirectory(ModBase.exePath + @".minecraft\");
                    Directory.CreateDirectory(ModBase.exePath + @".minecraft\versions\");
                    ModFolder.McFolderLauncherProfilesJsonCreate(ModBase.exePath + @".minecraft\");
                }

                PageSelectLeft.AddFolder(ModBase.exePath + @".minecraft\",
                    ModBase.GetFolderNameFromPath(ModBase.exePath), false);
                ModFolder.mcFolderListLoader.WaitForExit();
            }

            // 确认 Minecraft 文件夹存在
            ModFolder.mcFolderSelected =
                States.Game.SelectedFolder.ToString().Replace("$", ModBase.exePath);
            if (string.IsNullOrEmpty(ModFolder.mcFolderSelected) || !Directory.Exists(ModFolder.mcFolderSelected))
            {
                // 无效的文件夹
                if (string.IsNullOrEmpty(ModFolder.mcFolderSelected))
                    ModBase.Log("[Launch] 没有已储存的 Minecraft 文件夹");
                else
                    ModBase.Log("[Launch] Minecraft 文件夹无效，该文件夹已不存在：" + ModFolder.mcFolderSelected,
                        ModBase.LogLevel.Debug);
                ModFolder.mcFolderListLoader.WaitForExit(isForceRestart: true);
                States.Game.SelectedFolder = ModFolder.mcFolderList[0].Location.Replace(ModBase.exePath, "$");
            }

            ModBase.Log("[Launch] Minecraft 文件夹：" + ModFolder.mcFolderSelected);
            if (Config.Debug.AddRandomDelay)
                Thread.Sleep(RandomUtils.NextInt(500, 3000));
            // 自动整合包安装
            if (packInstallPath is not null)
                try
                {
                    var installLoader = ModModpack.ModpackInstall(packInstallPath);
                    ModBase.Log("[Launch] 自动安装整合包已开始：" + packInstallPath);
                    installLoader.WaitForExit();
                    if (installLoader.State == ModBase.LoadState.Finished)
                    {
                        ModBase.Log("[Launch] 自动安装整合包成功，清理安装包：" + packInstallPath);
                        if (File.Exists(packInstallPath))
                            File.Delete(packInstallPath);
                    }
                }
                catch (ModBase.CancelledException ex)
                {
                    ModBase.Log(ex, "自动安装整合包被用户取消：" + packInstallPath);
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        Lang.Text("Select.Folder.Error.InstallPack", packInstallPath),
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Select.Folder.Error.InstallPack", packInstallPath));
                }

            // 确认 Minecraft 版本实例
            var selection = States.Game.SelectedInstance;
            var instance = selection == "" ? null : new McInstance(selection);
            if (instance is null || !instance.PathInstance.StartsWithF(ModFolder.mcFolderSelected) ||
                !instance.Check())
            {
                // 无效的实例
                ModBase.Log("[Launch] 当前选择的 Minecraft 实例无效：" + (instance is null ? "null" : instance.PathInstance),
                    instance is null ? ModBase.LogLevel.Normal : ModBase.LogLevel.Debug);
                if (ModInstanceList.mcInstanceListLoader.State != ModBase.LoadState.Finished)
                    ModLoader.LoaderFolderRun(ModInstanceList.mcInstanceListLoader, ModFolder.mcFolderSelected,
                        ModLoader.LoaderFolderRunType.ForceRun, 1, @"versions\", true);
                if (ModInstanceList.mcInstanceList.Count == 0 ||
                    ModInstanceList.mcInstanceList.First().Value[0].Logo.Contains("RedstoneBlock"))
                {
                    instance = null;
                    States.Game.SelectedInstance = "";
                    ModBase.Log("[Launch] 无可用 Minecraft 实例");
                }
                else
                {
                    instance = ModInstanceList.mcInstanceList.First().Value[0];
                    States.Game.SelectedInstance = instance.Name;
                    ModBase.Log("[Launch] 自动选择 Minecraft 实例：" + instance.PathInstance);
                }
            }

            ModBase.RunInUi(() =>
            {
                ModInstanceList.McMcInstanceSelected = instance; // 绕这一圈是为了避免 McInstanceCheck 触发第二次实例改变
                isLoadFinished = true;
                RefreshButtonsUI();
                RefreshPage(false); // 有可能选择的版本变化了，需要重新刷新
                // If IsProfileVaild() = "" Then McLoginLoader.Start() '自动登录
            });
        }, "Instance Check", ThreadPriority.AboveNormal);

        // 改变页面
        RefreshPage(false);

        ModAnimation.AniControlEnabled -= 1;
    }

    // 实例选择按钮
    private void BtnInstance_Click(object sender, MouseButtonEventArgs e)
    {
        if (ModLaunch.mcLaunchLoader.State == ModBase.LoadState.Loading)
            return;
        ModMain.frmMain.PageChange(FormMain.PageType.InstanceSelect);
    }

    // 启动按钮
    public void LaunchButtonClick()
    {
        if (ModLaunch.mcLaunchLoader.State == ModBase.LoadState.Loading || !BtnLaunch.IsEnabled ||
            (ModMain.frmMain.pageRight is not null &&
             ModMain.frmMain.pageRight.PageState != MyPageRight.PageStates.ContentStay &&
             ModMain.frmMain.pageRight.PageState != MyPageRight.PageStates.ContentEnter))
            return;
        // 愚人节处理
        if (ModMain.isAprilEnabled && !ModMain.isAprilGiveup)
        {
            ModMain.isAprilGiveup = true;
            ModMain.frmLaunchLeft.AprilScaleTrans.ScaleX = 1d;
            ModMain.frmLaunchLeft.AprilScaleTrans.ScaleY = 1d;
            ModMain.frmLaunchLeft.AprilPosTrans.X = 0d;
            ModMain.frmLaunchLeft.AprilPosTrans.Y = 0d;
            ModMain.frmMain.BtnExtraApril.ShowRefresh();
        }

        // 实际的启动
        switch (_launchButtonAction)
        {
            case LaunchButtonAction.Launch:
            {
                if (File.Exists(ModInstanceList.McMcInstanceSelected.PathInstance + ".pclignore"))
                {
                    HintService.Hint(Lang.Text("Launch.Home.Instance.InstallingCannotLaunch"), HintType.Error);
                    return;
                }

                ModLaunch.McLaunchStart();
                break;
            }
            case LaunchButtonAction.Download:
            {
                ModMain.frmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall);
                break;
            }
        }
    }

    public void RefreshButtonsUI()
    {
        if (!BtnLaunch.IsLoaded)
            return;
        // 获取当前状态
        int currentState;
        if (!isLoadFinished || ModInstanceList.mcInstanceListLoader.State == ModBase.LoadState.Loading ||
            ModFolder.mcFolderListLoader.State == ModBase.LoadState.Loading)
        {
            currentState = 0;
        }
        else if (ModInstanceList.McMcInstanceSelected is null)
        {
            if (Config.Preference.Hide.PageDownload && !PageSetupUI.HiddenForceShow)
                currentState = 1;
            else
                currentState = 2;
        }
        else
        {
            currentState = 3;
        }

        // 更新状态。
        var currentLanguage = LocalizationService.CurrentLanguage.Code;
        if (currentState == btnLaunchState &&
            currentLanguage == _btnLaunchLanguage &&
            ((ModInstanceList.McMcInstanceSelected is null ? "" : ModInstanceList.McMcInstanceSelected.PathInstance) ?? "") ==
            ((btnLaunchVersion is null ? "" : btnLaunchVersion.PathInstance) ?? ""))
            goto ExitRefresh;
        _btnLaunchLanguage = currentLanguage;
        btnLaunchVersion = ModInstanceList.McMcInstanceSelected;
        btnLaunchState = currentState;
        switch (currentState)
        {
            case 0:
            {
                _launchButtonAction = LaunchButtonAction.Loading;
                ModBase.Log("[Minecraft] 启动按钮：正在加载 Minecraft 实例");
                ModMain.frmLaunchLeft.BtnLaunch.Text = Lang.Text("Launch.Home.Button.Loading");
                ModMain.frmLaunchLeft.BtnLaunch.IsEnabled = false;
                ModMain.frmLaunchLeft.LabVersion.Text = Lang.Text("Launch.Home.Instance.Loading");
                ModMain.frmLaunchLeft.BtnInstance.IsEnabled = false;
                ModMain.frmLaunchLeft.BtnMore.Visibility = Visibility.Collapsed;
                break;
            }
            case 1:
            {
                _launchButtonAction = LaunchButtonAction.Disabled;
                ModBase.Log("[Minecraft] 启动按钮：无 Minecraft 实例，下载已禁用");
                ModMain.frmLaunchLeft.BtnLaunch.Text = Lang.Text("Launch.Home.Button.Launch");
                ModMain.frmLaunchLeft.BtnLaunch.IsEnabled = false;
                ModMain.frmLaunchLeft.LabVersion.Text = Lang.Text("Launch.Home.Instance.NotFound");
                ModMain.frmLaunchLeft.BtnInstance.IsEnabled = true;
                ModMain.frmLaunchLeft.BtnMore.Visibility = Visibility.Collapsed;
                break;
            }
            case 2:
            {
                _launchButtonAction = LaunchButtonAction.Download;
                ModBase.Log("[Minecraft] 启动按钮：无 Minecraft 实例，要求下载");
                ModMain.frmLaunchLeft.BtnLaunch.Text = Lang.Text("Launch.Home.Button.Download");
                ModMain.frmLaunchLeft.BtnLaunch.IsEnabled = true;
                ModMain.frmLaunchLeft.LabVersion.Text = Lang.Text("Launch.Home.Instance.NotFound");
                ModMain.frmLaunchLeft.BtnInstance.IsEnabled = true;
                ModMain.frmLaunchLeft.BtnMore.Visibility = Visibility.Collapsed;
                break;
            }
            case 3:
            {
                _launchButtonAction = LaunchButtonAction.Launch;
                ModBase.Log("[Minecraft] 启动按钮：Minecraft 实例：" + ModInstanceList.McMcInstanceSelected.PathInstance);
                ModMain.frmLaunchLeft.BtnLaunch.Text = Lang.Text("Launch.Home.Button.Launch");
                ModMain.frmLaunchLeft.BtnInstance.IsEnabled = true;
                if (ProfileService.Current is not null)
                    BtnLaunch.IsEnabled = true;
                else
                    BtnLaunch.IsEnabled = false;
                ModMain.frmLaunchLeft.LabVersion.Text = ModInstanceList.McMcInstanceSelected.Name;
                break;
            }
            // FrmLaunchLeft.BtnMore.Visibility = Visibility.Visible '由功能隐藏设置修改
        }

        ExitRefresh: ;

        // 功能隐藏
        ModMain.frmLaunchLeft.BtnInstance.Visibility =
            !PageSetupUI.HiddenForceShow && Config.Preference.Hide.FunctionSelect
                ? Visibility.Collapsed
                : Visibility.Visible;
        if (currentState == 3) ModMain.frmLaunchLeft.BtnMore.Visibility = ModMain.frmLaunchLeft.BtnInstance.Visibility;
    }

    // 取消按钮
    private void BtnCancel_Click(object sender, MouseButtonEventArgs e)
    {
        if (ModLaunch.mcLaunchLoaderReal is not null)
        {
            ModLaunch.mcLaunchLoaderReal.Abort();
            ModLaunch.McLaunchLog("已取消启动");
            try
            {
                if (ModLaunch.mcLaunchWatcher is not null)
                    ModLaunch.mcLaunchWatcher.Kill();
                else if (ModLaunch.mcLaunchProcess is not null)
                    if (!ModLaunch.mcLaunchProcess.HasExited)
                        ModLaunch.mcLaunchProcess.Kill();
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Minecraft.Launch.Error.CancelProcess"),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Minecraft.Launch.Error.CancelProcess"));
            }
        }
    }

    // 实例设置按钮
    private void BtnMore_Click(object sender, MouseButtonEventArgs e)
    {
        if (ModLaunch.mcLaunchLoader.State == ModBase.LoadState.Loading)
            return;
        ModInstanceList.McMcInstanceSelected.Load();
        PageInstanceLeft.McInstance = ModInstanceList.McMcInstanceSelected;
        if (File.Exists(ModInstanceList.McMcInstanceSelected.PathInstance + ".pclignore"))
        {
            HintService.Hint(Lang.Text("Launch.Home.Instance.InstallingCannotSetup"), HintType.Error);
            return;
        }

        ModMain.frmMain.PageChange(FormMain.PageType.InstanceSetup);
    }

    /// <summary>
    ///     每 0.2s 执行一次，刷新启动的数据 UI 显示。
    /// </summary>
    public void LaunchingRefresh()
    {
        try
        {
            if (ModLaunch.mcLaunchLoaderReal.State == ModBase.LoadState.Aborted)
                return;
            // 阶段状态获取
            var isLaunched = false; // 是否已经启动游戏，只是在等待窗口
            do
            {
                try
                {
                    var exitTry = false;
                    foreach (var Loader in ModLaunch.mcLaunchLoaderReal.GetLoaderList(false))
                        if (Loader.State == ModBase.LoadState.Loading || Loader.State == ModBase.LoadState.Waiting)
                        {
                            LabLaunchingStage.Text = Loader.name;
                            isLaunched = Loader.name == StageWaitWindow || Loader.name == StageEnd;
                            exitTry = true;
                            break;
                        }

                    if (exitTry) break;
                    LabLaunchingStage.Text = Lang.Text("Launch.Status.Completed");
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "获取是否启动完成失败，可能是由于启动状态改变导致集合已修改");
                    return;
                }
            } while (false);

            if (ModAnimation.AniIsRun("Launch State Page"))
                isLaunched = false; // 等待页面切换动画完成
            // 计算应显示的进度
            var actualProgress = ModLaunch.mcLaunchLoaderReal.Progress;
            if (actualProgress >= showProgress)
                showProgress += (actualProgress - showProgress) * 0.2d + 0.005d; // 向实际进度靠一点
            if (actualProgress <= showProgress)
                showProgress = actualProgress; // 原来或处理后变得比实际进度高，直接回退
            if (isLaunched)
                showProgress = 1d; // 如果已经完成了，就不卖关子了
            // 文本
            LabLaunchingTitle.Text = isLaunched ? Lang.Text("Launch.Status.Title.Launched") :
                ModLaunch.currentLaunchOptions.SaveBatch is null ? Lang.Text("Launch.Status.Title.Launching") : Lang.Text("Launch.Status.Title.ExportingScript");
            LabLaunchingProgress.Text = Lang.Number(showProgress, "P2");
            var hasLaunchDownloader = false;
            try
            {
                foreach (var Loader in ModNet.NetManager.Tasks)
                    if (Loader.RealParent is not null && Loader.RealParent.name == StageRoot &&
                        Loader.State == ModBase.LoadState.Loading)
                        hasLaunchDownloader = true;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "获取 Minecraft 启动下载器失败，可能是因为启动被取消");
                hasLaunchDownloader = false;
            }

            LabLaunchingDownload.Text = ModBase.GetString(ModNet.NetManager.Speed) + "/s";
            var shouldShowHint = Config.Preference.ShowLaunchingHint;
            // 进度改变动画
            var animList = new List<ModAnimation.AniData>
            {
                ModAnimation.AaGridLengthWidth(ProgressLaunchingFinished,
                    showProgress - ProgressLaunchingFinished.Width.Value, 260,
                    ease: new ModAnimation.AniEaseOutFluent()),
                ModAnimation.AaGridLengthWidth(ProgressLaunchingUnfinished,
                    1d - showProgress - ProgressLaunchingUnfinished.Width.Value, 260,
                    ease: new ModAnimation.AniEaseOutFluent())
            };
            var isDownloadStateChanged =
                hasLaunchDownloader == (LabLaunchingDownload.Visibility == Visibility.Collapsed);
            if (isDownloadStateChanged)
            {
                LabLaunchingDownload.Visibility = Visibility.Visible;
                LabLaunchingDownloadLeft.Visibility = Visibility.Visible;
                animList.AddRange(new[]
                {
                    ModAnimation.AaOpacity(LabLaunchingDownload,
                        (hasLaunchDownloader ? 1 : 0) - LabLaunchingDownload.Opacity, 100),
                    ModAnimation.AaOpacity(LabLaunchingDownloadLeft,
                        (hasLaunchDownloader ? 0.5d : 0d) - LabLaunchingDownloadLeft.Opacity, 100),
                    ModAnimation.AaCode(() =>
                    {
                        if (!hasLaunchDownloader)
                        {
                            LabLaunchingDownload.Visibility = Visibility.Collapsed;
                            LabLaunchingDownloadLeft.Visibility = Visibility.Collapsed;
                        }
                    }, 110)
                });
            }

            var isProgressStateChanged = !isLaunched == (LabLaunchingProgress.Visibility == Visibility.Collapsed);
            if (isProgressStateChanged)
            {
                LabLaunchingProgress.Visibility = Visibility.Visible;
                LabLaunchingProgressLeft.Visibility = Visibility.Visible;
                if (isLaunched && shouldShowHint) PanLaunchingHint.Visibility = Visibility.Visible;
                animList.AddRange(new[]
                {
                    ModAnimation.AaOpacity(LabLaunchingProgress, (!isLaunched ? 1 : 0) - LabLaunchingProgress.Opacity,
                        100),
                    ModAnimation.AaOpacity(LabLaunchingProgressLeft,
                        (!isLaunched ? 0.5d : 0d) - LabLaunchingProgressLeft.Opacity, 100),
                    ModAnimation.AaOpacity(PanLaunchingHint,
                        (isLaunched && shouldShowHint ? 1 : 0) - PanLaunchingHint.Opacity, 100)
                });
            }

            ModAnimation.AniStart(animList, "Launching Progress");
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Minecraft.Launch.Error.RefreshInfo"),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Minecraft.Launch.Error.RefreshInfo"));
        }
    }

    private void PanLaunchingInfo_SizeChangedW(object sender, SizeChangedEventArgs e)
    {
        var deltaWidth = e.NewSize.Width - e.PreviousSize.Width;
        if (e.PreviousSize.Width == 0d || isWidthAnimating || Math.Abs(deltaWidth) < 1d ||
            PanLaunchingInfo.ActualWidth == 0d)
            return;
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaWidth(PanLaunchingInfo, deltaWidth, 180, ease: new ModAnimation.AniEaseOutFluent()),
            ModAnimation.AaCode(() =>
            {
                isWidthAnimating = false;
                PanLaunchingInfo.Width = actualUsedWidth;
            }, after: true)
        }, "Launching Info Width");
        isWidthAnimating = true;
        actualUsedWidth = PanLaunchingInfo.Width;
        PanLaunchingInfo.Width = e.PreviousSize.Width;
    }

    private void PanLaunchingInfo_SizeChangedH(object sender, SizeChangedEventArgs e)
    {
        var deltaHeight = e.NewSize.Height - e.PreviousSize.Height;
        if (e.PreviousSize.Height == 0d || isHeightAnimating || Math.Abs(deltaHeight) < 1d ||
            PanLaunchingInfo.ActualHeight == 0d)
            return;
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaHeight(PanLaunchingInfo, deltaHeight, 180, ease: new ModAnimation.AniEaseOutFluent()),
            ModAnimation.AaCode(() =>
            {
                isHeightAnimating = false;
                PanLaunchingInfo.Height = actualUsedHeight;
            }, after: true)
        }, "Launching Info Height");
        isHeightAnimating = true;
        actualUsedHeight = PanLaunchingInfo.Height;
        PanLaunchingInfo.Height = e.PreviousSize.Height;
    }

    // 启动游戏按钮
    private void BtnLaunch_Click(object sender, MouseButtonEventArgs e)
    {
        LaunchButtonClick();
    }

    #region 切换大页面

    /// <summary>
    ///     切换至启动中页面。
    /// </summary>
    public void PageChangeToLaunching()
    {
        // 修改验证方式
        switch (ProfileService.Current?.ProfileType)
        {
            case ProfileType.Offline:
            {
                LabLaunchingMethod.Text = Lang.Text("Launch.Account.Type.Offline");
                break;
            }
            case ProfileType.Microsoft:
            {
                LabLaunchingMethod.Text = Lang.Text("Launch.Account.Type.Microsoft");
                break;
            }
            case ProfileType.Authlib:
            case ProfileType.YggdrasilConnect:
            {
                LabLaunchingMethod.Text = Lang.Text("Launch.Account.Type.ThirdParty") + (!string.IsNullOrEmpty(ProfileService.Current?.ServerName)
                    ? " / " + ProfileService.Current.ServerName
                    : "");
                break;
            }
        }

        // 初始化页面
        LabLaunchingName.Text = ModInstanceList.McMcInstanceSelected.Name;
        LabLaunchingStage.Text = Lang.Text("Common.Action.Initialize");
        LabLaunchingTitle.Text = ModLaunch.currentLaunchOptions?.SaveBatch is null
            ? Lang.Text("Launch.Status.Title.Launching")
            : Lang.Text("Launch.Status.Title.ExportingScript");
        LabLaunchingProgress.Text = Lang.Number(0d, "P2");
        LabLaunchingProgress.Opacity = 1d;
        LabLaunchingDownload.Visibility = Visibility.Visible;
        LabLaunchingProgressLeft.Opacity = 0.6d;
        LabLaunchingDownload.Visibility = Visibility.Visible;
        LabLaunchingDownload.Text = ModBase.GetString(0) + "/s";
        LabLaunchingDownload.Opacity = 0d;
        LabLaunchingDownload.Visibility = Visibility.Collapsed;
        LabLaunchingDownloadLeft.Opacity = 0d;
        LabLaunchingDownloadLeft.Visibility = Visibility.Collapsed;
        ProgressLaunchingFinished.Width = new GridLength(0d, GridUnitType.Star);
        ProgressLaunchingUnfinished.Width = new GridLength(1d, GridUnitType.Star);
        PanLaunchingHint.Opacity = 0d;
        PanLaunchingHint.Visibility = Visibility.Collapsed;
        PanLaunchingInfo.Width = double.NaN; // 重置宽度改变动画
        ModLaunch.mcLaunchProcess = null;
        ModLaunch.mcLaunchWatcher = null;

        var shouldShowHint = Config.Preference.ShowLaunchingHint;
        if (shouldShowHint)
            LabLaunchingHint.Text = PageLaunchRight.GetRandomHint(true, true);
        else
            LabLaunchingHint.Text = "";

        // 初始化其他页面
        PanInput.IsHitTestVisible = false;
        PanLaunching.IsHitTestVisible = false;
        LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Run;
        PanLaunching.Visibility = Visibility.Visible;
        ModAnimation.AniStart(
            new[]
            {
                ModAnimation.AaOpacity(PanInput, 0d, 50),
                ModAnimation.AaOpacity(PanInput, -PanInput.Opacity, 110, ease: new ModAnimation.AniEaseInFluent(),
                    after: true),
                ModAnimation.AaScaleTransform(PanInput, 1.2d - ((ScaleTransform)PanInput.RenderTransform).ScaleX, 160),
                ModAnimation.AaOpacity(PanLaunching, 1d - PanLaunching.Opacity, 150, 100),
                ModAnimation.AaScaleTransform(PanLaunching, 1d - ((ScaleTransform)PanLaunching.RenderTransform).ScaleX,
                    500, 100, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)),
                ModAnimation.AaCode(() => PanLaunching.IsHitTestVisible = true, 150)
            }, "Launch State Page"); // 略作延迟，这样如果预检测失败，不会出现奇怪的弹一下的动画
    }

    /// <summary>
    ///     切换至登录页面。
    /// </summary>
    public void PageChangeToLogin()
    {
        if (PageGet(pageCurrent) is ILoginPage loginPage) loginPage.Reload();
        PanInput.IsHitTestVisible = false;
        PanLaunching.IsHitTestVisible = false;
        LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Stop;
        PanInput.Visibility = Visibility.Visible;
        ModAnimation.AniStart(
            new[]
            {
                ModAnimation.AaOpacity(PanLaunching, -PanLaunching.Opacity, 150),
                ModAnimation.AaScaleTransform(PanLaunching,
                    0.8d - ((ScaleTransform)PanLaunching.RenderTransform).ScaleX, 150,
                    ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)),
                ModAnimation.AaOpacity(PanInput, 1d - PanInput.Opacity, 250, 50),
                ModAnimation.AaScaleTransform(PanInput, 1d - ((ScaleTransform)PanInput.RenderTransform).ScaleX, 300, 50,
                    new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)),
                ModAnimation.AaCode(() => PanInput.IsHitTestVisible = true, 200)
            }, "Launch State Page", true);
    }

    #endregion

    #region 切换登录页面

    private enum PageType
    {
        None,
        Auth,
        Ms,
        Profile,
        ProfileSkin,
        Offline
    }

    /// <summary>
    ///     当前页面的种类。
    /// </summary>
    private PageType pageCurrent = PageType.None;

    private object PageGet(PageType type)
    {
        switch (type)
        {
            case PageType.Auth:
            {
                if (ModMain.frmLoginAuth is null)
                    ModMain.frmLoginAuth = new PageLoginAuth();
                return ModMain.frmLoginAuth;
            }
            case PageType.Ms:
            {
                if (ModMain.frmLoginMs is null)
                    ModMain.frmLoginMs = new PageLoginMs();
                return ModMain.frmLoginMs;
            }
            case PageType.Profile:
            {
                if (ModMain.frmLoginProfile is null)
                    ModMain.frmLoginProfile = new PageLoginProfile();
                return ModMain.frmLoginProfile;
            }
            case PageType.ProfileSkin:
            {
                if (ModMain.frmLoginProfileSkin is null)
                    ModMain.frmLoginProfileSkin = new PageLoginProfileSkin();
                return ModMain.frmLoginProfileSkin;
            }
            case PageType.Offline:
            {
                if (ModMain.frmLoginOffline is null)
                    ModMain.frmLoginOffline = new PageLoginOffline();
                return ModMain.frmLoginOffline;
            }

            default:
            {
                throw new ArgumentOutOfRangeException("Type", "即将切换的登录分页编号越界");
            }
        }
    }

    /// <summary>
    ///     切换现有登录页面种类，返回新页面的实例。
    /// </summary>
    /// <param name="type">新页面的种类。</param>
    /// <param name="anim">是否显示动画。</param>
    private object PageChange(PageType type, bool anim)
    {
        object pageNew = ModMain.frmLoginMs; // 初始化一个东西，避免在执行时出现异常导致雪崩
        try
        {
            #region 确定更改的页面实例并实例化

            if (pageCurrent == type)
                return pageNew;
            pageNew = PageGet(type);

            #endregion

            #region 切换页面

            ModAnimation.AniStop("FrmLogin PageChange");
            // 清除页面关联性
            if (pageNew is FrameworkElement element && element.Parent is not null)
            {
                element.SetValue(ContentPresenter.ContentProperty, null);
            }
            if (anim)
            {
                // 动画
                // 执行动画
                Dispatcher.Invoke(() => ModAnimation.AniStart(new[]
                {
                    ModAnimation.AaOpacity(PanLogin, -PanLogin.Opacity, 100, ease: new ModAnimation.AniEaseOutFluent()),
                    ModAnimation.AaCode(() =>
                    {
                        ModAnimation.AniControlEnabled += 1;
                        PanLogin.Children.Clear();
                        PanLogin.Children.Add((UIElement)pageNew);
                        ModAnimation.AniControlEnabled -= 1;
                    }, 100),
                    ModAnimation.AaOpacity(PanLogin, 1d, 100, 120, new ModAnimation.AniEaseInFluent())
                }, "FrmLogin PageChange"), DispatcherPriority.Render);
            }
            else
            {
                // 无动画
                ModAnimation.AniControlEnabled += 1;
                PanLogin.Children.Clear();
                PanLogin.Children.Add((UIElement)pageNew);
                ModAnimation.AniControlEnabled -= 1;
            }

            #endregion

            pageCurrent = type;
            return pageNew;
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Launch.Account.Error.SwitchPage", ModBase.GetStringFromEnum(type)),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Launch.Account.Error.SwitchPage", ModBase.GetStringFromEnum(type)));
            return pageNew;
        }
    }

    /// <summary>
    ///     确认当前显示的子页面正确，并刷新该页面。
    /// </summary>
    /// <param name="anim">是否显示动画</param>
    /// <param name="targetLoginType">目标验证方式，若正在创建档案需填</param>
    public void RefreshPage(bool anim, ModLaunch.McLoginType targetLoginType = default)
    {
        var type = default(PageType);
        if (targetLoginType != default)
        {
            if (targetLoginType == ModLaunch.McLoginType.Ms)
                type = PageType.Ms;
            if (targetLoginType == ModLaunch.McLoginType.Auth)
                type = PageType.Auth;
            if (targetLoginType == ModLaunch.McLoginType.Legacy)
                type = PageType.Offline;
        }
        else if (ProfileService.Current is not null)
        {
            type = PageType.ProfileSkin;
            BtnLaunch.IsEnabled = true;
        }
        else
        {
            type = PageType.Profile;
            if (_launchButtonAction != LaunchButtonAction.Download)
                BtnLaunch.IsEnabled = false;
        }

        // 刷新页面
        if (pageCurrent == type)
            return;
        PageChange(type, anim);
    }

    #endregion

    #region 皮肤

    // 正版皮肤
    public static ModLoader.LoaderTask<ModBase.EqualableList<string>, string> skinMs = new("Loader Skin Ms", SkinMsLoad,
        SkinMsInput, ThreadPriority.AboveNormal);

    private static ModBase.EqualableList<string> SkinMsInput()
    {
        // 获取名称
        return new ModBase.EqualableList<string>
            { ProfileService.Current?.UserName ?? "", ProfileService.Current?.Uuid ?? "" };
    }

    private static void SkinMsLoad(ModLoader.LoaderTask<ModBase.EqualableList<string>, string> data)
    {
        // 清空已有皮肤
        // 如果在输入时清空皮肤，若输入内容一样则不会执行 Load 方法，导致皮肤不被加载
        ModBase.RunInUi(() =>
        {
            if (ModMain.frmLoginProfileSkin is not null && ModMain.frmLoginProfileSkin.Skin is not null)
                ModMain.frmLoginProfileSkin.Skin.Clear();
        });
        // 获取 Url
        var userName = data.input[0];
        var uuid = data.input[1];
        if (ProfileService.Current is not null)
        {
            userName = ProfileService.Current.UserName;
            uuid = ProfileService.Current.Uuid;
        }

        if (string.IsNullOrEmpty(userName))
        {
            data.output = ModBase.pathImage + "Skins/" + ModSkin.McSkinSex(ProfileUi.GetOfflineUuid(userName)) +
                          ".png";
            ModBase.Log("[Minecraft] 获取微软正版皮肤失败，ID 为空");
            goto Finish;
        }

        try
        {
            var result = ModSkin.McSkinGetAddress(uuid, "Ms");
            if (data.IsAborted)
                throw new ThreadInterruptedException("当前任务已取消：" + userName);
            result = ModSkin.McSkinDownload(result);
            if (data.IsAborted)
                throw new ThreadInterruptedException("当前任务已取消：" + userName);
            data.output = result;
        }
        catch (Exception ex)
        {
            if (ex is ThreadInterruptedException)
            {
                data.output = "";
                ModBase.Log("[Minecraft] 已取消皮肤获取：" + userName);
                return;
            }

            if (ex.ToString().Contains("429"))
            {
                data.output = ModBase.pathImage + "Skins/" +
                              ModSkin.McSkinSex(ProfileUi.GetOfflineUuid(userName)) + ".png";
                ModBase.Log(
                    Lang.Text("Launch.Skin.Error.MsRateLimited", userName),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Launch.Skin.Error.MsRateLimited", userName));
            }
            else if (ex.ToString().Contains("未设置自定义皮肤"))
            {
                data.output = ModBase.pathImage + "Skins/" +
                               ModSkin.McSkinSex(ProfileUi.GetOfflineUuid(userName)) + ".png";
                ModBase.Log("[Minecraft] 用户未设置自定义皮肤，跳过皮肤加载");
            }
            else
            {
                data.output = ModBase.pathImage + "Skins/" +
                               ModSkin.McSkinSex(ProfileUi.GetOfflineUuid(userName)) + ".png";
                ModBase.Log(
                    ex,
                    Lang.Text("Launch.Skin.Error.MsGet", userName),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Launch.Skin.Error.MsGet", userName));
            }
        }

        Finish: ;

        // 刷新显示
        if (ModMain.frmLoginProfileSkin is not null && ReferenceEquals(ModMain.frmLoginProfileSkin.Skin.loader, data))
            ModBase.RunInUi(ModMain.frmLoginProfileSkin.Skin.Load);
        else if (!data.IsAborted) // 如果已经中断，Input 也被清空，就不会再次刷新
            data.input = null; // 清空输入，因为皮肤实际上没有被渲染，如果不清空切换到页面的 Start 会由于输入相同而不渲染
    }

    // 离线皮肤
    public static ModLoader.LoaderTask<ModBase.EqualableList<string>, string> skinLegacy = new("Loader Skin Legacy",
        SkinLegacyLoad, SkinLegacyInput, ThreadPriority.AboveNormal);

    private static ModBase.EqualableList<string> SkinLegacyInput()
    {
        return new ModBase.EqualableList<string>
            { ProfileService.Current?.UserName ?? "", ProfileService.Current?.Uuid ?? "" };
    }

    private static void SkinLegacyLoad(ModLoader.LoaderTask<ModBase.EqualableList<string>, string> data)
    {
        // 清空已有皮肤
        ModBase.RunInUi(() =>
        {
            if (ModMain.frmLoginProfileSkin is not null && ModMain.frmLoginProfileSkin.Skin is not null)
                ModMain.frmLoginProfileSkin.Skin.Clear();
        });
        data.output = ModBase.pathImage + "Skins/" + ModSkin.McSkinSex(data.input[1]) + ".png";
        // 刷新显示
        if (ModMain.frmLoginProfileSkin is not null && ReferenceEquals(ModMain.frmLoginProfileSkin.Skin.loader, data))
            ModBase.RunInUi(() => ModMain.frmLoginProfileSkin.Skin.Load());
        else if (!data.IsAborted) // 如果已经中断，Input 也被清空，就不会再次刷新
            data.input = null; // 清空输入，因为皮肤实际上没有被渲染，如果不清空切换到页面的 Start 会由于输入相同而不渲染
    }

    // Authlib-Injector 皮肤
    public static ModLoader.LoaderTask<ModBase.EqualableList<string>, string> skinAuth = new("Loader Skin Auth",
        SkinAuthLoad, SkinAuthInput, ThreadPriority.AboveNormal);

    private static ModBase.EqualableList<string> SkinAuthInput()
    {
        // 获取名称
        return new ModBase.EqualableList<string>
            { ProfileService.Current?.UserName ?? "", ProfileService.Current?.Uuid ?? "" };
    }

    private static void SkinAuthLoad(ModLoader.LoaderTask<ModBase.EqualableList<string>, string> data)
    {
        // 清空已有皮肤
        // 如果在输入时清空皮肤，若输入内容一样则不会执行 Load 方法，导致皮肤不被加载
        ModBase.RunInUi(() =>
        {
            if (ModMain.frmLoginProfileSkin is not null && ModMain.frmLoginProfileSkin.Skin is not null)
                ModMain.frmLoginProfileSkin.Skin.Clear();
        });
        // 获取 Url
        var userName = data.input[0];
        var uuid = data.input[1];
        if (string.IsNullOrEmpty(userName))
        {
            data.output = ModBase.pathImage + "Skins/Steve.png";
            ModBase.Log("[Minecraft] 获取 Authlib-Injector 皮肤失败，ID 为空");
            goto Finish;
        }

        try
        {
            var result = ModSkin.McSkinGetAddress(uuid, "Auth");
            if (data.IsAborted)
                throw new ThreadInterruptedException("当前任务已取消：" + userName);
            result = ModSkin.McSkinDownload(result);
            if (data.IsAborted)
                throw new ThreadInterruptedException("当前任务已取消：" + userName);
            data.output = result;
        }
        catch (Exception ex)
        {
            if (ex is ThreadInterruptedException)
            {
                data.output = "";
                return;
            }

            if (ex.ToString().Contains("429"))
            {
                data.output = ModBase.pathImage + "Skins/Steve.png";
                ModBase.Log(
                    $"[Minecraft] 获取 Authlib-Injector 皮肤失败（{userName}）：获取皮肤太过频繁，请 5 分钟后再试！",
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Launch.Skin.Error.AuthlibRateLimited"));
            }
            else if (ex.ToString().Contains("未设置自定义皮肤"))
            {
                data.output = ModBase.pathImage + "Skins/Steve.png";
                ModBase.Log("[Minecraft] 用户未设置自定义皮肤，跳过皮肤加载");
            }
            else
            {
                data.output = ModBase.pathImage + "Skins/Steve.png";
                ModBase.Log(
                    ex,
                    Lang.Text("Launch.Skin.Error.AuthGet", userName),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Launch.Skin.Error.AuthGet", userName));
            }
        }

        Finish: ;

        // 刷新显示
        if (ModMain.frmLoginProfileSkin is not null && ReferenceEquals(ModMain.frmLoginProfileSkin.Skin.loader, data))
            ModBase.RunInUi(ModMain.frmLoginProfileSkin.Skin.Load);
        else if (!data.IsAborted) // 如果已经中断，Input 也被清空，就不会再次刷新
            data.input = null; // 清空输入，因为皮肤实际上没有被渲染，如果不清空切换到页面的 Start 会由于输入相同而不渲染
    }

    // 全部皮肤加载器
    // 需要放在其中元素的后面，否则会因为它提前被加载而莫名其妙变成 Nothing
    public static List<ModLoader.LoaderTask<ModBase.EqualableList<string>, string>> skinLoaders = new()
        { skinMs, skinLegacy, skinAuth };

    #endregion
}

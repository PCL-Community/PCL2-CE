using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PCL.Core.App;
using PCL.Core.Minecraft.Profile;
using PCL.Core.App.IoC;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.Minecraft;
using PCL.Core.UI;
using PCL.Core.UI.Theme;
using PCL.Core.Utils;
using PCL.Core.Utils.OS;
using PCL.Core.Utils.Validate;
using PCL.Network;

namespace PCL;

public partial class FormMain
{
    // 愚人节鼠标位置
    public MouseEventArgs lastMouseArg;

    private void FormMain_MouseMove(object sender, MouseEventArgs e)
    {
        lastMouseArg = e;
    }

    #region 基础

    // 更新日志
    private void ShowUpdateLog()
    {
        ModBase.RunInNewThread(() =>
        {
            var changelogFile = $"{ModBase.pathTemp}CEUpdateLog.md";
            string changelog;
            if (File.Exists(changelogFile))
                changelog = ModBase.ReadFile(changelogFile);
            else
                changelog = Lang.Text("Main.UpdateLog.Empty");
            if (ModMain.MyMsgBoxMarkdown(changelog,
                    Lang.Text("Main.UpdateLog.Title", ModBase.versionBranchName, ModBase.versionBaseName), Lang.Text("Common.Action.Confirm"), Lang.Text("Main.UpdateLog.FullChangelog")) ==
                2) ModBase.OpenWebsite("https://github.com/PCL-Community/PCL2-CE/releases");
        }, "UpdateLog Output");
    }

    // 窗口加载
    private bool isWindowLoadFinished;
    private readonly DragHelper _helper = new();

    public FormMain()
    {
        ModBase.applicationStartTick = TimeUtils.GetTimeTick();
        // 刷新主题
        // ThemeCheckAll(False)
        // ThemeRefreshColor()
        ThemeService.ColorModeChanged += (_, _) => ThemeManager.ThemeRefresh();
        ThemeService.ColorThemeChanged += theme => ThemeManager.ThemeRefresh((int)theme);
        // 窗体参数初始化
        ModMain.frmMain = this;
        ModMain.frmLaunchLeft = new PageLaunchLeft();
        ModMain.frmLaunchRight = new PageLaunchRight();
        // 版本号改变
        var lastVersion = States.System.LastVersion;
        if (lastVersion < ModBase.versionCode)
        {
            // 重新询问是否启用遥测数据收集
            if (lastVersion <= 511)
            {
                if (!Config.System.TelemetryConfig.IsDefault() && Config.System.Telemetry)
                {
                    Config.System.TelemetryConfig.Reset();
                    ModBase.Log("[Start] 遥测策略变更：由旧版本升级到含新版遥测的版本，已重置遥测设置");
                }
            }
            // 触发升级
            UpgradeSub(lastVersion);
        }
        else if (lastVersion > ModBase.versionCode)
            // 触发降级
            DowngradeSub(lastVersion);

        _ = Config.Preference.Theme.ThemeSelected;
        // 注册拖拽事件（不能直接加 Handles，否则没用；#6340）
        AddHandler(DragDrop.DragEnterEvent, new DragEventHandler(HandleDrag), true);
        AddHandler(DragDrop.DragOverEvent, new DragEventHandler(HandleDrag), true);
        // 注册 MsgBox 事件
        MsgBoxWrapper.OnShow += ModMain.MsgBoxWrapper_OnShow;
        // 注册 Hint 事件
        HintWrapper.OnShow += HintService.HintWrapper_OnShow;
        // 加载 UI
        InitializeComponent();
        Opacity = 0d;
        try
        {
            Height = States.UI.WindowHeight;
            Width = States.UI.WindowWidth;
        }
        catch (Exception ex) // 修复 #2019
        {
            ModBase.Log(
                ex,
                "读取窗口默认大小失败",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Main.Error.OperationFailed"));
            Height = MinHeight + 100d;
            Width = MinWidth + 100d;
        }

        // 管理员权限下文件拖拽
        if (ProcessInterop.IsAdmin())
        {
            ModBase.Log("[Start] PCL 当前正以管理员权限运行");
            SourceInitialized += (_, _) =>
            {
                var windowInterop = new WindowInteropHelper(this);
                _helper.HwndSource = HwndSource.FromHwnd(windowInterop.Handle);
                _helper.AddHook();
            };
            Closing += (_, _) => _helper.RemoveHook();
            _helper.DragDrop += (_, _) => FileDrag(_helper.DropFilePaths);
        }

        if (ModMain.frmLaunchLeft.Parent is not null)
            ModMain.frmLaunchLeft.SetValue(ContentPresenter.ContentProperty, null);
        if (ModMain.frmLaunchRight.Parent is not null)
            ModMain.frmLaunchRight.SetValue(ContentPresenter.ContentProperty, null);
        PanMainLeft.Child = ModMain.frmLaunchLeft;
        pageLeft = ModMain.frmLaunchLeft;
        PanMainRight.Child = ModMain.frmLaunchRight;
        pageRight = ModMain.frmLaunchRight;
        ModMain.frmLaunchRight.PageState = MyPageRight.PageStates.ContentStay;
        // 调试模式提醒
        if (ModBase.modeDebug)
            HintService.Hint(Lang.Text("Main.DebugMode.Hint"));
        // 尽早执行的加载池
        ModFolder.mcFolderListLoader
            .Start(0); // 为了让下载已存在文件检测可以正常运行，必须跑一次；为了让启动按钮尽快可用，需要尽早执行；为了与 PageLaunchLeft 联动，需要为 0 而不是 GetUuid

        ModBase.Log("[Start] 第二阶段加载用时：" + (TimeUtils.GetTimeTick() - ModBase.applicationStartTick) + " ms");
        // 注册生命周期状态事件
        Lifecycle.When(LifecycleState.WindowCreated, FormMain_Loaded);
    }

    private void FormMain_Loaded() // (sender As Object, e As RoutedEventArgs) Handles Me.Loaded
    {
        FormMain_SizeChanged();
        ModBase.applicationStartTick = TimeUtils.GetTimeTick();
        ModBase.frmHandle = new WindowInteropHelper(this).Handle;
        // 读取设置
        PageSetupUI.BackgroundRefresh(false, true);
        ModMusic.MusicRefreshPlay(false, true);
        // 扩展按钮
        BtnExtraUpdateRestart.showCheck = BtnExtraUpdateRestart_ShowCheck;
        BtnExtraDownload.showCheck = BtnExtraDownload_ShowCheck;
        BtnExtraBack.showCheck = BtnExtraBack_ShowCheck;
        BtnExtraApril.showCheck = BtnExtraApril_ShowCheck;
        BtnExtraShutdown.showCheck = BtnExtraShutdown_ShowCheck;
        BtnExtraLog.showCheck = BtnExtraLog_ShowCheck;
        BtnExtraApril.ShowRefresh();
        // 初始化尺寸改变
        if (!Config.Preference.LockWindowSize)
            AddResizer();
        else
            RemoveResizer();
        // PLC 彩蛋
        if (RandomUtils.NextInt(1, 1000) == 233)
            ShapeTitleLogo.Data = (Geometry)new GeometryConverter().ConvertFromString(
                "M26,29 v-25 h6 a7,7 180 0 1 0,14 h-6 M83,6.5 a10,11.5 180 1 0 0,18 M48,2.5 v24.5 h13.5");
        // 加载窗口

        ThemeManager.ThemeRefresh();
        ModSetup.ApplyAll();
        Lifecycle.CurrentApplication.Resources["BlurSamplingRate"] = Config.Preference.Blur.SamplingRate * 0.01d;
        Lifecycle.CurrentApplication.Resources["BlurType"] = Config.Preference.Blur.KernelType;
        if (Config.Preference.Blur.IsEnabled)
            Lifecycle.CurrentApplication.Resources["BlurRadius"] = Config.Preference.Blur.Radius * 1.0d;
        else
            Lifecycle.CurrentApplication.Resources["BlurRadius"] = 0.0d;

        // #If DEBUG Then
        // MinHeight = 50
        // MinWidth = 50
        // #End If
        Topmost = false;
        if (ModMain.frmStart is not null)
            ModMain.frmStart.Close(new TimeSpan(0, 0, 0, 0, (int)Math.Round(400d / ModAnimation.aniSpeed)));
        // 更改窗口
        // Top = (GetWPFSize(My.Computer.Screen.WorkingArea.Height) - Height) / 2
        // Left = (GetWPFSize(My.Computer.Screen.WorkingArea.Width) - Width) / 2
        isSizeSaveable = true;
        ShowWindowToTop();
        var hwndSource = (HwndSource)PresentationSource.FromVisual(this);
        hwndSource.AddHook(WndProc);
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaCode(() => ModAnimation.AniControlEnabled -= 1, 50),
            ModAnimation.AaOpacity(this, Config.Preference.Theme.WindowOpacity / 1000d + 0.4d, 250, 100),
            ModAnimation.AaDouble(i => TransformPos.Y += (double)i, -TransformPos.Y, 600,
                100, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)),
            ModAnimation.AaDouble(i => TransformRotate.Angle += (double)i,
                -TransformRotate.Angle, 500, 100, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)),
            ModAnimation.AaCode(() =>
            {
                RenderTransform = null;
                isWindowLoadFinished = true;
                ModBase.Log(
                    $"[System] DPI：{ModBase.dpi}，系统版本：{Environment.OSVersion.VersionString}，PCL 位置：{Basics.ExecutablePath}");
            }, after: true)
        }, "Form Show");
        // Timer 启动
        ModAnimation.AniStart();
        ModMain.TimerMainStart();
        // 特殊版本提示
        ModBase.RunInNewThread(() =>
        {
            // 特殊版本提示
            try
            {


#if DEBUG || DEBUGCI

                if (Environment.GetEnvironmentVariable("PCL_DISABLE_DEBUG_HINT") is null)
                {

#if DEBUG
                    var hint = Lang.Text("Main.SpecialVersion.DebugHint");
#else
                    var hint = Lang.Text("Main.SpecialVersion.CiHint");
#endif

                    ModMain.MyMsgBox(
                        $"{hint}{"\r\n"}{"\r\n"}{Lang.Text("Main.SpecialVersion.HideHintNotice")}",
                        Lang.Text("Main.SpecialVersion.Title"), Lang.Text("Main.SpecialVersion.IUnderstand"), Lang.Text("Main.SpecialVersion.OpenDownloadPageAndExit"), isWarn: true, button2Action: () =>
                        {
                            ModBase.OpenWebsite("https://github.com/PCL-Community/PCL2-CE/releases/latest");
                            EndProgram(false);
                        });
                }


#endif
                // EULA 提示
                if (!States.System.LauncherEula)
                    switch (ModMain.MyMsgBox(Lang.Text("Main.Eula.Message"), Lang.Text("Main.Eula.Title"), Lang.Text("Common.Action.Agree"), Lang.Text("Common.Action.Decline"), Lang.Text("Main.Eula.View"),
                                button3Action: () => ModBase.OpenWebsite("https://shimo.im/docs/rGrd8pY8xWkt6ryW")))
                    {
                        case 1:
                            {
                                States.System.LauncherEula = true;
                                break;
                            }
                        case 2:
                            {
                                EndProgram(false);
                                break;
                            }
                    }

                // 遥测提示
                if (Config.System.TelemetryConfig.IsDefault())
                {
                    var selection = ModMain.MyMsgBox(
                                Lang.Text("Main.Telemetry.Message"),
                                Lang.Text("Main.Telemetry.Title"), Lang.Text("Common.Action.Agree"), Lang.Text("Common.Action.Decline"));
                    Config.System.TelemetryConfig.SetValue(selection == 1, forceNewValue: true);
                }
                // 启动加载器池
                try
                {
                    ModDownload.dlClientListMojangLoader.Start(1); // PCL 会同时根据这里的加载结果决定是否使用官方源进行下载
                    RunCountSub();
                    UpdateManager.serverLoader.Start(1);
                    ModBase.RunInNewThread(ModMain.TryClearTaskTemp, "TryClearTaskTemp", ThreadPriority.BelowNormal);
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        "初始化加载池运行失败",
                        ModBase.LogLevel.Feedback,
                        userSummary: Lang.Text("Main.Error.OperationFailed"));
                }

                HardwareInfo.GetHardwareInfo();
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    "初始弹窗提示运行失败",
                    ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Main.Error.OperationFailed"));
            }
        }, "Start Loader", ThreadPriority.BelowNormal);

        ModBase.Log($"[Start] 第三阶段加载用时：{TimeUtils.GetTimeTick() - ModBase.applicationStartTick} ms");
    }

    // 根据打开次数触发的事件
    private void RunCountSub()
    {
        States.System.StartupCount += 1;
    }

    // 升级与降级事件
    private void UpgradeSub(int lastVersionCode)
    {
        ModBase.Log("[Start] 版本号从 " + lastVersionCode + " 升高到 " + ModBase.versionCode);
        States.System.LastVersion = ModBase.versionCode;
        // 检查有记录的最高版本号
        int lowerVersionCode;
#if BETA
        lowerVersionCode = States.System.LastBetaVersion;
        if (lowerVersionCode < ModBase.versionCode)
        {
            States.System.LastBetaVersion = ModBase.versionCode;
            ModBase.Log($"[Start] 最高版本号从 {lowerVersionCode} 升高到 {ModBase.versionCode}");
        }
#else
        lowerVersionCode = States.System.LastAlphaVersion;
        if (lowerVersionCode < ModBase.versionCode)
        {
            States.System.LastAlphaVersion = ModBase.versionCode;
            ModBase.Log($"[Start] 最高版本号从 {lowerVersionCode} 升高到 {ModBase.versionCode}");
        }
#endif
        // 被移除的窗口设置选项 (Commit 3161488 2026/1/23)
        if ((int)Config.Launch.GameWindowMode == 5)
            Config.Launch.GameWindowMode = GameWindowSizeMode.Default;

        // 更新后展示社区版提示
        UpdateManager.ShowCEAnnounce();
        // 输出更新日志
        if (lastVersionCode <= 0)
            return;
        if (lowerVersionCode >= ModBase.versionCode)
            return;
        ShowUpdateLog();
        
        // 重置自定义主页配置
        if (lastVersionCode < 521 && Config.Preference.Homepage.SelectedPreset >= 3)
        {
            Config.Preference.Homepage.SelectedPreset = 0;
            ModMain.MyMsgBox(Lang.Text("Main.HomepageReset.Content"), Lang.Text("Main.HomepageReset.Title"));
        }
    }

    private void DowngradeSub(int lastVersionCode)
    {
        ModBase.Log("[Start] 版本号从 " + lastVersionCode + " 降低到 " + ModBase.versionCode);
        States.System.LastVersion = ModBase.versionCode;
    }

    #endregion

    #region 自定义窗口

    private bool canResize = true;

    // 重写窗口边缘判定以使 DWM 自带的 resizer 行为看起来比较正常
    private nint _SizeWndProc(nint hWnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // 窗口活动常量
        const int WM_NCHITTEST = 0x84;
        const int HTCLIENT = 1;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;

        // WPF 尺寸的 offset
        const int offsetWpf = 6;
        const int hitWidthWpf = 5;

        // 过滤非 WM_NCHITTEST 事件
        if (msg != WM_NCHITTEST)
            return nint.Zero;

        // 提取鼠标坐标
        // 没妈的 VB 强转还得检查一下幻想的妈是不是还活着
        var mouseBytes = BitConverter.GetBytes(lParam.ToInt64());
        var xMouse = BitConverter.ToInt16(mouseBytes, 0);
        var yMouse = BitConverter.ToInt16(mouseBytes, 2);

        // 获取窗口参数
        var windowRect = WindowInterop.GetWindowRectangle(hWnd);
        var windowBounds = windowRect.ToWindowBounds();

        // 判断鼠标是否在窗口范围内
        var isInWindow = xMouse >= windowRect.Left && xMouse <= windowRect.Right && yMouse >= windowRect.Top &&
                         yMouse <= windowRect.Bottom;

        // 过滤不在窗口内的请求
        if (!isInWindow)
            return nint.Zero;

        // 如果 CanResize 为 False，直接返回 HTCLIENT
        if (!canResize)
            return new nint(HTCLIENT);

        // 真实像素尺寸的 offset
        var dpi = VisualTreeHelper.GetDpi(this);
        var offsetPxX = offsetWpf * dpi.DpiScaleX;
        var offsetPxY = offsetWpf * dpi.DpiScaleY;
        var hitWidthPxX = hitWidthWpf * dpi.DpiScaleX;
        var hitWidthPxY = hitWidthWpf * dpi.DpiScaleY;

        // 计算鼠标相对于窗口左上角的物理像素位置
        var relX = xMouse - windowRect.Left;
        var relY = yMouse - windowRect.Top;
        var w = windowBounds.Width;
        var h = windowBounds.Height;

        // 判定是否命中偏移后的热区
        var inLeft = relX >= offsetPxX && relX <= offsetPxX + hitWidthPxX;
        var inRight = relX <= w - offsetPxX && relX >= w - offsetPxX - hitWidthPxX;
        var inTop = relY >= offsetPxY && relY <= offsetPxY + hitWidthPxY;
        var inBottom = relY <= h - offsetPxY && relY >= h - offsetPxY - hitWidthPxY;

        handled = true; // 接管该区域的消息

        // 返回结果
        if (inTop && inLeft)
            return new nint(HTTOPLEFT);
        if (inTop && inRight)
            return new nint(HTTOPRIGHT);
        if (inBottom && inLeft)
            return new nint(HTBOTTOMLEFT);
        if (inBottom && inRight)
            return new nint(HTBOTTOMRIGHT);
        if (inLeft)
            return new nint(HTLEFT);
        if (inRight)
            return new nint(HTRIGHT);
        if (inTop)
            return new nint(HTTOP);
        if (inBottom)
            return new nint(HTBOTTOM);

        // 如果在 0-offset 范围内，返回 HTCLIENT 杀掉默认缩放
        return new nint(HTCLIENT);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        // 硬件加速
        if (Config.System.DisableHardwareAcceleration)
        {
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (hwndSource is not null) hwndSource.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
        }

        base.OnSourceInitialized(e);

        // 获取当前窗口句柄
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        if (source is not null)
        {
            // 渲染层允许 Alpha 通道通过
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            // 魔改窗口边缘判定
            source.AddHook(_SizeWndProc);
        }

        // 设置 DWM 窗口框架
        try
        {
            WindowInterop.ExtendFrameIntoClientArea(hwnd, -1);
        }
        catch (Exception ex)
        {
            LogWrapper.Error("DWM 窗口框架应用失败: " + ex.Message);
        }
    }

    // 关闭
    private void FormMain_Closing(object sender, CancelEventArgs e)
    {
        EndProgram(true);
        e.Cancel = true;
    }

    /// <summary>
    ///     正常关闭程序。程序将在执行此方法后约 0.3s 退出。
    /// </summary>
    /// <param name="sendWarning">是否在还有下载任务未完成时发出警告。</param>
    /// <param name="isUpdating">是否正在更新重启</param>
    public void EndProgram(bool sendWarning, bool isUpdating = false)
    {
        // 发出警告
        if (sendWarning && ModNet.HasDownloadingTask())
        {
            if (ModMain.MyMsgBox(Lang.Text("Main.Exit.HasDownloadingTask"), Lang.Text("Common.Dialog.Title"), Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Cancel")) == 1)
                // 强行结束下载任务
                ModBase.RunInNewThread(() =>
                {
                    ModBase.Log("[System] 正在强行停止任务");
                    foreach (var Task in ModLoader.loaderTaskbar.ToList())
                        Task.Abort();
                }, "强行停止下载任务");
            else
                return;
        }

        // 关闭联机大厅
        // Await LobbyController.CloseAsync().ConfigureAwait(False)
        // 关闭
        ModBase.RunInUiWait(() =>
        {
            // 清理视频背景
            VideoBack.Stop();
            VideoBack.Source = null;
            VideoBack.Close();
            IsHitTestVisible = false;
            if (RenderTransform is null)
            {
                var transformPos = new TranslateTransform(0d, 0d);
                var transformRotate = new RotateTransform(0d);
                var transformScale = new ScaleTransform(1d, 1d);
                transformScale.CenterX = Width / 2d;
                transformScale.CenterY = Height / 2d;
                RenderTransform = new TransformGroup
                    { Children = new TransformCollection([transformRotate, transformPos, transformScale]) };
                ModAnimation.AniStart(new[]
                {
                    ModAnimation.AaOpacity(this, -Opacity, 140, 40,
                        new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)),
                    ModAnimation.AaDouble(i =>
                    {
                        transformScale.ScaleX += (double)i;
                        transformScale.ScaleY += (double)i;
                    }, 0.88d - transformScale.ScaleX, 180),
                    ModAnimation.AaDouble(i => transformPos.Y += (double)i,
                        20d - transformPos.Y, 180, 0,
                        new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)),
                    ModAnimation.AaDouble(i => transformRotate.Angle += (double)i,
                        0.6d - transformRotate.Angle, 180, 0,
                        new ModAnimation.AniEaseInoutFluent(ModAnimation.AniEasePower.Weak)),
                    ModAnimation.AaCode(() =>
                    {
                        IsHitTestVisible = false;
                        Visibility = Visibility.Collapsed;
                        ShowInTaskbar = false;
                    }, 210),
                    ModAnimation.AaCode(() => EndProgramForce(force: false, isUpdating: isUpdating), 230)
                }, "Form Close");
            }
            else
            {
                EndProgramForce(force: false, isUpdating: isUpdating);
            }

            ModBase.Log("[System] 收到关闭指令");
        });
    }

    private static bool isLogShown;

    public static void EndProgramForce(ModBase.ProcessReturnValues returnCode = ModBase.ProcessReturnValues.Success,
        bool force = true, bool isUpdating = false)
    {
        // On Error Resume Next
        // 关闭联机大厅
        // Await LobbyController.CloseAsync().ConfigureAwait(False)
        ModBase.isProgramEnded = true;
        ModAnimation.AniControlEnabled += 1;
        if (UpdateManager.isUpdateWaitingRestart && !isUpdating)
            UpdateManager.UpdateRestart(false, false);
        if (returnCode == ModBase.ProcessReturnValues.Exception)
        {
            if (!isLogShown)
            {
                ModBase.FeedbackInfo();
                ModBase.Log("请在 https://github.com/PCL-Community/PCL2-CE/issues 提交错误报告，以便于社区解决此问题！（这也有可能是原版 PCL 的问题）");
                isLogShown = true;
                ModBase.ShellOnly(LogWrapper.CurrentLogger.CurrentLogFiles.Last());
            }

            Thread.Sleep(500); // 防止 PCL 在记事本打开前就被掐掉
        }

        ModBase.Log("[System] 程序已退出，返回值：" + ModBase.GetStringFromEnum(returnCode));
        // If ReturnCode <> ProcessReturnValues.Success Then Environment.Exit(ReturnCode)
        // Process.GetCurrentProcess.Kill()
        Lifecycle.Shutdown((int)returnCode, force);
    }

    private void BtnTitleClose_Click(object sender, EventArgs e)
    {
        EndProgram(true);
    }

    // 移动
    private void FormDragMove(object sender, MouseButtonEventArgs e)
    {
        // On Error Resume Next
        if (((Grid)sender).IsMouseDirectlyOver)
            DragMove();
    }

    // 改变大小
    /// <summary>
    ///     是否可以向注册表储存尺寸改变信息。以此避免初始化时误储存。
    /// </summary>
    public bool isSizeSaveable;

    private void FormMain_SizeChanged(object? sender = null, EventArgs? e = null)
    {
        if (isSizeSaveable)
        {
            States.UI.WindowHeight = Height;
            States.UI.WindowWidth = Width;
        }

        if (PanBack is not null)
        {
            RectForm.Rect = new Rect(0d, 0d, PanBack.ActualWidth, PanBack.ActualHeight);

            var formWidth = PanBack.ActualWidth + 0.001d;
            var formHeight = PanBack.ActualHeight + 0.001d;

            PanForm.Width = formWidth;
            PanForm.Height = formHeight;
            PanMain.Width = formWidth;

            if (PanTitle is not null)
                PanMain.Height = Math.Max(0d, formHeight - PanTitle.ActualHeight);
            else
                PanMain.Height = formHeight;

            VideoBack.Width = formWidth;
            VideoBack.Height = formHeight;
        }

        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal; // 修复 #1938
    }

    // 标题栏改变大小
    private void PanTitle_SizeChanged(object sender, EventArgs e)
    {
        if (PanTitleMain.ColumnDefinitions[0].ActualWidth - 30 <= 0)
            PanTitleLeft.ColumnDefinitions[0].MaxWidth = 0;
        else
            PanTitleLeft.ColumnDefinitions[0].MaxWidth = PanTitleMain.ColumnDefinitions[0].ActualWidth - 30;
    }

    // 最小化
    private void BtnTitleMin_Click(object sender, EventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnTitleHelp_Click(object sender, EventArgs e)
    {
        ModBase.OpenWebsite("https://www.bilibili.com/video/BV1uT4y1P7CX");
    }

    #endregion

    #region 窗体事件

    public void AddResizer()
    {
        canResize = true;
    }

    public void RemoveResizer()
    {
        canResize = false;
    }

    // 按键事件
    private void FormMain_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat)
            return;
        // 调用弹窗：回车选择第一个，Esc 选择最后一个
        if (PanMsg.Children.Count > 0)
        {
            if (e.Key == Key.Enter)
            {
                var msg = PanMsg.Children[0];
                Action? enterAction = msg switch
                {
                    MyMsgInput input => () => input.Btn1_Click(sender, null),
                    MyMsgSelect select => () => select.Btn1_Click(sender, null),
                    MyMsgText text => () => text.Btn1_Click(sender, null),
                    MyMsgMarkdown markdown => () => markdown.Btn1_Click(sender, null),
                    MyMsgLogin login => () => login.Btn1_Click(sender, null),
                    _ => null
                };
                enterAction?.Invoke();
                return;
            }

            if (e.Key == Key.Escape)
            {
                var msg = PanMsg.Children[0];
                Action? escapeAction = msg switch
                {
                    MyMsgInput input => input.Btn2.Visibility == Visibility.Visible
                        ? () => input.Btn2_Click(sender, null)
                        : () => input.Btn1_Click(sender, null),
                    MyMsgSelect select => select.Btn2.Visibility == Visibility.Visible
                        ? () => select.Btn2_Click(sender, null)
                        : () => select.Btn1_Click(sender, null),
                    MyMsgText text => text.Btn3.Visibility == Visibility.Visible
                        ? () => text.Btn3_Click(sender, null)
                        : text.Btn2.Visibility == Visibility.Visible
                            ? () => text.Btn2_Click(sender, null)
                            : () => text.Btn1_Click(sender, null),
                    MyMsgMarkdown markdown => markdown.Btn3.Visibility == Visibility.Visible
                        ? () => markdown.Btn3_Click(sender, null)
                        : markdown.Btn2.Visibility == Visibility.Visible
                            ? () => markdown.Btn2_Click(sender, null)
                            : () => markdown.Btn1_Click(sender, null),
                    MyMsgLogin login => login.Btn3.Visibility == Visibility.Visible
                        ? () => login.Btn3_Click(sender, null)
                        : () => login.Btn1_Click(sender, null),
                    _ => null
                };
                escapeAction?.Invoke();
                return;
            }
        }

        // 按 ESC 返回上一级
        if (e.Key == Key.Escape)
            TriggerPageBack();
        // 更改隐藏实例可见性
        if (e.Key == Key.F11 && pageCurrent == PageType.InstanceSelect)
        {
            ModMain.frmSelectRight.showHidden = !ModMain.frmSelectRight.showHidden;
            ModLoader.LoaderFolderRun(ModInstanceList.mcInstanceListLoader, ModFolder.mcFolderSelected,
                ModLoader.LoaderFolderRunType.ForceRun, 1, @"versions\");
            return;
        }

        // 更改功能隐藏可见性
        if (e.Key == Key.F12)
        {
            PageSetupUI.HiddenForceShow = !PageSetupUI.HiddenForceShow;
            if (PageSetupUI.HiddenForceShow)
                HintService.Hint(Lang.Text("Main.HiddenFeature.Disabled"), HintType.Success);
            else
                HintService.Hint(Lang.Text("Main.HiddenFeature.Enabled"), HintType.Success);
            PageSetupUI.HiddenRefresh();
            return;
        }

        // 按 F5 刷新页面
        if (e.Key == Key.F5)
        {
            if (pageLeft is IRefreshable)
                ((IRefreshable)pageLeft).Refresh();
            if (pageRight is IRefreshable)
                ((IRefreshable)pageRight).Refresh();
            return;
        }

        // 调用启动游戏
        if (e.Key == Key.Enter && pageCurrent == PageType.Launch)
        {
            if (ModMain.isAprilEnabled && !ModMain.isAprilGiveup)
                HintService.Hint(Lang.Text("Main.April.Nope"));
            else
                ModMain.frmLaunchLeft.LaunchButtonClick();
        }

        // 修复按下 Alt 后误认为弹出系统菜单导致的冻结
        if (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt)
            e.Handled = true;
    }

    private void FormMain_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // 鼠标侧键返回上一级
        if (ModMain.frmMain!.PanMsg.Children.Count > 0 || ModMain.WaitingMyMsgBox.Any())
            return; // 弹窗中（#5513）
        if (e.ChangedButton == MouseButton.XButton1 || e.ChangedButton == MouseButton.XButton2)
            TriggerPageBack();
    }

    private void TriggerPageBack()
    {
        if (pageCurrent == PageType.Download && PageCurrentSub == PageSubType.DownloadInstall &&
            ModMain.frmDownloadInstall.isInSelectPage)
            ModMain.frmDownloadInstall.ExitSelectPage();
        else if (pageCurrent == PageType.InstanceSetup && PageCurrentSub == PageSubType.VersionInstall &&
                 ModMain.frmInstanceInstall.isInSelectPage)
            ModMain.frmInstanceInstall.ExitSelectPage();
        else
            PageBack();
    }

    // 切回窗口
    private void FormMain_Activated(object sender, EventArgs e)
    {
        try
        {
            if (Config.Download.Comp.ReadClipboard)
                ModComp.CompClipboard.GetClipboardResource();
            if (pageCurrent == PageType.InstanceSetup && PageCurrentSub == PageSubType.VersionMod)
            {
                // Mod 管理自动刷新
                ModMain.frmInstanceMod.ReloadCompFileList();
            }
            else if (pageCurrent == PageType.InstanceSetup && PageCurrentSub == PageSubType.VersionResourcePack)
            {
                // 资源包管理自动刷新
                if (ModMain.frmInstanceResourcePack is not null)
                    ModMain.frmInstanceResourcePack.ReloadCompFileList();
            }
            else if (pageCurrent == PageType.InstanceSetup && PageCurrentSub == PageSubType.VersionShader)
            {
                // 光影包管理自动刷新
                if (ModMain.frmInstanceShader is not null)
                    ModMain.frmInstanceShader.ReloadCompFileList();
            }
            else if (pageCurrent == PageType.InstanceSetup && PageCurrentSub == PageSubType.VersionSchematic)
            {
                // 投影原理图管理自动刷新
                if (ModMain.frmInstanceSchematic is not null)
                    ModMain.frmInstanceSchematic.ReloadCompFileList();
            }
            else if (pageCurrent == PageType.InstanceSelect)
            {
                // 实例选择自动刷新
                ModLoader.LoaderFolderRun(ModInstanceList.mcInstanceListLoader, ModFolder.mcFolderSelected,
                    ModLoader.LoaderFolderRunType.RunOnUpdated, 1, @"versions\");
            }
            else if (ModMain.frmMain.pageRight is PageInstanceSavesDatapack &&
                     ModMain.frmInstanceSavesDatapack is not null)
            {
                // 数据包管理自动刷新
                ModMain.frmInstanceSavesDatapack.ReloadDatapackFileList();
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "切回窗口时出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Main.Error.OperationFailed"));
        }
    }

    private IDataObject _HandleDrag_PrevData;
    private DragDropEffects _HandleDrag_PrevEffects;

    // 文件拖放
    private void HandleDrag(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Handled && e.Effects != DragDropEffects.None)
                return;
            // 缓存
            e.Handled = true;
            if (ReferenceEquals(e.Data, _HandleDrag_PrevData))
            {
                e.Effects = _HandleDrag_PrevEffects;
                return;
            }

            // 确定拖放效果
            e.Effects = DragDropEffects.None;
            if (e.Data.GetDataPresent(DataFormats.Text))
            {
                var str = (string)e.Data.GetData(DataFormats.Text);
                if (str.StartsWithF("authlib-injector:yggdrasil-server:"))
                    e.Effects = DragDropEffects.Copy;
                else if (str.StartsWithF("file:///")) e.Effects = DragDropEffects.Copy;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files is not null && files.Length > 0) e.Effects = DragDropEffects.Link;
            }

            _HandleDrag_PrevData = e.Data;
            _HandleDrag_PrevEffects = e.Effects;
            ModBase.Log("[System] 设置拖放类型：" + ModBase.GetStringFromEnum(e.Effects));
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "处理拖放时出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Main.Error.OperationFailed"));
        }
    }

    private void FrmMain_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetDataPresent(DataFormats.Text))
            {
                // 获取文本
                try
                {
                    var str = (string)e.Data.GetData(DataFormats.Text);
                    ModBase.Log("[System] 接受文本拖拽：" + str);
                    if (str.StartsWithF("authlib-injector:yggdrasil-server:"))
                    {
                        // Authlib 拖拽
                        e.Handled = true;
                        e.Effects = DragDropEffects.Copy;
                        var authlibServer =
                            WebUtility.UrlDecode(str.Substring("authlib-injector:yggdrasil-server:".Length));
                        ModBase.Log("[System] Authlib 拖拽：" + authlibServer);
                        if (!new HttpValidator().Validate(authlibServer).IsValid)
                        {
                            HintService.Hint(Lang.Text("Main.FileDrag.AuthlibInvalid", authlibServer), HintType.Error);
                            return;
                        }

                        if (ModMain.MyMsgBox(Lang.Text("Main.FileDrag.CreateAuthlibProfile", authlibServer), Lang.Text("Main.FileDrag.CreateAuthlibProfileTitle"),
                                Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Cancel")) == 2)
                            return;
                        ModBase.RunInUi(() =>
                        {
                            PageLoginAuth.draggedAuthServer = authlibServer;
                            ModMain.frmLaunchLeft.RefreshPage(true, ModLaunch.McLoginType.Auth);
                        });
                        if (pageCurrent == PageType.InstanceSetup && PageCurrentSub == PageSubType.VersionSetup)
                            // 正在服务器选项页，需要刷新设置项显示
                            ModMain.frmInstanceSetup.Reload();
                    }
                    else if (str.StartsWithF("file:///"))
                    {
                        // 文件拖拽（例如从浏览器下载窗口拖入）
                        var filePath = WebUtility.UrlDecode(str).Substring("file:///".Length).Replace("/", @"\");
                        e.Handled = true;
                        e.Effects = DragDropEffects.Copy;
                        FileDrag(new List<string> { filePath });
                    }
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "无法接取文本拖拽事件", ModBase.LogLevel.Developer);
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // 获取文件并检查
                var filePathRaw = e.Data.GetData(DataFormats.FileDrop);
                if (filePathRaw is null) // #2690
                {
                    HintService.Hint(Lang.Text("Main.FileDrag.ExtractFirst"), HintType.Error);
                    return;
                }

                e.Handled = true;
                e.Effects = DragDropEffects.Link;
                FileDrag((IEnumerable<string>)filePathRaw);
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "接取拖拽事件失败",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Main.Error.OperationFailed"));
        }
    }

    private void FileDrag(IEnumerable<string> filePathList)
    {
        ModBase.RunInNewThread(() =>
        {
            var filePath = filePathList.First();
            ModBase.Log("[System] 接受文件拖拽：" + filePath + (filePathList.Any() ? $" 等 {filePathList.Count()} 个文件" : ""),
                ModBase.LogLevel.Developer);
            // 基础检查
            if (Directory.Exists(filePathList.First()) && !File.Exists(filePathList.First()))
            {
                HintService.Hint(Lang.Text("Main.FileDrag.FileOnly"), HintType.Error);
                return;
            }

            if (!File.Exists(filePathList.First()))
            {
                HintService.Hint(Lang.Text("Main.FileDrag.FileNotFound", filePathList.First()), HintType.Error);
                return;
            }

            // 多文件拖拽
            if (filePathList.Count() > 1)
            {
                // 检查是否为同类型文件
                var firstExtension = filePathList.First().AfterLast(".").ToLower();
                var allSameType = filePathList.All(f => (f.AfterLast(".").ToLower() ?? "") == (firstExtension ?? ""));

                if (allSameType &&
                    new[] { "jar", "litemod", "disabled", "old", "litematic", "nbt", "schematic", "schem" }.Contains(
                        firstExtension))
                {
                }
                // 允许同类型的 Mod 文件或投影文件批量拖拽
                else
                {
                    HintService.Hint(Lang.Text("Main.FileDrag.SameTypeOnly"), HintType.Error);
                    return;
                }
            }

            // 主页
            var extension = filePath.AfterLast(".").ToLower();
            if (extension == "xaml")
            {
                ModBase.Log("[System] 文件后缀为 XAML，作为主页加载");
                if (File.Exists(ModBase.exePath + @"PCL\Custom.xaml"))
                    if (ModMain.MyMsgBox(Lang.Text("Main.FileDrag.HomepageExists"), Lang.Text("Main.FileDrag.OverwriteTitle"), Lang.Text("Common.Action.Overwrite"), Lang.Text("Common.Action.Cancel")) == 2)
                        return;

                ModBase.CopyFile(filePath, ModBase.exePath + @"PCL\Custom.xaml");
                ModBase.RunInUi(() =>
                {
                    Config.Preference.Homepage.Type = 1;
                    ModMain.frmLaunchRight.ForceRefresh();
                    HintService.Hint(Lang.Text("Main.FileDrag.HomepageLoaded"), HintType.Success);
                });
                return;
            }

            // 安装 Mod
            if (PageInstanceCompResource.InstallMods(filePathList))
                return;
            // 安装投影文件
            if (new[] { "litematic", "nbt", "schematic", "schem" }.Contains(extension))
            {
                ModBase.Log($"[System] 文件为 {extension} 格式，尝试作为原理图安装");
                // 获取当前文件夹路径（如果在资源管理页面）
                string targetFolderPath = null;
                if (pageCurrent == PageType.InstanceSetup && PageCurrentSub == PageSubType.VersionSchematic &&
                    ModMain.frmInstanceSchematic is not null &&
                    ModMain.frmInstanceSchematic is PageInstanceCompResource)
                    targetFolderPath = ModMain.frmInstanceSchematic.CurrentFolderPath;
                PageInstanceCompResource.InstallCompFiles(filePathList, ModComp.CompType.Schematic, targetFolderPath);
                return;
            }

            // 处理资源安装
            if (pageCurrent == PageType.InstanceSetup && new[] { "zip" }.Any(i => (i ?? "") == (extension ?? "")))
                switch (PageCurrentSub)
                {
                    case PageSubType.VersionWorld:
                    {
                        var destFolder = PageInstanceLeft.McInstance.PathIndie + @"saves\" +
                                         ModBase.GetFileNameWithoutExtentionFromPath(filePath);
                        var destLevelDat = Path.Combine(destFolder, "level.dat");
                        if (Directory.Exists(destFolder))
                        {
                            HintService.Hint(Lang.Text("Main.FileDrag.SameFolderExists", destFolder), HintType.Error);
                            return;
                        }

                        var extractFolder = Path.Combine(ModBase.pathTemp, "Cache", "WorldImport", ModBase.GetUuid().ToString());
                        try
                        {
                            ModBase.ExtractFile(filePath, extractFolder);
                            var saveRoot = SaveImportHelper.GetSaveRootDirectory(extractFolder);
                            if (saveRoot is null)
                            {
                                HintService.Hint(Lang.Text("Main.FileDrag.SaveNotFound"), HintType.Error);
                                return;
                            }

                            ModBase.CopyDirectory(saveRoot, destFolder);
                            if (!File.Exists(destLevelDat))
                            {
                                if (Directory.Exists(destFolder))
                                    ModBase.DeleteDirectory(destFolder, true);
                                HintService.Hint(Lang.Text("Main.FileDrag.SaveInvalid"), HintType.Error);
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (Directory.Exists(destFolder))
                                ModBase.DeleteDirectory(destFolder, true);
                            ModBase.Log(
                                ex,
                                Lang.Text("Main.FileDrag.SaveImportFailed"),
                                ModBase.LogLevel.Hint,
                                userSummary: Lang.Text("Main.FileDrag.SaveImportFailed"));
                            return;
                        }
                        finally
                        {
                            if (Directory.Exists(extractFolder))
                                ModBase.DeleteDirectory(extractFolder, true);
                        }

                        HintService.Hint(Lang.Text("Main.FileDrag.Imported", ModBase.GetFileNameWithoutExtentionFromPath(filePath)),
                            HintType.Success);
                        if (ModMain.frmInstanceSaves is not null)
                            ModBase.RunInUi(() => ModMain.frmInstanceSaves.Reload());
                        return;
                    }
                    case PageSubType.VersionResourcePack:
                    {
                        var destFile = PageInstanceLeft.McInstance.PathIndie + @"resourcepacks\" +
                                       ModBase.GetFileNameFromPath(filePath);
                        if (File.Exists(destFile))
                        {
                            HintService.Hint(Lang.Text("Main.FileDrag.SameFileExists", destFile), HintType.Error);
                            return;
                        }

                        ModBase.CopyFile(filePath, destFile);
                        HintService.Hint(Lang.Text("Main.FileDrag.Imported", ModBase.GetFileNameFromPath(filePath)), HintType.Success);
                        if (ModMain.frmInstanceResourcePack is not null)
                            ModBase.RunInUi(() => ModMain.frmInstanceResourcePack.ReloadCompFileList());
                        return;
                    }
                    case PageSubType.VersionShader:
                    {
                        var destFile = PageInstanceLeft.McInstance.PathIndie + @"shaderpacks\" +
                                       ModBase.GetFileNameFromPath(filePath);
                        if (File.Exists(destFile))
                        {
                            HintService.Hint(Lang.Text("Main.FileDrag.SameFileExists", destFile), HintType.Error);
                            return;
                        }

                        ModBase.CopyFile(filePath, destFile);
                        HintService.Hint(Lang.Text("Main.FileDrag.Imported", ModBase.GetFileNameFromPath(filePath)), HintType.Success);
                        if (ModMain.frmInstanceShader is not null)
                            ModBase.RunInUi(() => ModMain.frmInstanceShader.ReloadCompFileList());
                        return;
                    }
                }

            // 处理投影文件
            if (pageCurrent == PageType.InstanceSetup &&
                new[] { "litematic", "nbt", "schematic", "schem" }.Contains(extension) &&
                PageCurrentSub == PageSubType.VersionSchematic)
            {
                var destFile = PageInstanceLeft.McInstance.PathIndie + @"schematics\" +
                               ModBase.GetFileNameFromPath(filePath);
                if (File.Exists(destFile))
                {
                    HintService.Hint(Lang.Text("Main.FileDrag.SameFileExists", destFile), HintType.Error);
                    return;
                }

                Directory.CreateDirectory(PageInstanceLeft.McInstance.PathIndie + @"schematics\");
                ModBase.CopyFile(filePath, destFile);
                HintService.Hint(Lang.Text("Main.FileDrag.Imported", ModBase.GetFileNameFromPath(filePath)), HintType.Success);
                if (ModMain.frmInstanceSchematic is not null)
                    ModBase.RunInUi(() => ModMain.frmInstanceSchematic.ReloadCompFileList());
                return;
            }

            // 安装整合包
            if (new[] { "zip", "rar", "mrpack" }.Any(t =>
                    (t ?? "") == (extension ?? ""))) // 部分压缩包是 zip 格式但后缀为 rar，总之试一试
            {
                ModBase.Log("[System] 文件为压缩包，尝试作为整合包安装");
                try
                {
                    ModModpack.ModpackInstall(filePath);
                    return;
                }
                catch (ModBase.CancelledException ex)
                {
                    return; // 用户主动取消
                }
                catch (Exception ex)
                {
                    // 安装失败，继续往后尝试
                }
            }

            // 错误报告分析
            do
            {
                try
                {
                    ModBase.Log("[System] 尝试进行错误报告分析");
                    var analyzer = new CrashAnalyzer(ModBase.GetUuid());
                    analyzer.Import(filePath);
                    if (!analyzer.Prepare())
                        break;
                    analyzer.Analyze();
                    analyzer.Output(true, new List<string>());
                    return;
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        "自主错误报告分析失败",
                        ModBase.LogLevel.Feedback,
                        userSummary: Lang.Text("Main.Error.OperationFailed"));
                }
            } while (false);

            // 未知操作
            HintService.Hint(Lang.Text("Main.FileDrag.UnknownOperation"));
        }, "文件拖拽");
    }

    // 接受到 Windows 窗体事件
    public bool isSystemTimeChanged;

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 30)
        {
            var nowDate = DateTime.Now;
            if (nowDate.Date == ModBase.applicationOpenTime.Date)
            {
                ModBase.Log("[System] 系统时间微调为：" + nowDate.ToLongDateString() + " " + nowDate.ToLongTimeString());
                isSystemTimeChanged = false;
            }
            else
            {
                ModBase.Log("[System] 系统时间修改为：" + nowDate.ToLongDateString() + " " + nowDate.ToLongTimeString());
                isSystemTimeChanged = true;
            }
        }
        else if (msg == 400 * 16 + 2)
        {
            ModBase.Log("[System] 收到置顶信息：" + hwnd.ToInt64());
            if (!isWindowLoadFinished)
            {
                ModBase.Log("[System] 窗口尚未加载完成，忽略置顶请求");
                return nint.Zero;
            }

            ShowWindowToTop();
            handled = true;
        }
        else if (msg == 26) // WM_SETTINGCHANGE
        {
            if (Marshal.PtrToStringAuto(lParam) == "ImmersiveColorSet")
            {
                ModBase.Log($"[System] 系统主题更改，深色模式：{SystemTheme.IsSystemInDarkMode()}");
                if (Config.Preference.Theme.ColorMode == ColorMode.System &&
                    (ThemeManager.IsDarkMode != SystemTheme.IsSystemInDarkMode())) ThemeService.RefreshColorMode();
            }
        }

        return nint.Zero;
    }

    // 窗口隐藏与置顶
    public bool Hidden
    {
        get => field;
        set
        {
            if (field == value)
                return;
            field = value;
            if (value)
            {
                // 隐藏
                Left -= 10000d;
                ShowInTaskbar = false;
                Visibility = Visibility.Hidden;
                ModBase.Log("[System] 窗口已隐藏，位置：(" + Left + "," + Top + ")");
            }
            else
            {
                // 取消隐藏
                if (Left < -2000)
                    Left += 10000d;
                ShowWindowToTop();
            }
        }
    }

    // 解决龙猫的非通用实现史山
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (Hidden)
            Hidden = false;
    }

    /// <summary>
    ///     把当前窗口拖到最前面。
    /// </summary>
    public void ShowWindowToTop()
    {
        ModBase.RunInUi(() =>
        {
            // 这一坨乱七八糟的，别改，改了指不定就炸了，自己电脑还复现不出来
            Visibility = Visibility.Visible;
            ShowInTaskbar = true;
            WindowState = WindowState.Normal;
            Hidden = false;
            Topmost = true; // 偶尔 SetForegroundWindow 失效
            Topmost = false;
            ModMain.SetForegroundWindow(ModBase.frmHandle);
            Focus();
            ModBase.Log($"[System] 窗口已置顶，位置：({Left}, {Top}), {Width} x {Height}");
        });
    }

    // 背景视频循环播放
    private void VideoEnded(object sender, RoutedEventArgs e)
    {
        VideoBack.Position = TimeSpan.Zero;
        VideoBack.Play();
    }

    // 最小化时暂停背景视频
    private void WindowStateChanged(object sender, EventArgs e)
    {
        switch (WindowState)
        {
            case WindowState.Minimized:
            {
                ModVideoBack.isMinimized = true;
                ModVideoBack.VideoPause();
                break;
            }
            case WindowState.Normal:
            {
                ModVideoBack.isMinimized = false;
                ModVideoBack.VideoPlay();
                break;
            }
        }
    }

    #endregion

    #region 切换页面

    // 页面种类与属性
    // 注意，这一枚举在 “切换页面” EventType 中调用，应视作公开 API 的一部分
    /// <summary>
    ///     页面种类。
    /// </summary>
    public enum PageType
    {
        /// <summary>
        ///     启动。
        /// </summary>
        Launch = 0,

        /// <summary>
        ///     下载。
        /// </summary>
        Download = 1,

        /// <summary>
        ///     联机。
        /// </summary>
        Tools = 3,

        /// <summary>
        ///     设置。
        /// </summary>
        Setup = 2,

        /// <summary>
        ///     实例选择。这是一个副页面。
        /// </summary>
        InstanceSelect = 5,

        /// <summary>
        ///     任务管理。这是一个副页面。
        /// </summary>
        TaskManager = 6,

        /// <summary>
        ///     实例设置。这是一个副页面。
        /// </summary>
        InstanceSetup = 7,

        /// <summary>
        ///     资源工程详情。这是一个副页面。
        /// </summary>
        CompDetail = 8,

        /// <summary>
        ///     游戏实时日志。这是一个副页面。
        /// </summary>
        GameLog = 10,

        /// <summary>
        ///     存档详细管理，这是一个副页面。
        /// </summary>
        VersionSaves = 12,
    }

    /// <summary>
    ///     次要页面种类。其数值必须与 StackPanel 中的下标一致。
    /// </summary>
    public enum PageSubType
    {
        Default = 0,
        DownloadInstall = 1,
        DownloadMod = 2,
        DownloadPack = 3,
        DownloadDataPack = 4,
        DownloadResourcePack = 5,
        DownloadShader = 6,
        DownloadWorld = 7,
        DownloadCompFavorites = 8,
        DownloadClient = 9,
        DownloadOptiFine = 10,
        DownloadForge = 11,
        DownloadNeoForge = 12,
        DownloadCleanroom = 13,
        DownloadFabric = 14,
        DownloadLiteLoader = 16,
        DownloadLabyMod = 17,
        DownloadLegacyFabric = 18,

        SetupLaunch = 0,
        SetupUI = 1,
        SetupGameManage = 2,
        SetupLink = 3,
        SetupAbout = 4,
        SetupLog = 5,
        SetupFeedback = 6,
        SetupGameLink = 7,
        SetupUpdate = 8,
        SetupJava = 9,
        SetupLauncherMisc = 10,
        SetupLauncherLanguage = 11,

        ToolsGameLink = 1,
        ToolsTest = 3,

        VersionOverall = 0,
        VersionSetup = 1,
        VersionExport = 2,
        VersionWorld = 3,
        VersionScreenshot = 4,
        VersionMod = 5,
        VersionModDisabled = 6,
        VersionResourcePack = 7,
        VersionShader = 8,
        VersionSchematic = 9,
        VersionInstall = 10,
        VersionServer = 11,
        VersionModJarInJar = 12,
        VersionSavesInfo = 0,
        VersionSavesDatapack = 1
    }

    /// <summary>
    ///     获取次级页面的名称。若并非次级页面则返回空字符串，故可以以此判断是否为次级页面。
    /// </summary>
    private string PageNameGet(PageStackData stack)
    {
        switch (stack.page)
        {
            case PageType.InstanceSelect:
            {
                return Lang.Text("Main.Title.InstanceSelect");
            }
            case PageType.TaskManager:
            {
                return Lang.Text("Main.Title.TaskManager");
            }
            case PageType.GameLog:
            {
                return Lang.Text("Main.Title.GameLog");
            }
            case PageType.InstanceSetup:
            {
                return Lang.Text("Main.Title.InstanceSetup", PageInstanceLeft.McInstance is null ? Lang.Text("Common.State.Unknown") : PageInstanceLeft.McInstance.Name);
            }
            case PageType.CompDetail:
            {
                return Lang.Text("Main.Title.ResourceDownload", stack.additional.Value.CompProject.TranslatedName);
            }
            case PageType.VersionSaves:
            {
                return Lang.Text("Main.Title.SaveManagement", ModBase.GetFolderNameFromPath(stack.additional.Value.SavePath));
            }

            default:
            {
                return "";
            }
        }
    }

    /// <summary>
    ///     刷新次级页面的名称。
    /// </summary>
    public void PageNameRefresh(PageStackData type)
    {
        LabTitleInner.Text = PageNameGet(type);
    }

    /// <summary>
    ///     刷新次级页面的名称。
    /// </summary>
    public void PageNameRefresh()
    {
        PageNameRefresh(pageCurrent);
    }

    // 页面状态存储
    /// <summary>
    ///     当前的主页面。
    /// </summary>
    public PageStackData pageCurrent = PageType.Launch;

    /// <summary>
    ///     上一个主页面。
    /// </summary>
    public PageStackData pageLast = PageType.Launch;

    /// <summary>
    ///     当前的子页面。
    /// </summary>
    public PageSubType PageCurrentSub
    {
        get
        {
            switch (pageCurrent.page)
            {
                case PageType.Download:
                {
                    if (ModMain.frmDownloadLeft is null)
                        ModMain.frmDownloadLeft = new PageDownloadLeft();
                    return ModMain.frmDownloadLeft.pageID;
                }

                case PageType.Setup:
                {
                    if (ModMain.frmSetupLeft is null)
                        ModMain.frmSetupLeft = new PageSetupLeft();
                    return ModMain.frmSetupLeft.pageID;
                }

                case PageType.InstanceSetup:
                {
                    if (ModMain.frmInstanceLeft is null)
                        ModMain.frmInstanceLeft = new PageInstanceLeft();
                    return ModMain.frmInstanceLeft.pageID;
                }

                default:
                {
                    return 0; // 没有子页面
                }
            }
        }
    }

    /// <summary>
    ///     上层页面的编号堆栈，用于返回。
    /// </summary>
    public List<PageStackData> pageStack = new();

    public class PageStackData
    {
        /// <summary>
        /// <list type="bullet">
        ///   <item><description>CompDetail: (CompProject, ExpandedTitles, TargetVersion, TargetLoader, ResourceType)</description></item>
        ///   <item><description>VersionSaves: SavePath</description></item>
        /// </list>
        /// </summary>
        public (
            ModComp.CompProject CompProject,
            List<string> ExpandedTitles,
            string TargetVersion,
            ModComp.CompLoaderType TargetLoader,
            ModComp.CompType ResourceType,
            string SavePath
        )? additional;

        public PageType page;

        public override bool Equals(object other)
        {
            if (other is null)
                return false;
            if (other is PageStackData)
            {
                var pageOther = (PageStackData)other;
                if (page != pageOther.page)
                    return false;
                if (additional is null) return pageOther.additional is null;

                return pageOther.additional is not null && additional.Equals(pageOther.additional);
            }

            if (other is int o)
            {
                if ((int)page == o)
                    return false;
                return additional is null;
            }

            return false;
        }

        public static bool operator ==(PageStackData left, PageStackData right)
        {
            return EqualityComparer<PageStackData>.Default.Equals(left, right);
        }

        public static bool operator !=(PageStackData left, PageStackData right)
        {
            return !(left == right);
        }

        public static implicit operator PageStackData(PageType value)
        {
            return new PageStackData { page = value };
        }

        public static implicit operator PageType(PageStackData value)
        {
            return value.page;
        }
    }

    public MyPageLeft pageLeft;
    public MyPageRight pageRight;

    // 引发实际页面切换的入口
    private bool isChangingPage;

    /// <summary>
    ///     切换页面，并引起对应选择 UI 的改变。
    /// </summary>
    public void PageChange(PageStackData stack, PageSubType subType = PageSubType.Default)
    {
        if (string.IsNullOrEmpty(PageNameGet(stack)))
        {
            // 切换到主页面
            PageChangeExit();
            isChangingPage = true; // 防止下面的勾选直接触发了 PageChangeActual
            ((MyRadioButton)PanTitleSelect.Children[(int)stack.page]).SetChecked(true, true,
                string.IsNullOrEmpty(PageNameGet(pageCurrent)));
            isChangingPage = false;
            switch (stack.page)
            {
                case PageType.Download:
                {
                    if (ModMain.frmDownloadLeft is null)
                        ModMain.frmDownloadLeft = new PageDownloadLeft();
                    foreach (var item in ModMain.frmDownloadLeft.PanItem.Children)
                        if (item is MyListItem listItem &&
                            ModBase.Val(listItem.Tag) == (double)subType)
                        {
                            listItem.SetChecked(true, true, stack == pageCurrent);
                            break;
                        }

                    break;
                }
                case PageType.Setup:
                {
                    if (ModMain.frmSetupLeft is null)
                        ModMain.frmSetupLeft = new PageSetupLeft();
                    foreach (var item in ModMain.frmSetupLeft.PanItem.Children)
                        if (item is MyListItem listItem &&
                            ModBase.Val(listItem.Tag) == (double)subType)
                        {
                            listItem.SetChecked(true, true, stack == pageCurrent);
                            break;
                        }

                    break;
                }
            }

            PageChangeActual(stack, subType);
        }
        else
        {
            // 切换到次页面
            switch (stack.page)
            {
                case PageType.InstanceSetup:
                {
                    if (ModMain.frmInstanceLeft is null)
                        ModMain.frmInstanceLeft = new PageInstanceLeft();
                    foreach (var item in ModMain.frmInstanceLeft.PanItem.Children)
                        if (item is MyListItem listItem &&
                            ModBase.Val(listItem.Tag) == (double)subType)
                        {
                            listItem.SetChecked(true, true, stack == pageCurrent);
                            break;
                        }

                    break;
                }
                case PageType.VersionSaves:
                {
                    if (ModMain.frmInstanceSavesLeft is null)
                        ModMain.frmInstanceSavesLeft = new PageInstanceSavesLeft();
                    foreach (var item in ModMain.frmInstanceSavesLeft.PanItem.Children)
                        if (item is MyListItem listItem &&
                            ModBase.Val(listItem.Tag) == (double)subType)
                        {
                            listItem.SetChecked(true, true, stack == pageCurrent);
                            break;
                        }

                    break;
                }
            }

            PageChangeActual(stack, subType);
        }
    }

    /// <summary>
    ///     通过点击导航栏改变页面。
    /// </summary>
    private void BtnTitleSelect_Click(MyRadioButton sender, bool raiseByMouse)
    {
        if (isChangingPage)
            return;
        var pageType = (PageType)int.Parse(sender.Tag.ToString());
        PageChangeActual(pageType, PageSubType.Default);
        }

    private void BtnTitleInner_Click(object sender, EventArgs e)
    {
        PageBack();
    }

    /// <summary>
    ///     通过点击返回按钮或手动触发返回来改变页面。
    /// </summary>
    public void PageBack()
    {
        if (pageStack.Any())
            PageChangeActual(pageStack[0], PageSubType.Default);
        else
            PageChange(PageType.Launch);
    }

    // 实际处理页面切换
    /// <summary>
    ///     切换现有页面的实际方法。
    /// </summary>
    private void PageChangeActual(PageStackData stack, PageSubType subType)
    {
        if (pageCurrent == stack && (PageCurrentSub == subType || (int)subType == -1))
            return;
        ModAnimation.AniControlEnabled += 1;
        try
        {
            #region 子页面处理

            var pageName = PageNameGet(stack);
            if (string.IsNullOrEmpty(pageName))
            {
                // 即将切换到一个顶级页面
                PageChangeExit();
            }
            // 即将切换到一个子页面
            else if (pageStack.Any())
            {
                // 子页面 → 另一个子页面，更新
                ModAnimation.AniStart(
                    new[]
                    {
                    ModAnimation.AaOpacity(LabTitleInner, -LabTitleInner.Opacity, 130),
                    ModAnimation.AaCode(() => LabTitleInner.Text = pageName, after: true),
                    ModAnimation.AaOpacity(LabTitleInner, 1d, 150, 30)
                    }, "FrmMain Titlebar SubLayer");
                if (pageStack.Contains(stack))
                    // 返回到更上层的子页面
                    while (pageStack.Contains(stack))
                        pageStack.RemoveAt(0);
                else
                    // 进入更深层的子页面
                    pageStack.Insert(0, pageCurrent);
            }
            else
            {
                // 主页面 → 子页面，进入
                PanTitleInner.Visibility = Visibility.Visible;
                PanTitleMain.IsHitTestVisible = false;
                PanTitleInner.IsHitTestVisible = true;
                PageNameRefresh(stack);
                ModAnimation.AniStart(
                    new[]
                    {
                    ModAnimation.AaOpacity(PanTitleMain, -PanTitleMain.Opacity, 150),
                    ModAnimation.AaX(PanTitleMain, 12d - PanTitleMain.Margin.Left, 150,
                        ease: new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)),
                    ModAnimation.AaOpacity(PanTitleInner, 1d - PanTitleInner.Opacity, 150, 200),
                    ModAnimation.AaX(PanTitleInner, -PanTitleInner.Margin.Left, 350, 200,
                        new ModAnimation.AniEaseOutBack()),
                    ModAnimation.AaCode(() => PanTitleMain.Visibility = Visibility.Collapsed, after: true)
                    }, "FrmMain Titlebar FirstLayer");
                pageStack.Insert(0, pageCurrent);
            }

            #endregion

            #region 实际更改页面框架 UI

            pageLast = pageCurrent;
            pageCurrent = stack;
            switch (stack.page)
            {
                case PageType.Launch: // 启动
                    {
                        PageChangeAnim(ModMain.frmLaunchLeft, ModMain.frmLaunchRight);
                        break;
                    }
                case PageType.Download: // 下载
                    {
                        ModMain.frmDownloadLeft ??= new PageDownloadLeft();
                        if (subType != PageSubType.Default)
                            ModMain.frmDownloadLeft.pageID = subType;
                        else
                            subType = ModMain.frmDownloadLeft.pageID;
                        // PageGet 方法会在未设置 SubType 时指定默认值，并建立相关页面的实例
                        PageChangeAnim(ModMain.frmDownloadLeft, (FrameworkElement)ModMain.frmDownloadLeft.PageGet(subType));
                        break;
                    }
                case PageType.Tools: // 联机
                    {
                        ModMain.frmToolsLeft ??= new PageToolsLeft();
                        subType = ModMain.frmToolsLeft.pageID;
                        PageChangeAnim(ModMain.frmToolsLeft, (FrameworkElement)ModMain.frmToolsLeft.PageGet(subType));
                        break;
                    }
                case PageType.Setup: // 设置
                    {
                        ModMain.frmSetupLeft ??= new PageSetupLeft();
                        subType = ModMain.frmSetupLeft.pageID;
                        PageChangeAnim(ModMain.frmSetupLeft, (FrameworkElement)ModMain.frmSetupLeft.PageGet(subType));
                        break;
                    }
                case PageType.GameLog: // 实时日志
                    {
                        if (ModMain.frmLogLeft is null)
                            ModMain.frmLogLeft = new PageLogLeft();
                        if (ModMain.frmLogLeft is null)
                            ModMain.frmLogRight = new PageLogRight();
                        PageChangeAnim(ModMain.frmLogLeft, ModMain.frmLogRight);
                        break;
                    }
                case PageType.InstanceSelect: // 实例选择
                    {
                        if (ModMain.frmSelectLeft is null)
                            ModMain.frmSelectLeft = new PageSelectLeft();
                        if (ModMain.frmSelectRight is null)
                            ModMain.frmSelectRight = new PageSelectRight();
                        PageChangeAnim(ModMain.frmSelectLeft, ModMain.frmSelectRight);
                        break;
                    }
                case PageType.TaskManager: // 任务管理
                    {
                        if (ModMain.frmSpeedLeft is null)
                            ModMain.frmSpeedLeft = new PageSpeedLeft();
                        if (ModMain.frmSpeedRight is null)
                            ModMain.frmSpeedRight = new PageSpeedRight();
                        PageChangeAnim(ModMain.frmSpeedLeft, ModMain.frmSpeedRight);
                        break;
                    }
                case PageType.InstanceSetup: // 实例设置
                    {
                        ModMain.frmInstanceLeft ??= new PageInstanceLeft();
                        subType = ModMain.frmInstanceLeft.pageID;
                        PageChangeAnim(ModMain.frmInstanceLeft, (FrameworkElement)ModMain.frmInstanceLeft.PageGet(subType));
                        break;
                    }
                case PageType.CompDetail: // Mod 信息
                    {
                        if (ModMain.frmDownloadCompDetail is null)
                            ModMain.frmDownloadCompDetail = new PageDownloadCompDetail();
                        PageChangeAnim(new MyPageLeft(), ModMain.frmDownloadCompDetail);
                        break;
                    }
                case PageType.VersionSaves: // 存档管理
                    {
                        if (ModMain.frmInstanceSavesLeft is null)
                            ModMain.frmInstanceSavesLeft = new PageInstanceSavesLeft();
                        PageInstanceSavesLeft.currentSave = stack.additional.Value.SavePath;
                        subType = ModMain.frmInstanceSavesLeft.pageID;
                        PageChangeAnim(ModMain.frmInstanceSavesLeft,
                            (FrameworkElement)ModMain.frmInstanceSavesLeft.PageGet(subType));
                        break;
                    }
            }

            #endregion

            #region 设置为最新状态

            BtnExtraDownload.ShowRefresh();
            BtnExtraApril.ShowRefresh();

            #endregion

            ModBase.Log("[Control] 切换主要页面：" + ModBase.GetStringFromEnum(stack) + ", " + (int)subType);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "切换主要页面失败（ID " + (int)pageCurrent.page + "）",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Main.Error.OperationFailed"));
        }
        finally
        {
            ModAnimation.AniControlEnabled -= 1;
        }
    }

    private void PageChangeAnim(FrameworkElement targetLeft, FrameworkElement targetRight)
    {
        ModAnimation.AniStop("FrmMain LeftChange");
        ModAnimation.AniStop("PageLeft PageChange"); // 停止左边栏变更导致的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
        ModAnimation.AniControlEnabled += 1;
        // 清除新页面关联性
        if (targetLeft.Parent is not null)
            targetLeft.SetValue(ContentPresenter.ContentProperty, null);
        if (targetRight is not null && targetRight.Parent is not null)
            targetRight.SetValue(ContentPresenter.ContentProperty, null);
        pageLeft = (MyPageLeft)targetLeft;
        pageRight = (MyPageRight)targetRight;
        // 触发页面通用动画
        ((MyPageLeft)PanMainLeft.Child).TriggerHideAnimation();
        ((MyPageRight)PanMainRight.Child).PageOnExit();
        ModAnimation.AniControlEnabled -= 1;
        // 执行动画
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaCode(() =>
            {
                ModAnimation.AniControlEnabled += 1;
                // 把新页面添加进容器
                PanMainLeft.Child = pageLeft;
                pageLeft.Opacity = 0d;
                PanMainLeft.Background = null;
                ModAnimation.AniControlEnabled -= 1;
                ModBase.RunInUi(() => PanMainLeft_Resize(PanMainLeft.ActualWidth), true);
            }, 110),
            ModAnimation.AaCode(() =>
            {
                // 延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                pageLeft.Opacity = 1d;
                pageLeft.TriggerShowAnimation();
            }, 30, true)
        }, "FrmMain PageChangeLeft");
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaCode(() =>
            {
                ModAnimation.AniControlEnabled += 1;
                ((MyPageRight)PanMainRight.Child).PageOnForceExit();
                // 把新页面添加进容器
                PanMainRight.Child = pageRight;
                pageRight.Opacity = 0d;
                PanMainRight.Background = null;
                ModAnimation.AniControlEnabled -= 1;
                ModBase.RunInUi(() => BtnExtraBack.ShowRefresh(), true);
            }, 110),
            ModAnimation.AaCode(() =>
            {
                // 延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                pageRight.Opacity = 1d;
                pageRight.PageOnEnter();
            }, 30, true)
        }, "FrmMain PageChangeRight");
    }

    /// <summary>
    ///     退出子界面。
    /// </summary>
    private void PageChangeExit()
    {
        if (pageStack.Any())
        {
            // 子页面 → 主页面，退出
            PanTitleMain.Visibility = Visibility.Visible;
            PanTitleMain.IsHitTestVisible = true;
            PanTitleInner.IsHitTestVisible = false;
            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaOpacity(PanTitleInner, -PanTitleInner.Opacity, 150),
                    ModAnimation.AaX(PanTitleInner, -18 - PanTitleInner.Margin.Left, 150,
                        ease: new ModAnimation.AniEaseInFluent()),
                    ModAnimation.AaOpacity(PanTitleMain, 1d - PanTitleMain.Opacity, 150, 200),
                    ModAnimation.AaX(PanTitleMain, -PanTitleMain.Margin.Left, 350, 200,
                        new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)),
                    ModAnimation.AaCode(() => PanTitleInner.Visibility = Visibility.Collapsed, after: true)
                }, "FrmMain Titlebar FirstLayer");
            pageStack.Clear();
        }
        // 主页面 → 主页面，无事发生
    }

    // 左边栏改变
    private void PanMainLeft_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;
        PanMainLeft_Resize(e.NewSize.Width);
    }

    private void PanMainLeft_Resize(double newWidth)
    {
        var delta = newWidth - RectLeftBackground.Width;
        if (Math.Abs(delta) > 0.1d && ModAnimation.AniControlEnabled == 0)
        {
            if (PanMain.Opacity < 0.1d)
                PanMainLeft.IsHitTestVisible = false; // 避免左边栏指向背景未能完美覆盖左边栏
            if (newWidth > 0d)
                // 宽度足够，显示
                ModAnimation.AniStart(
                    new[]
                    {
                        ModAnimation.AaWidth(RectLeftBackground, newWidth - RectLeftBackground.Width, 180,
                            ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.ExtraStrong)),
                        ModAnimation.AaOpacity(RectLeftShadow, 1d - RectLeftShadow.Opacity, 180),
                        ModAnimation.AaCode(() => PanMainLeft.IsHitTestVisible = true, 150)
                    }, "FrmMain LeftChange", true);
            else
                // 宽度不足，隐藏
                ModAnimation.AniStart(
                    new[]
                    {
                        ModAnimation.AaWidth(RectLeftBackground, -RectLeftBackground.Width, 180,
                            ease: new ModAnimation.AniEaseOutFluent()),
                        ModAnimation.AaOpacity(RectLeftShadow, -RectLeftShadow.Opacity, 180),
                        ModAnimation.AaCode(() => PanMainLeft.IsHitTestVisible = true, 150)
                    }, "FrmMain LeftChange", true);
        }
        else
        {
            RectLeftBackground.Width = newWidth;
            PanMainLeft.IsHitTestVisible = true;
            ModAnimation.AniStop("FrmMain LeftChange");
        }
    }

    #endregion

    #region 控件拖动

    // 在时钟中调用，使得即使鼠标在窗口外松开，也可以释放控件
    public void DragTick()
    {
        if (ModMain.dragControl is null)
            return;
        if (!(Mouse.LeftButton == MouseButtonState.Pressed)) DragStop();
    }

    // 在鼠标移动时调用，以改变 Slider 位置
    public void DragDoing()
    {
        if (ModMain.dragControl is null)
            return;
        if (Mouse.LeftButton == MouseButtonState.Pressed) 
        {
            ModMain.dragControl.DragDoing();
        }
        else
            DragStop();
    }

    private void PanBack_MouseMove(object sender, EventArgs e)
    {
        DragDoing();
    }

    public void DragStop()
    {
        // 存在其他线程调用的可能性，因此需要确保在 UI 线程运行
        ModBase.RunInUi(() =>
        {
            if (ModMain.dragControl is null)
                return;
            var control = ModMain.dragControl;
            ModMain.dragControl = null;
            control.DragStop(); // 控件会在该事件中判断 DragControl，所以得放在后面
        });
    }

    #endregion

    #region 附加按钮

    // 更新重启
    private void BtnExtraUpdateRestart_Click(object sender, MouseButtonEventArgs e)
    {
        UpdateManager.UpdateRestart(true);
    }

    private bool BtnExtraUpdateRestart_ShowCheck()
    {
        return UpdateManager.isUpdateWaitingRestart;
    }

    // 音乐
    private void BtnExtraMusic_Click(object sender, MouseButtonEventArgs e)
    {
        ModMusic.MusicControlPause();
    }

    private void BtnExtraMusic_RightClick(object sender, MouseButtonEventArgs e)
    {
        ModMusic.MusicControlNext();
    }

    // 任务管理
    private void BtnExtraDownload_Click(object sender, MouseButtonEventArgs e)
    {
        PageChange(PageType.TaskManager);
    }

    private bool BtnExtraDownload_ShowCheck()
    {
        return ModNet.HasDownloadingTask() && !(pageCurrent == PageType.TaskManager);
    }

    // 投降
    public void AprilGiveup()
    {
        if (ModMain.isAprilEnabled && !ModMain.isAprilGiveup)
        {
            HintService.Hint("=D", HintType.Success);
            ModMain.isAprilGiveup = true;
            ModMain.frmLaunchLeft.AprilScaleTrans.ScaleX = 1d;
            ModMain.frmLaunchLeft.AprilScaleTrans.ScaleY = 1d;
            BtnExtraApril.ShowRefresh();
        }
    }

    private void BtnExtraApril_Click(object sender, MouseButtonEventArgs e)
    {
        AprilGiveup();
    }

    public bool BtnExtraApril_ShowCheck()
    {
        return ModMain.isAprilEnabled && !ModMain.isAprilGiveup && pageCurrent == PageType.Launch;
    }

    // 关闭 Minecraft
    private void BtnExtraShutdown_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (ModLaunch.mcLaunchLoaderReal is not null)
                ModLaunch.mcLaunchLoaderReal.Abort();
            foreach (var Watcher in ModWatcher.mcWatcherList)
                Watcher.Kill();
            HintService.Hint(Lang.Text("Main.ShutdownMinecraft.Success"), HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "强制关闭所有 Minecraft 失败",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Main.Error.OperationFailed"));
        }
    }

    public bool BtnExtraShutdown_ShowCheck()
    {
        return ModWatcher.hasRunningMinecraft;
    }

    // 游戏日志
    private void BtnExtraLog_Click(object sender, MouseButtonEventArgs e)
    {
        PageChange(PageType.GameLog);
    }

    public bool BtnExtraLog_ShowCheck()
    {
        if (ModMain.frmLogLeft is null || ModMain.frmLogRight is null || pageCurrent == PageType.GameLog)
            return false;
        return ModMain.frmLogLeft.shownLogs.Count > 0;
    }

    /// <summary>
    ///     返回顶部。
    /// </summary>
    public void BackToTop()
    {
        var realScroll = BtnExtraBack_GetRealChild();
        if (realScroll is not null)
            realScroll.PerformVerticalOffsetDelta(-realScroll.VerticalOffset);
        else
            ModBase.Log(
                "[UI] 无法返回顶部，未找到合适的 RealScroll",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Main.Error.ScrollToTopFailed"));
    }

    private void BtnExtraBack_Click(object sender, MouseButtonEventArgs e)
    {
        BackToTop();
    }

    private bool BtnExtraBack_ShowCheck()
    {
        var realScroll = BtnExtraBack_GetRealChild();
        return realScroll is not null && realScroll.Visibility == Visibility.Visible &&
               realScroll.VerticalOffset > Height + (BtnExtraBack.Show ? 0 : 700);
    }

    private MyScrollViewer? BtnExtraBack_GetRealChild()
    {
        if (PanMainRight.Child is null || !(PanMainRight.Child is MyPageRight))
            return null;
        return ((MyPageRight)PanMainRight.Child).PanScroll;
    }

    #endregion
}

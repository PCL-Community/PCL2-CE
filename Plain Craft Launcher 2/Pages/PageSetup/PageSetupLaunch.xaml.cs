using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.Utils.OS;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageSetupLaunch
{
    private bool isLoad;

    public PageSetupLaunch()
    {
        Loaded += PageSetupLaunch_Loaded;
        InitializeComponent();
    }

    private void PageSetupLaunch_Loaded(object sender, RoutedEventArgs e)
    {
        // 重复加载部分
        PanBack.ScrollToHome();
        RefreshRam(false);
        if (ModInstanceList.McMcInstanceSelected is null)
            BtnSwitch.Visibility = Visibility.Collapsed;
        else
            BtnSwitch.Visibility = Visibility.Visible;

        // 非重复加载部分
        if (isLoad)
            return;
        isLoad = true;

        ModAnimation.AniControlEnabled += 1;
        Reload();
        ModAnimation.AniControlEnabled -= 1;

        // 内存自动刷新
        var timer = new DispatcherTimer { Interval = new TimeSpan(0, 0, 0, 1) };
        timer.Tick += (_, _) => RefreshRam();
        timer.Start();
        RectRamGame.SizeChanged += (s, e) => RefreshRamText();
    }

    public void Reload()
    {
        try
        {
            // 启动参数
            TextArgumentTitle.Text = Config.Launch.Title;
            TextArgumentInfo.Text = Config.Launch.TypeInfo;
            ComboArgumentIndieV2.SelectedIndex = Config.Launch.IndieSolutionV2;
            ComboArgumentPersonalFilesBackup.SelectedIndex = (int)Config.Launch.PersonalFilesBackup;
            ComboArgumentVisibie.SelectedIndex = (int)Config.Launch.LauncherVisibility;
            ComboArgumentPriority.SelectedValue = ((int)Config.Launch.ProcessPriority).ToString();
            ComboArgumentWindowType.SelectedIndex = (int)Config.Launch.GameWindowMode;
            TextArgumentWindowWidth.Text = Config.Launch.GameWindowWidth.ToString();
            TextArgumentWindowHeight.Text = Config.Launch.GameWindowHeight.ToString();
            ComboMsAuthType.SelectedIndex = Config.Launch.LoginMsAuthType;
            ComboPreferredIpStack.SelectedIndex = (int)Config.Launch.PreferredIpStack;
            WindowTypeUIRefresh();

            // 游戏内存
            ((MyRadioBox)FindName("RadioRamType" + Config.Launch.MemoryAllocationMode)).Checked = true;
            SliderRamCustom.Value = Config.Launch.CustomMemorySize;
            RamType(Config.Launch.MemoryAllocationMode);

            // 高级设置
            ComboAdvanceRenderer.SelectedIndex = Config.Launch.Renderer;
            TextAdvanceJvm.Text = Config.Launch.JvmArgs;
            TextAdvanceGame.Text = Config.Launch.GameArgs;
            TextAdvanceRun.Text = Config.Launch.PreLaunchCommand;
            CheckAdvanceRunWait.Checked = Config.Launch.PreLaunchCommandWait;
            CheckAdvanceDisableLF.Checked = Config.Launch.DisableLF;
            CheckAdvanceGraphicCard.Checked = Config.Launch.SetGpuPreference;
            CheckAdvanceNoJavaw.Checked = Config.Launch.NoJavaw;
            CheckAdvanceDisableLwjglUnsafeAgent.Checked = Config.Launch.DisableLwjglUnsafeAgent;
            CheckAdvanceDisableCrashAnalysis.Checked = Config.Launch.DisableCrashAnalysis;
            CheckAdvanceLockMemory.Checked = Config.Launch.LockMemory;
            if (SystemInfo.IsArm64System)
            {
                CheckAdvanceDisableJLW.Checked = true;
                CheckAdvanceDisableJLW.IsEnabled = false;
                CheckAdvanceDisableJLW.ToolTip = Lang.Text("Setup.Launch.Advanced.DisableJlw.Arm64Notice");
            }
            else
            {
                CheckAdvanceDisableJLW.Checked = Config.Launch.DisableJlw;
            }
        }

        catch (NullReferenceException ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.Launch.Error.ConfigReset"),
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Setup.Launch.Error.ConfigReset"));
            Reset();
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.Launch.Error.LoadFailed"),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Setup.Launch.Error.LoadFailed"));
        }
    }

    // 初始化
    public void Reset()
    {
        try
        {
            Config.Launch.Reset();
            ModBase.Log("[Setup] 已初始化启动设置");
            HintService.Hint(Lang.Text("Setup.Launch.Initialized"), HintType.Success, false);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.Launch.Error.InitFailed"),
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Setup.Launch.Error.InitFailed"));
        }

        Reload();
    }

    // 将控件改变路由到设置改变
    private void RadioBoxChange(object senderRaw, ModBase.RouteEventArgs e)
    {
        var sender = (MyRadioBox)senderRaw;
        var gotCfg = sender.Tag?.ToString()?.Split("/") ?? Array.Empty<string>();
        if (ModAnimation.AniControlEnabled == 0 && gotCfg.Length >= 2)
            SetByTag(gotCfg[0], int.Parse(gotCfg[1]));
    }

    private void TextBoxChange(object senderRaw, RoutedEventArgs e)
    {
        var sender = (MyTextBox)senderRaw;
        if (ModAnimation.AniControlEnabled == 0)
            SetByTag(sender.Tag?.ToString(), sender.Text);
    }

    private void TextArgumentTitle_OnTextChanged(object senderRaw, TextChangedEventArgs e)
    {
        var sender = (MyTextBox)senderRaw;
        if (ModAnimation.AniControlEnabled == 0)
            SetByTag(sender.Tag?.ToString(), sender.Text);
    }

    private void SliderChange(object senderRaw, bool user)
    {
        var sender = (MySlider)senderRaw;
        if (ModAnimation.AniControlEnabled == 0)
            SetByTag(sender.Tag?.ToString(), sender.Value);
    }

    private void ComboChange(object senderRaw, SelectionChangedEventArgs e)
    {
        var sender = (MyComboBox)senderRaw;
        if (ModAnimation.AniControlEnabled == 0)
        {
            var senderTag = sender.Tag?.ToString();
            SetByTag(senderTag,
                senderTag == "LaunchArgumentPriority" ? Convert.ToInt32(sender.SelectedValue) : sender.SelectedIndex);
            if (senderTag == "LaunchArgumentWindowType") WindowTypeUIRefresh();
        }
    }

    private void CheckBoxChange(object senderRaw, bool user)
    {
        var sender = (MyCheckBox)senderRaw;
        if (ModAnimation.AniControlEnabled == 0)
            SetByTag(sender.Tag?.ToString(), sender.Checked);
    }

    private static void SetByTag(string tag, object value)
        => ConfigService.TrySetValue(tag, value);

    // 切换到实例独立设置
    private void BtnSwitch_Click(object sender, MouseButtonEventArgs e)
    {
        ModInstanceList.McMcInstanceSelected.Load();
        PageInstanceLeft.McInstance = ModInstanceList.McMcInstanceSelected;
        ModMain.frmMain.PageChange(FormMain.PageType.InstanceSetup, FormMain.PageSubType.VersionSetup);
    }

    private void ComboAdvanceRenderer_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboChange(sender, e);
        ComboAdvanceRenderer_SelectionChanged((MyComboBox)sender, e);
    }

    private void ComboArgumentIndie_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboChange(sender, e);
        ComboArgumentIndie_SelectionChanged(sender, e);
    }

    #region 游戏内存

    public void RamType(int type)
    {
        if (SliderRamCustom is null)
            return;
        SliderRamCustom.IsEnabled = type == 1;
    }

    /// <summary>
    ///     刷新 UI 上的 RAM 显示。
    /// </summary>
    public void RefreshRam(bool showAnim)
    {
        if (LabRamGame is null || LabRamUsed is null || ModMain.frmMain.pageCurrent != FormMain.PageType.Setup ||
            ModMain.frmSetupLeft.pageID != FormMain.PageSubType.SetupLaunch)
            return;
        // 获取内存情况
        var ramGame = Math.Round(GetRam(ModInstanceList.McMcInstanceSelected, false), 5);
        var phyRam = KernelInterop.GetPhysicalMemoryBytes();
        var ramTotal = Math.Round((double)phyRam.Total / 1024 / 1024 / 1024, 1);
        var ramAvailable = Math.Round((double)phyRam.Available / 1024 / 1024 / 1024, 1);
        var ramGameActual = Math.Round(Math.Min(ramGame, ramAvailable), 5);
        var ramUsed = Math.Round(ramTotal - ramAvailable, 5);
        var ramEmpty = Math.Round(ModBase.MathClamp(ramTotal - ramUsed - ramGame, 0d, 1000d), 1);
        // 设置最大可用内存
        if (ramTotal <= 1.5d)
            SliderRamCustom.MaxValue = (int)Math.Round(Math.Max(Math.Floor((ramTotal - 0.3d) / 0.1d), 1d));
        else if (ramTotal <= 8d)
            SliderRamCustom.MaxValue = (int)Math.Round(Math.Floor((ramTotal - 1.5d) / 0.5d) + 12d);
        else if (ramTotal <= 16d)
            SliderRamCustom.MaxValue = (int)Math.Round(Math.Floor((ramTotal - 8d) / 1d) + 25d);
        else
            SliderRamCustom.MaxValue = (int)Math.Round(Math.Floor((ramTotal - 16d) / 2d) + 33d);
        // 设置文本
        LabRamGame.Text = $"{Lang.Number(ramGame, "N1")} GiB{(ramGame != ramGameActual ? $" ({Lang.Text("Setup.Launch.Memory.AvailableSuffix", Lang.Number(ramGameActual, "N1"))})" : "")}";
        LabRamUsed.Text = $"{Lang.Number(ramUsed, "N1")} GiB";
        LabRamTotal.Text = $" / {Lang.Number(ramTotal, "N1")} GiB";
        LabRamWarn.Visibility =
            ramGame == 1d && !ModJava.IsGameSet64BitJava() && !SystemInfo.Is32BitSystem && ModJava.Javas.ExistAnyJava()
                ? Visibility.Visible
                : Visibility.Collapsed;
        HintRamTooHigh.Visibility = ramGame / ramTotal > 0.75d ? Visibility.Visible : Visibility.Collapsed;
        if (showAnim)
        {
            // 宽度动画
            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaGridLengthWidth(ColumnRamUsed, ramUsed - ColumnRamUsed.Width.Value, 800,
                        ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)),
                    ModAnimation.AaGridLengthWidth(ColumnRamGame, ramGameActual - ColumnRamGame.Width.Value, 800,
                        ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)),
                    ModAnimation.AaGridLengthWidth(ColumnRamEmpty, ramEmpty - ColumnRamEmpty.Width.Value, 800,
                        ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong))
                }, "SetupLaunch Ram Grid");
        }
        else
        {
            // 宽度设置
            ColumnRamUsed.Width = new GridLength(ramUsed, GridUnitType.Star);
            ColumnRamGame.Width = new GridLength(ramGameActual, GridUnitType.Star);
            ColumnRamEmpty.Width = new GridLength(ramEmpty, GridUnitType.Star);
        }
    }

    private void RefreshRam()
    {
        RefreshRam(true);
    }

    private int ramTextLeft = 2;
    private int ramTextRight = 1;

    /// <summary>
    ///     刷新 UI 上的文本位置。
    /// </summary>
    private void RefreshRamText()
    {
        // 获取宽度信息
        var rectUsedWidth = RectRamUsed.ActualWidth;
        var totalWidth = PanRamDisplay.ActualWidth;
        var labGameWidth = LabRamGame.ActualWidth;
        var labUsedWidth = LabRamUsed.ActualWidth;
        var labTotalWidth = LabRamTotal.ActualWidth;
        var labGameTitleWidth = LabRamGameTitle.ActualWidth;
        var labUsedTitleWidth = LabRamUsedTitle.ActualWidth;
        // 左侧
        int left;
        if (rectUsedWidth - 30d < labUsedWidth || rectUsedWidth - 30d < labUsedTitleWidth)
            // 全写不下了
            left = 0;
        else if (rectUsedWidth - 25d < labUsedWidth + labTotalWidth)
            // 显示不下完整数据
            left = 1;
        else
            // 正常
            left = 2;
        if (ramTextLeft != left)
        {
            ramTextLeft = left;
            switch (left)
            {
                case 0:
                {
                    ModAnimation.AniStart(
                        new[]
                        {
                            ModAnimation.AaOpacity(LabRamUsed, -LabRamUsed.Opacity, 100),
                            ModAnimation.AaOpacity(LabRamTotal, -LabRamTotal.Opacity, 100),
                            ModAnimation.AaOpacity(LabRamUsedTitle, -LabRamUsedTitle.Opacity, 100)
                        }, "SetupLaunch Ram TextLeft");
                    break;
                }
                case 1:
                {
                    ModAnimation.AniStart(
                        new[]
                        {
                            ModAnimation.AaOpacity(LabRamUsed, 1d - LabRamUsed.Opacity, 100),
                            ModAnimation.AaOpacity(LabRamTotal, -LabRamTotal.Opacity, 100),
                            ModAnimation.AaOpacity(LabRamUsedTitle, 0.7d - LabRamUsedTitle.Opacity, 100)
                        }, "SetupLaunch Ram TextLeft");
                    break;
                }
                case 2:
                {
                    ModAnimation.AniStart(
                        new[]
                        {
                            ModAnimation.AaOpacity(LabRamUsed, 1d - LabRamUsed.Opacity, 100),
                            ModAnimation.AaOpacity(LabRamTotal, 1d - LabRamTotal.Opacity, 100),
                            ModAnimation.AaOpacity(LabRamUsedTitle, 0.7d - LabRamUsedTitle.Opacity, 100)
                        }, "SetupLaunch Ram TextLeft");
                    break;
                }
            }
        }

        // 右侧
        int right;
        if (totalWidth < labGameWidth + 2d + rectUsedWidth || totalWidth < labGameTitleWidth + 2d + rectUsedWidth)
            // 挤到最右边
            right = 0;
        else
            // 正常情况
            right = 1;
        if (right == 0)
        {
            if (ModAnimation.AniControlEnabled == 0 &&
                (ramTextRight != right || ModAnimation.AniIsRun("SetupLaunch Ram TextRight")))
            {
                // 需要动画
                ModAnimation.AniStart(
                    new[]
                    {
                        ModAnimation.AaX(LabRamGame, totalWidth - labGameWidth - LabRamGame.Margin.Left, 100,
                            ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)),
                        ModAnimation.AaX(LabRamGameTitle, totalWidth - labGameTitleWidth - LabRamGameTitle.Margin.Left,
                            100, ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak))
                    }, "SetupLaunch Ram TextRight");
            }
            else
            {
                // 不需要动画
                ModAnimation.AniStop("SetupLaunch Ram TextRight");
                LabRamGame.Margin = new Thickness(totalWidth - labGameWidth, 3d, 0d, 0d);
                LabRamGameTitle.Margin = new Thickness(totalWidth - labGameTitleWidth, 0d, 0d, 5d);
            }
        }
        else if (ModAnimation.AniControlEnabled == 0 &&
                 (ramTextRight != right || ModAnimation.AniIsRun("SetupLaunch Ram TextRight")))
        {
            // 需要动画
            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaX(LabRamGame, 2d + rectUsedWidth - LabRamGame.Margin.Left, 100,
                        ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)),
                    ModAnimation.AaX(LabRamGameTitle, 2d + rectUsedWidth - LabRamGameTitle.Margin.Left, 100,
                        ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak))
                }, "SetupLaunch Ram TextRight");
        }
        else
        {
            // 不需要动画
            ModAnimation.AniStop("SetupLaunch Ram TextRight");
            LabRamGame.Margin = new Thickness(2d + rectUsedWidth, 3d, 0d, 0d);
            LabRamGameTitle.Margin = new Thickness(2d + rectUsedWidth, 0d, 0d, 5d);
        }

        ramTextRight = right;
    }

    /// <summary>
    ///     获取当前设置的 RAM 值。单位为 GB。
    /// </summary>
    public static double GetRam(McInstance version, bool useVersionJavaSetup, bool? is32BitJava = default)
    {
        // ------------------------------------------
        // 修改下方代码时需要一并修改 PageInstanceSetup
        // ------------------------------------------

        var ramGive = default(double);
        if (Config.Launch.MemoryAllocationMode == 0)
        {
            // 自动配置
            var ramAvailable =
                Math.Round((double)KernelInterop.GetAvailablePhysicalMemoryBytes() / 1024 / 1024 / 1024 * 10) / 10;
            // 确定需求的内存值
            double ramMininum; // 无论如何也需要保证的最低限度内存
            double ramTarget1; // 估计能勉强带动了的内存
            double ramTarget2; // 估计没啥问题了的内存
            double ramTarget3; // 放一百万个材质和 Mod 和光影需要的内存
            if (version is not null && !version.IsLoaded)
                version.Load();
            if (version is not null && version.Modable)
            {
                // 可安装 Mod 的实例
                var modDir = new DirectoryInfo(version.PathIndie + @"mods\");
                var modCount = modDir.Exists ? modDir.GetFiles().Length : 0;
                ramMininum = 0.5d + modCount / 150d;
                ramTarget1 = 1.5d + modCount / 90d;
                ramTarget2 = 2.7d + modCount / 50d;
                ramTarget3 = 4.5d + modCount / 25d;
            }
            else if (version is not null && version.Info.HasOptiFine)
            {
                // OptiFine 实例
                ramMininum = 0.5d;
                ramTarget1 = 1.5d;
                ramTarget2 = 3d;
                ramTarget3 = 5d;
            }
            else
            {
                // 普通实例
                ramMininum = 0.5d;
                ramTarget1 = 1.5d;
                ramTarget2 = 2.5d;
                ramTarget3 = 4d;
            }

            var ramStages = new[]
            {
                (Delta: ramTarget1, Ratio: 1d),
                (Delta: ramTarget2 - ramTarget1, Ratio: 0.7d),
                (Delta: ramTarget3 - ramTarget2, Ratio: 0.4d),
                (Delta: ramTarget3, Ratio: 0.15d)
            };
            foreach (var (RamDelta, RamRatio) in ramStages)
            {
                ramGive += Math.Min(ramAvailable * RamRatio, RamDelta);
                ramAvailable -= RamDelta / RamRatio;
                if (ramAvailable < 0.1d)
                    break;
            }

            // 不低于最低值
            ramGive = Math.Round(Math.Max(ramGive, ramMininum), 1);
        }
        else
        {
            // 手动配置
            var value = Config.Launch.CustomMemorySize;
            ramGive = value switch
            {
                <= 12 => value * 0.1d + 0.3d,
                <= 25 => (value - 12) * 0.5d + 1.5d,
                <= 33 => (value - 25) * 1 + 8,
                _ => (value - 33) * 2 + 16
            };
        }

        // 若使用 32 位 Java，则限制为 1G
        if (is32BitJava ?? !ModJava.IsGameSet64BitJava(useVersionJavaSetup ? version : null))
            ramGive = Math.Min(1d, ramGive);
        return ramGive;
    }

    #endregion

    #region 其他选项

    private void WindowTypeUIRefresh()
    {
        if (ComboArgumentWindowType is null)
            return;
        if (ComboArgumentWindowType.SelectedIndex == 3 && LabArgumentWindowMiddle is not null &&
            LabArgumentWindowMiddle.Visibility == Visibility.Collapsed)
        {
            LabArgumentWindowMiddle.Visibility = Visibility.Visible;
            TextArgumentWindowHeight.Visibility = Visibility.Visible;
            TextArgumentWindowWidth.Visibility = Visibility.Visible;
        }
        else if (ComboArgumentWindowType.SelectedIndex != 3 && LabArgumentWindowMiddle is not null &&
                 LabArgumentWindowMiddle.Visibility == Visibility.Visible)
        {
            LabArgumentWindowMiddle.Visibility = Visibility.Collapsed;
            TextArgumentWindowHeight.Visibility = Visibility.Collapsed;
            TextArgumentWindowWidth.Visibility = Visibility.Collapsed;
        }
    }

    // 可见性选择直接关闭的警告
    private void ComboArgumentVisibie_SelectionChanged(object sender, SelectionChangedEventArgs sizeChangedEventArgs)
    {
        ComboChange(sender, sizeChangedEventArgs);
        if (ModAnimation.AniControlEnabled != 0)
            return;
        if (ComboArgumentVisibie.SelectedIndex == 0)
            if (ModMain.MyMsgBox(
                    Lang.Text("Setup.Launch.Options.Visibility.CloseImmediately.Warning.Message"),
                    Lang.Text("Setup.Launch.Options.Visibility.CloseImmediately.Warning.Title"),
                    Lang.Text("Setup.Launch.Options.Visibility.CloseImmediately.Warning.Continue"),
                    Lang.Text("Common.Action.Cancel")) == 2)
                ComboArgumentVisibie.SelectedItem = sizeChangedEventArgs.RemovedItems[0];
    }

    // 实例隔离提示
    private void ComboArgumentIndie_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModAnimation.AniControlEnabled != 0)
            return;
        ModMain.MyMsgBox(Lang.Text("Setup.Launch.Options.InstanceIsolation.DefaultPolicyHint"));
    }

    #endregion

    #region 高级设置

    private void TextAdvanceRun_TextChanged(object sender, TextChangedEventArgs e)
    {
        CheckAdvanceRunWait.Visibility =
            string.IsNullOrEmpty(TextAdvanceRun.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // JVM 参数重设
    private void TextAdvanceJvm_TextChanged(object sender, TextChangedEventArgs e)
    {
        BtnAdvanceJvmReset.Visibility =
            TextAdvanceJvm.Text == Config.Launch.JvmArgsConfig.DefaultValue
                ? Visibility.Hidden
                : Visibility.Visible;
    }

    private void BtnAdvanceJvmReset_Click(object sender, EventArgs e)
    {
        Config.Launch.JvmArgsConfig.Reset();
        Reload();
    }

    private void ComboAdvanceRenderer_SelectionChanged(MyComboBox sender, object e)
    {
        if (ModAnimation.AniControlEnabled != 0)
            return;
        if (!States.Hint.Renderer && ComboAdvanceRenderer.SelectedIndex != 0)
        {
            if (ModMain.MyMsgBox(Lang.Text("Setup.Launch.Advanced.Renderer.Warning.Message"),
                    Lang.Text("Common.Dialog.Warning"),
                    Lang.Text("Setup.Launch.Advanced.Renderer.Warning.Confirm"),
                    Lang.Text("Common.Action.Cancel"), isWarn: true) == 2)
            {
                ComboAdvanceRenderer.SelectedItem = ((SelectionChangedEventArgs)e).RemovedItems[0];
            }
            else
            {
                Config.Launch.Renderer = sender.SelectedIndex;
                States.Hint.Renderer = true;
            }
        }
        else
        {
            Config.Launch.Renderer = sender.SelectedIndex;
        }
    }

    #endregion
}

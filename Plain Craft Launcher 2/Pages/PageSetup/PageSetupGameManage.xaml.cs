using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.App.Localization;
using PCL.Core.Utils;

namespace PCL;

public partial class PageSetupGameManage
{
    private new bool isLoaded;

    public PageSetupGameManage()
    {
        InitializeComponent();
        Loaded += PageSetupSystem_Loaded;
    }

    private void PageSetupSystem_Loaded(object sender, RoutedEventArgs e)
    {
        // 重复加载部分
        PanBack.ScrollToHome();

        // 非重复加载部分
        if (isLoaded)
            return;
        isLoaded = true;

        ModAnimation.AniControlEnabled += 1;
        Reload();
        SliderLoad();

        if (!Lang.IsChineseMainland)
        {
            TextFilenameFormat.Visibility = Visibility.Collapsed;
            ComboDownloadTranslateV2.Visibility = Visibility.Collapsed;
            TextModManageStyle.Visibility = Visibility.Collapsed;
            ComboModLocalNameStyle.Visibility = Visibility.Collapsed;
            
            RowFilenameFormat.Height = new GridLength(0);
            RowFilenameFormatGap.Height = new GridLength(0);
            RowModManageStyle.Height = new GridLength(0);
            RowModManageStyleGap.Height = new GridLength(0);
        }

        ModAnimation.AniControlEnabled -= 1;
    }

    public void Reload()
    {
        // 下载
        SliderDownloadThread.Value = Config.Download.ThreadLimit;
        SliderDownloadFileConnection.Value = Config.Download.FileConnectionLimit;
        SliderDownloadSpeed.Value = Config.Download.SpeedLimit;
        ComboDownloadHttpMode.SelectedIndex = (int)Config.Download.HttpMode;
        ComboDownloadSource.SelectedIndex = Config.Download.FileSource;
        ComboDownloadVersion.SelectedIndex = Config.Download.VersionListSource;
        CheckDownloadAutoSelectVersion.Checked = Config.Download.AutoSelectInstance;
        CheckFixAuthlib.Checked = Config.Download.FixAuthLib;

        // Mod 与整合包
        ComboDownloadTranslateV2.SelectedIndex = Config.Download.Comp.NameFormatV2;
        ComboDownloadMod.SelectedIndex = Config.Download.Comp.CompSourceSolution;
        ComboModLocalNameStyle.SelectedIndex = Config.Download.Comp.UiCompNameSolution;
        ComboDownloadQuickBehavior.SelectedIndex = Config.Download.Comp.QuickDownloadBehavior;
        CheckDownloadIgnoreQuilt.Checked = Config.Download.Comp.IgnoreQuilt;
        CheckDownloadAutoInstallDependencies.Checked = Config.Download.Comp.AutoInstallDependencies;
        CheckDownloadClipboard.Checked = Config.Download.Comp.ReadClipboard;

        // Minecraft 更新提示
        CheckUpdateRelease.Checked = Config.Tool.ReleaseNotification;
        CheckUpdateSnapshot.Checked = Config.Tool.SnapshotNotification;

        // 辅助设置
        CheckHelpLauncherLanguage.Checked = Config.Tool.AutoChangeLanguage;
    }

    // 初始化
    public void Reset()
    {
        try
        {
            Config.Download.Reset();
            Config.Tool.Reset();
            ModBase.Log("[Setup] 已初始化其他页设置");
            HintService.Hint(Lang.Text("Setup.GameManage.Initialized"), HintType.Success, false);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.GameManage.Error.InitFailed"),
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Setup.GameManage.Error.InitFailed"));
        }

        Reload();
    }

    // 将控件改变路由到设置改变
    private void CheckBoxChange(object senderRaw, bool user)
    {
        var sender = (MyCheckBox)senderRaw;
        if (ModAnimation.AniControlEnabled == 0)
            SetByTag(sender.Tag?.ToString(), sender.Checked);
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
            SetByTag(sender.Tag?.ToString(), sender.SelectedIndex);
    }

    private static void SetByTag(string tag, object value)
        => ConfigService.TrySetValue(tag, value);

    // 滑动条
    private void SliderLoad()
    {
        SliderDownloadThread.getHintText = new Func<object, object>(v => (int)v + 1);
        SliderDownloadFileConnection.getHintText = new Func<object, object>(v => (int)v + 1);
        SliderDownloadSpeed.getHintText = new Func<object, object>(v =>
        {
            int value = (int)v;
            switch (value)
            {
                case <= 14:
                    return Lang.Number((value + 1) * 0.1d, "N1") + " MiB/s";
                case <= 31:
                    return Lang.Number((value - 11) * 0.5d, "N1") + " MiB/s";
                case <= 41:
                    return Lang.Number(value - 21, "N0") + " MiB/s";
                default:
                    return Lang.Text("Setup.GameManage.Download.Unlimited");
            }
        });
    }

    private void SliderDownloadThread_PreviewChange(object sender, ModBase.RouteEventArgs e)
    {
        if (SliderDownloadThread.Value < 64)
            return;
        if (!States.Hint.LargeDownloadThread)
        {
            States.Hint.LargeDownloadThread = true;
            ModMain.MyMsgBox(
                Lang.Text("Setup.GameManage.Download.Threads.TooManyWarning.Message"),
                Lang.Text("Common.Dialog.Warning"),
                Lang.Text("Setup.GameManage.Download.Threads.TooManyWarning.Confirm"), isWarn: true);
        }
    }
}

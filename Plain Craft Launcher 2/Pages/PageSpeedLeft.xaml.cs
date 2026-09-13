using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PCL.Network;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageSpeedLeft
{
    private const int watcherInterval = 300;

    // 定时器任务
    private readonly Dictionary<string, MyCard> rightCards = new();

    // 初始化
    private bool isLoad;

    public PageSpeedLeft()
    {
        InitializeComponent();
        Loaded += Page_Loaded;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // 进入时就刷新一次显示
        Watcher();

        // 如果在页面切换动画的 “上一页消失” 部分已经完成了下载，就直接尝试返回
        TryReturnToHome();

        if (isLoad)
            return;
        isLoad = true;

        // 监控定时器
        var timer = new DispatcherTimer { Interval = new TimeSpan(0, 0, 0, 0, watcherInterval) };
        timer.Tick += (_, _) => Watcher();
        timer.Start();

        // 非调试模式隐藏连接数
        if (!ModBase.modeDebug)
        {
            RowDefinitions[12].Height = new GridLength(0d);
            RowDefinitions[13].Height = new GridLength(0d);
            RowDefinitions[14].Height = new GridLength(0d);
            RowDefinitions[15].Height = new GridLength(0d);
        }
    }

    private void Watcher()
    {
        if (ModMain.frmMain.pageCurrent != FormMain.PageType.TaskManager)
            return;
        try
        {
            #region 更新左边栏

            if (!ModLoader.loaderTaskbar.Any())
            {
                // 无任务
                LabProgress.Text = Lang.Number(1d, "P0");
                LabSpeed.Text = ModBase.GetString(0) + "/s";
                LabFile.Text = Lang.Number(0, "N0");
                LabThread.Text = Lang.Number(0, "N0") + " / " + Lang.Number(ModNet.NetTaskConnectionLimit, "N0");
            }
            else
            {
                // 有任务，输出基本信息
                var tasks = ModLoader.loaderTaskbar.Where(l => l.show).ToList(); // 筛选掉启动 MC 的任务（#6270）
                var rawPercent = tasks.Any()
                    ? ModBase.MathClamp(
                        tasks.Average(l => l.Progress),
                        0, 1)
                    : 1d;
                var predictText = Lang.Number(rawPercent, "P2");
                LabProgress.Text = rawPercent > 0.999999d ? Lang.Number(1d, "P0") : predictText;
                LabSpeed.Text = ModBase.GetString(ModNet.NetManager.Speed) + "/s";
                LabFile.Text = ModNet.NetManager.FileRemain < 0 ? "0*" : Lang.Number(ModNet.NetManager.FileRemain, "N0");
                LabThread.Text = Lang.Number(ModNet.NetManager.ConnectionCount, "N0") + " / " +
                                 Lang.Number(ModNet.NetTaskConnectionLimit, "N0");
            }
        }

        #endregion

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "任务管理左栏监视出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Speed.Error.OperationFailed"));
        }

        if (ModMain.frmSpeedRight is null || ModMain.frmSpeedRight.PanMain is null)
            return;
        try
        {
            foreach (var Loader in ModLoader.loaderTaskbar.ToList())
                TaskRefresh(Loader);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "任务管理右栏监视出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Speed.Error.OperationFailed"));
        }
    }

    public void TaskRefresh(ModLoader.LoaderBase loader)
    {
        if (loader is null || !loader.show)
            return;
        try
        {
            // 获取实际加载器列表
            var loaderList = ((ModLoader.LoaderCombo)loader).GetLoaderList();
            if (rightCards.ContainsKey(loader.name))
            {
                // 已有此卡片
                Grid card = rightCards[loader.name];
                var newValue = loader.Progress + (double)loader.State;
                if (ModBase.Val(card.Tag) == newValue)
                    return;
                card.Tag = newValue;
                if (card.Children.Count <= 3)
                {
                    ModBase.Log("[Watcher] 元素不足的卡片：" + loader.name, ModBase.LogLevel.Debug);
                    return;
                }

                card = (Grid)card.Children[3];
                try
                {
                    switch (loader.State)
                    {
                        case ModBase.LoadState.Failed:
                        {
                            #region 失败，更新卡片

                            card.RowDefinitions.Clear();
                            card.Children.Clear();
                            card.Children.Add((UIElement)ModBase.GetObjectFromXML(
                                "<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Stretch=\"Uniform\" Tag=\"Failed\" Data=\"F1 M2.5,0 L0,2.5 7.5,10 0,17.5 2.5,20 10,12.5 17.5,20 20,17.5 12.5,10 20,2.5 17.5,0 10,7.5 2.5,0Z\" Height=\"15\" Width=\"15\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"0\" Fill=\"{DynamicResource ColorBrush3}\" Margin=\"0,1,0,0\" VerticalAlignment=\"Top\"/>"));
                            var tb = (TextBlock)ModBase.GetObjectFromXML(
                                "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TextWrapping=\"Wrap\" HorizontalAlignment=\"Left\" ToolTip=\"" + Lang.Text("Speed.Error.ClickToCopy") + "\" Grid.Column=\"1\" Grid.Row=\"0\" Margin=\"0,0,0,5\" />");
                            tb.Text = loader.Error.ToString();
                            tb.MouseLeftButtonDown += (sender, _) =>
                            {
                                ModBase.ClipboardSet(((TextBlock)sender).Text, false);
                                HintService.Hint(Lang.Text("Speed.Error.Copied"), HintType.Success);
                            };
                            card.Children.Add(tb);
                            break;
                        }

                        #endregion

                        case ModBase.LoadState.Finished:
                        {
                            #region 完成，销毁卡片并返回

                            ModAnimation.AniDispose((MyCard)card.Parent, true, _ => TryReturnToHome());
                            break;
                        }

                        #endregion

                        case ModBase.LoadState.Loading:
                        case ModBase.LoadState.Waiting:
                        {
                            #region 进度不同，更新卡片

                            do
                            {
                                try
                                {
                                    if (card.Children.Count < loaderList.Count * 2)
                                    {
                                        ModBase.Log(
                                            $"[Watcher] 刷新任务管理卡片 {loader.name} 失败：卡片中仅有 {card.Children.Count} 个子项，要求至少有 {loaderList.Count * 2} 个子项",
                                            ModBase.LogLevel.Debug);
                                        break;
                                    }

                                    var row = 0;
                                    foreach (var SubTask in loaderList)
                                    {
                                        switch (SubTask.State)
                                        {
                                            case ModBase.LoadState.Waiting:
                                            {
                                                if ((string)((FrameworkElement)card.Children[row * 2]).Tag != "Waiting")
                                                {
                                                    card.Children.RemoveAt(row * 2);
                                                    card.Children.Insert(row * 2,
                                                        (UIElement)ModBase.GetObjectFromXML(
                                                            "<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"clr-namespace:PCL;assembly=Plain Craft Launcher 2\" Stretch=\"Uniform\" Tag=\"Waiting\" Data=\"F1 M5,0 a5,5 360 1 0 0,0.0001 m15,0 a5,5 360 1 0 0,0.0001 m15,0 a5,5 360 1 0 0,0.0001 Z\" Width=\"18\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"" +
                                                            row +
                                                            "\" Fill=\"{DynamicResource ColorBrush3}\" Margin=\"0,7,0,0\" VerticalAlignment=\"Top\" Height=\"6\"/>"));
                                                }

                                                break;
                                            }
                                            case ModBase.LoadState.Loading:
                                            {
                                                if ((string)((FrameworkElement)card.Children[row * 2]).Tag != "Loading")
                                                {
                                                    card.Children.RemoveAt(row * 2);
                                                    card.Children.Insert(row * 2,
                                                        (UIElement)ModBase.GetObjectFromXML(
                                                            $"<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"clr-namespace:PCL;assembly=Plain Craft Launcher 2\" Text=\"{Lang.Number(SubTask.Progress, "P0")}\" Tag=\"Loading\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"{row}\" Foreground=\"{{DynamicResource ColorBrush3}}\"/>"));
                                                }
                                                else
                                                {
                                                    ((TextBlock)card.Children[row * 2]).Text =
                                                        $"{Lang.Number(SubTask.Progress, "P0")}";
                                                }

                                                break;
                                            }
                                            case ModBase.LoadState.Finished:
                                            {
                                                if ((string)((FrameworkElement)card.Children[row * 2]).Tag != "Finished")
                                                {
                                                    card.Children.RemoveAt(row * 2);
                                                    card.Children.Insert(row * 2,
                                                        (UIElement)ModBase.GetObjectFromXML(
                                                            $"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"clr-namespace:PCL;assembly=Plain Craft Launcher 2\" Stretch=\"Uniform\" Tag=\"Finished\" Data=\"F1 M 23.7501,33.25L 34.8334,44.3333L 52.2499,22.1668L 56.9999,26.9168L 34.8334,53.8333L 19.0001,38L 23.7501,33.25 Z\" Height=\"16\" Width=\"15\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"{row}\" Fill=\"{{DynamicResource ColorBrush3}}\" Margin=\"0,3,0,0\" VerticalAlignment=\"Top\"/>"));
                                                }

                                                break;
                                            }
                                        }

                                        row += 1;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ModBase.Log(
                                        ex,
                                        $"刷新任务管理卡片 {loader.name} 失败",
                                        ModBase.LogLevel.Feedback,
                                        userSummary: Lang.Text("Speed.Error.OperationFailed"));
                                }
                            } while (false);

                            break;
                        }

                        #endregion
                    }
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        $"更新任务管理显示失败（{loader.State}）",
                        ModBase.LogLevel.Feedback,
                        userSummary: Lang.Text("Speed.Error.OperationFailed"));
                }
            }
            else if (!(loader.State == ModBase.LoadState.Aborted || loader.State == ModBase.LoadState.Finished))
            {
                try
                {
                    #region 没有卡片且未中断或完成，添加新的卡片

                    var cardXAML = $@"
                        <local:MyCard xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 2""
                            Tag=""{loader.Progress + (double)loader.State}"" Title=""{ModBase.EscapeXML(loader.name)}"" Margin=""0,0,0,15"">
                            <Grid Margin=""14,40,15,10"">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width=""50""/>
                                    <ColumnDefinition/>
                                </Grid.ColumnDefinitions>
                                <Grid.RowDefinitions>";
                    foreach (var SubTask in loaderList)
                        cardXAML += "<RowDefinition Height=\"26\"/>";
                    cardXAML += "</Grid.RowDefinitions>";
                    var row = 0;
                    foreach (var SubTask in loaderList)
                    {
                        switch (SubTask.State)
                        {
                            case ModBase.LoadState.Waiting:
                            {
                                cardXAML +=
                                    $"<Path Stretch=\"Uniform\" Tag=\"Waiting\" Data=\"F1 M5,0 a5,5 360 1 0 0,0.0001 m15,0 a5,5 360 1 0 0,0.0001 m15,0 a5,5 360 1 0 0,0.0001 Z\" Width=\"18\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"{row}\" Fill=\"{{DynamicResource ColorBrush3}}\" Margin=\"0,7,0,0\" VerticalAlignment=\"Top\" Height=\"6\"/>";
                                break;
                            }
                            case ModBase.LoadState.Loading:
                            {
                                cardXAML += $"<TextBlock Text=\"{Lang.Number(SubTask.Progress, "P0")}\" Tag=\"Loading\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"{row}\" Foreground=\"{{DynamicResource ColorBrush3}}\" />";
                                break;
                            }
                            case ModBase.LoadState.Finished:
                            {
                                cardXAML +=
                                    $"<Path Stretch=\"Uniform\" Tag=\"Finished\" Data=\"F1 M 23.7501,33.25L 34.8334,44.3333L 52.2499,22.1668L 56.9999,26.9168L 34.8334,53.8333L 19.0001,38L 23.7501,33.25 Z\" Height=\"16\" Width=\"15\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"{row}\" Fill=\"{{DynamicResource ColorBrush3}}\" Margin=\"0,3,0,0\" VerticalAlignment=\"Top\"/>";
                                break;
                            }

                            default:
                            {
                                cardXAML +=
                                    $"<Path Stretch=\"Uniform\" Tag=\"Failed\" Data=\"F1 M2.5,0 L0,2.5 7.5,10 0,17.5 2.5,20 10,12.5 17.5,20 20,17.5 12.5,10 20,2.5 17.5,0 10,7.5 2.5,0Z\" Height=\"15\" Width=\"15\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"{row}\" Fill=\"{{DynamicResource ColorBrush3}}\" Margin=\"0,1,0,0\" VerticalAlignment=\"Top\"/>";
                                break;
                            }
                        }

                        cardXAML += $"<TextBlock Text=\"{ModBase.EscapeXML(SubTask.name)}\" HorizontalAlignment=\"Left\" Grid.Column=\"1\" Grid.Row=\"{row}\"/>";
                        row += 1;
                    }

                    cardXAML += "</Grid></local:MyCard>";
                    // 实例化控件
                    MyCard card;
                    try
                    {
                        card = (MyCard)ModBase.GetObjectFromXML(cardXAML);
                    }
                    catch (Exception ex)
                    {
                        ModBase.Log(ex, "新建任务管理卡片失败");
                        ModBase.Log($"出错的卡片内容：\r\n{cardXAML}");
                        throw;
                    }

                    ModMain.frmSpeedRight.PanMain.Children.Insert(0, card);
                    rightCards.Add(loader.name, card);
                    ModBase.Log($"[Watcher] 新建任务管理卡片：{loader.name}");
                    // 添加取消按钮
                    var cancel = new MyIconButton
                    {
                        Name = "BtnCancel",
                        SvgIcon = "lucide/x", Height = 25d, Width = 25d,
                        Margin = new Thickness(0d, 10d, 10d, 0d), LogoScale = 1.1d,
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top
                    };
                    card.Children.Add(cancel);
                    cancel.Click += (sender, e) =>
                    {
                        ModAnimation.AniDispose((MyIconButton)sender, false);
                        ModAnimation.AniDispose(card, true, _ =>
                        {
                            if (ModMain.frmSpeedRight.PanMain.Children.Count == 0 &&
                                ModMain.frmMain.pageCurrent == FormMain.PageType.TaskManager)
                                ModMain.frmMain.PageBack();
                        });
                        rightCards.Remove(loader.name);
                        ModLoader.loaderTaskbar.Remove(loader);
                        ModBase.Log($"[Taskbar] 关闭任务管理卡片：{loader.name}，且移出任务列表");
                        ModBase.RunInThread(() => loader.Abort());
                    };
                    // 如果已经失败，再刷新一次，修改成失败的控件
                    if (loader.State == ModBase.LoadState.Failed)
                    {
                        card.Tag = null; // 避免重复导致刷新无效
                        TaskRefresh(loader);
                    }
                }

                #endregion

                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        "添加任务管理卡片失败",
                        ModBase.LogLevel.Feedback,
                        userSummary: Lang.Text("Speed.Error.OperationFailed"));
                }
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "刷新任务管理显示失败",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Speed.Error.OperationFailed"));
        }
    }

    public void TaskRemove(ModLoader.LoaderBase loader)
    {
        if (rightCards.ContainsKey(loader.name))
            ModBase.RunInUiWait(() =>
            {
                // 移除已有的卡片
                Grid card = rightCards[loader.name];
                ModMain.frmSpeedRight.PanMain.Children.Remove(card);
                rightCards.Remove(loader.name);
                ModBase.Log($"[Watcher] 移除任务管理卡片：{loader.name}");
            });
    }

    /// <summary>
    ///     若没有任务，尝试返回主页。
    /// </summary>
    private void TryReturnToHome()
    {
        if (ModMain.frmSpeedRight.PanMain.Children.Count == 0 &&
            ModMain.frmMain.pageCurrent == FormMain.PageType.TaskManager) ModMain.frmMain.PageBack();
    }
}

using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using PCL.Core.Utils;

namespace PCL;

public partial class PageInstanceInstall
{
    private enum InstallAction
    {
        Modify,
        Reset
    }

    private bool isLoad;
    private string lastVersionName;
    private InstallAction _installAction;

    public PageInstanceInstall()
    {
        Initialized += (a, b) => LoaderInit();
        Loaded += (a, b) => Init();
        InitializeComponent();
        LoadMinecraft.Text = Lang.Text("Download.Version.LoadingList");
    }

    private void LoaderInit()
    {
        disabledPageAnimControls.Add(BtnSelectStart);
        // 游戏运行期间禁用"开始修改/重置"按钮（防止文件被游戏占用导致崩溃 #3494）
        ModVideoBack.GamingStateChanged += (_, _) => ModBase.RunInUi(_RefreshStartButtonState);
        // PageLoaderInit(LoadMinecraft, PanLoad, PanBack, Nothing, DlClientListLoader, AddressOf LoadMinecraft_OnFinish)
        PageLoaderInit(LoadMinecraft, PanLoad, PanAllBack, null, ModDownload.dlClientListLoader, _ => GetCurrentInfo());
        LoadOptiFine.StateChanged += (_, _, _) => { OptiFine_Loaded(); ReloadSelected(); };
        LoadLiteLoader.StateChanged += (_, _, _) => { LiteLoader_Loaded(); ReloadSelected(); };
        LoadForge.StateChanged += (_, _, _) => { Forge_Loaded(); ReloadSelected(); };
        LoadNeoForge.StateChanged += (_, _, _) => { NeoForge_Loaded(); ReloadSelected(); };
        LoadCleanroom.StateChanged += (_, _, _) => { Cleanroom_Loaded(); ReloadSelected(); };
        LoadFabric.StateChanged += (_, _, _) => { Fabric_Loaded(); ReloadSelected(); };
        LoadFabricApi.StateChanged += (_, _, _) => { FabricApi_Loaded(); ReloadSelected(); };
        LoadLegacyFabric.StateChanged += (_, _, _) => { LegacyFabric_Loaded(); ReloadSelected(); };
        LoadLegacyFabricApi.StateChanged += (_, _, _) => { LegacyFabricApi_Loaded(); ReloadSelected(); };
        LoadOptiFabric.StateChanged += (_, _, _) => { OptiFabric_Loaded(); ReloadSelected(); };
        LoadLabyMod.StateChanged += (_, _, _) => { LabyMod_Loaded(); ReloadSelected(); };
        PageExit += () => isInSelectPage = false;
    }

    private void Init()
    {
        PanBack.ScrollToHome();

        GetCurrentInfo();

        var needRefresh = lastVersionName is null || (lastVersionName ?? "") != (_vanillaName ?? "");
        lastVersionName = _vanillaName;

        ModDownload.dlOptiFineListLoader.Start(isForceRestart: needRefresh);
        ModDownload.dlLiteLoaderListLoader.Start(isForceRestart: needRefresh);
        ModDownload.dlFabricListLoader.Start(isForceRestart: needRefresh);
        ModDownload.dlNeoForgeListLoader.Start(isForceRestart: needRefresh);
        ModDownload.dlCleanroomListLoader.Start(isForceRestart: needRefresh);
        ModDownload.dlLabyModListLoader.Start(isForceRestart: needRefresh);
        ModDownload.dlLegacyFabricListLoader.Start(isForceRestart: needRefresh);
        ModDownload.dlFabricApiLoader.Start(isForceRestart: needRefresh);
        ModDownload.dlLegacyFabricApiLoader.Start(isForceRestart: needRefresh);
        ModDownload.dlOptiFabricLoader.Start(isForceRestart: needRefresh);

        // 非重复加载部分
        if (isLoad)
        {
            ReloadSelected();
            return;
        }
        isLoad = true;

        ModDownloadLib.McDownloadForgeRecommendedRefresh();

        LoadOptiFine.State = ModDownload.dlOptiFineListLoader;
        LoadLiteLoader.State = ModDownload.dlLiteLoaderListLoader;
        LoadFabric.State = ModDownload.dlFabricListLoader;
        LoadFabricApi.State = ModDownload.dlFabricApiLoader;
        LoadNeoForge.State = ModDownload.dlNeoForgeListLoader;
        LoadCleanroom.State = ModDownload.dlCleanroomListLoader;
        LoadOptiFabric.State = ModDownload.dlOptiFabricLoader;
        LoadLabyMod.State = ModDownload.dlLabyModListLoader;
        LoadLegacyFabric.State = ModDownload.dlLegacyFabricListLoader;
        LoadLegacyFabricApi.State = ModDownload.dlLegacyFabricApiLoader;
    }

    #region 安装

    /// <summary>
    ///     根据游戏运行状态刷新"开始修改/重置"按钮的可用性。
    ///     游戏运行期间禁止修改或重置实例（防止文件被游戏占用导致崩溃），
    ///     并显示提示 Tooltip。
    /// </summary>
    private void _RefreshStartButtonState()
    {
        var blockedByGame = ModVideoBack.IsGaming;
        BtnSelectStart.IsEnabled = !blockedByGame;
        BtnSelectStart.ToolTip = blockedByGame ? Lang.Text("Instance.Install.ToolTip.GameRunning") : null;
    }

    private void BtnSelectStart_Click(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        // Quilt 实例无法通过安装管线重装/修改（已移除 Quilt 安装支持）
        if (PageInstanceLeft.McInstance.Info.HasQuilt)
        {
            HintService.Hint(Lang.Text("Instance.Overall.Reset.QuiltUnsupported"));
            return;
        }

        // 确认版本隔离
        if (selectedLoaderName is not null &&
            (Config.Launch.IndieSolutionV2 == 0 ||
             Config.Launch.IndieSolutionV2 == 2))
            if (ModMain.MyMsgBox(
                    Lang.Text("Download.Install.InstanceIsolation.Warning.Message"), Lang.Text("Download.Install.InstanceIsolation.Warning.Title"), Lang.Text("Download.Install.InstanceIsolation.Warning.Cancel"), Lang.Text("Download.Install.InstanceIsolation.Warning.Continue")) == 1)
                return;

        if (_installAction == InstallAction.Reset)
            if (ModMain.MyMsgBox(
                    Lang.Text("Instance.Install.Reset.Message"),
                    Lang.Text("Instance.Install.Reset.Title"),
                    Lang.Text("Common.Action.Continue"),
                    Lang.Text("Common.Action.Cancel")
                ) == 2)
                return;

        // 删除 LabyMod Neo 文件
        if ((PageInstanceLeft.McInstance.PathIndie ?? "") != (PageInstanceLeft.McInstance.PathInstance ?? "") &&
            PageInstanceLeft.McInstance.Info.HasLabyMod)
            Directory.Delete(System.IO.Path.Combine(PageInstanceLeft.McInstance.PathIndie, "labymod-neo"), true);
        // 备份实例核心文件
        ModBase.CopyFile(PageInstanceLeft.McInstance.PathInstance + PageInstanceLeft.McInstance.Name + ".json",
            PageInstanceLeft.McInstance.PathInstance + @"PCLInstallBackups\" + PageInstanceLeft.McInstance.Name + ".json");
        if (File.Exists(PageInstanceLeft.McInstance.PathInstance + PageInstanceLeft.McInstance.Name + ".jar"))
            ModBase.CopyFile(PageInstanceLeft.McInstance.PathInstance + PageInstanceLeft.McInstance.Name + ".jar",
                PageInstanceLeft.McInstance.PathInstance + @"PCLInstallBackups\" + PageInstanceLeft.McInstance.Name +
                ".jar");
        // 确认独立 API (如 Fabric API 等) 是否需要被修改
        if (selectedFabricApi?.Equals(_currentFabricApi) == true)
            selectedFabricApi = null;
        if (selectedLegacyFabricApi?.Equals(_currentLegacyFabricApi) == true)
            selectedLegacyFabricApi = null;
        if (selectedOptiFabric?.Equals(_currentOptiFabric) == true)
            selectedOptiFabric = null;
        // 提交安装申请
        var request = new ModDownloadLib.McInstallRequest
        {
            targetInstanceName = PageInstanceLeft.McInstance.Name,
            targetInstanceFolder = $@"{ModFolder.mcFolderSelected}versions\{PageInstanceLeft.McInstance.Name}\",
            minecraftJson = _vanillaData?["url"].ToString(),
            minecraftName = _vanillaName,
            optiFineEntry = selectedOptiFine,
            forgeEntry = selectedForge,
            neoForgeEntry = selectedNeoForge,
            neoForgeVersion = selectedNeoForgeVersion,
            cleanroomEntry = selectedCleanroom,
            cleanroomVersion = selectedCleanroomVersion,
            fabricVersion = selectedFabric,
            fabricApi = selectedFabricApi,
            optiFabric = selectedOptiFabric,
            liteLoaderEntry = selectedLiteLoader,
            labyModChannel = selectedLabyModChannel,
            labyModCommitRef = selectedLabyModCommitRef,
            legacyFabricVersion = selectedLegacyFabric,
            legacyFabricApi = selectedLegacyFabricApi
        };
        BtnSelectStart.IsEnabled = false;
        if (!ModDownloadLib.McInstall(request, _installAction == InstallAction.Modify ? Lang.Text("Instance.Install.Action.ModifyLabel") : Lang.Text("Common.Action.Reset")))
            return;
        // 删除旧的独立 API 文件
        if (selectedFabricApi is not null && _currentFabricApiPath is not null)
            File.Delete(_currentFabricApiPath);
        if (selectedLegacyFabricApi is not null && _currentLegacyFabricApiPath is not null)
            File.Delete(_currentLegacyFabricApiPath);
        if (selectedOptiFabric is not null && _currentOptiFabricPath is not null)
            File.Delete(_currentOptiFabricPath);
        // 返回主页
        ModMain.frmMain.PageChange(new FormMain.PageStackData { page = FormMain.PageType.Launch });
    }

    #endregion

    private string GetLoaderError(MyLoading loader)
    {
        if (loader is null || !loader.State.IsLoader)
            return Lang.Text("Download.Install.State.Getting");
        switch (loader.State.LoadingState)
        {
            case MyLoading.MyLoadingState.Run:
            {
                return Lang.Text("Download.Install.State.Getting");
            }
            case MyLoading.MyLoadingState.Error:
            {
                var message = ((ModLoader.LoaderBase)loader.State).Error.Message;
                return message == Lang.Text("Download.Install.State.NoVersion") ? Lang.Text("Download.Install.State.NoVersion") : Lang.Text("Download.Install.State.GetFailed", message);
            }
            case MyLoading.MyLoadingState.Unloaded:
            {
                return Lang.Text("Download.Install.State.UnknownUnloaded");
            }

            default:
            {
                return null;
            }
        }
    }

    #region 页面切换

    // 页面切换动画
    public bool isInSelectPage;
    private bool isFirstLoaded;

    private void EnterSelectPage()
    {
        if (isInSelectPage)
            return;
        isInSelectPage = true;

        disabledPageAnimControls.Remove(BtnSelectStart);
        BtnSelectStart.Show = true;
        autoSelectedFabricApi = false;
        autoSelectedOptiFabric = false;
        PanSelect.Visibility = Visibility.Visible;
        PanSelect.IsHitTestVisible = true;
        PanMinecraft.IsHitTestVisible = false;
        PanBack.IsHitTestVisible = false;
        PanBack.ScrollToHome();

        CardMinecraft.IsSwapped = true;
        CardOptiFine.IsSwapped = true;
        CardLiteLoader.IsSwapped = true;
        CardForge.IsSwapped = true;
        CardNeoForge.IsSwapped = true;
        CardCleanroom.IsSwapped = true;
        CardFabric.IsSwapped = true;
        CardFabricApi.IsSwapped = true;
        CardOptiFabric.IsSwapped = true;
        CardLabyMod.IsSwapped = true;
        CardLegacyFabric.IsSwapped = true;
        CardLegacyFabricApi.IsSwapped = true;

        if (!(bool)States.Hint.InstallPageBack)
        {
            States.Hint.InstallPageBack = true;
            HintService.Hint(Lang.Text("Download.Install.Hint.MinecraftBack"));
        }

        // 如果在选择页面按了刷新键，选择页的东西可能会由于动画被隐藏，但不会由于加载结束而再次显示，因此这里需要手动恢复
        foreach (var Card in GetAllAnimControls(PanSelect))
        {
            Card.Opacity = 1d;
            Card.RenderTransform = new TranslateTransform();
        }

        // 启动 Forge 加载
        if (McInstanceInfo.IsFormatFit(_vanillaName))
        {
            var forgeLoader =
                new ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>>(
                    "DlForgeVersion " + _vanillaName, ModDownload.DlForgeVersionMain);
            LoadForge.State = forgeLoader;
            forgeLoader.Start(_vanillaName);
        }

        // 启动 Fabric API、Legacy Fabric API、OptiFabric 加载
        ModDownload.dlFabricApiLoader.Start();
        ModDownload.dlLegacyFabricApiLoader.Start();
        ModDownload.dlOptiFabricLoader.Start();

        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaOpacity(PanMinecraft, -PanMinecraft.Opacity, 100, 10),
            ModAnimation.AaCode(() =>
            {
                PanBack.ScrollToHome();
                OptiFine_Loaded();
                LiteLoader_Loaded();
                Forge_Loaded();
                NeoForge_Loaded();
                Cleanroom_Loaded();
                Fabric_Loaded();
                LegacyFabric_Loaded();
                FabricApi_Loaded();
                LegacyFabricApi_Loaded();
                LabyMod_Loaded();
                OptiFabric_Loaded();
                ReloadSelected();
            }, after: true),
            ModAnimation.AaOpacity(PanSelect, 1d - PanSelect.Opacity, 250, 150),
            ModAnimation.AaCode(() =>
            {
                PanMinecraft.Visibility = Visibility.Collapsed;
                PanBack.IsHitTestVisible = true;
                // 初始化 Binding
                if (isFirstLoaded)
                    return;
                isFirstLoaded = true;
                BtnOptiFineClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardOptiFine.MainTextBlock, Mode = BindingMode.OneWay });
                BtnLiteLoaderClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardLiteLoader.MainTextBlock, Mode = BindingMode.OneWay });
                BtnForgeClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardForge.MainTextBlock, Mode = BindingMode.OneWay });
                BtnLegacyFabricClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardLegacyFabric.MainTextBlock, Mode = BindingMode.OneWay });
                BtnNeoForgeClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardNeoForge.MainTextBlock, Mode = BindingMode.OneWay });
                BtnCleanroomClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardCleanroom.MainTextBlock, Mode = BindingMode.OneWay });
                BtnFabricClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardFabric.MainTextBlock, Mode = BindingMode.OneWay });
                BtnLegacyFabricApiClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground")
                        { Source = CardLegacyFabricApi.MainTextBlock, Mode = BindingMode.OneWay });
                BtnFabricApiClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardFabricApi.MainTextBlock, Mode = BindingMode.OneWay });
                BtnLabyModClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardLabyMod.MainTextBlock, Mode = BindingMode.OneWay });
                BtnOptiFabricClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardOptiFabric.MainTextBlock, Mode = BindingMode.OneWay });
            }, after: true)
        }, "FrmInstanceInstall SelectPageSwitch", true);
    }

    public void ExitSelectPage()
    {
        if (!isInSelectPage)
            return;
        isInSelectPage = false;

        LoadMinecraft_OnFinish();

        disabledPageAnimControls.Add(BtnSelectStart);
        BtnSelectStart.Show = false;

        ClearSelected(); // 清除已选择项
        PanMinecraft.Visibility = Visibility.Visible;
        PanSelect.IsHitTestVisible = false;
        PanMinecraft.IsHitTestVisible = true;
        PanBack.IsHitTestVisible = false;
        PanBack.ScrollToHome();

        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaOpacity(PanSelect, -PanSelect.Opacity, 90, 10),
            ModAnimation.AaCode(() => PanBack.ScrollToHome(), after: true),
            ModAnimation.AaOpacity(PanMinecraft, 1d - PanMinecraft.Opacity, 150, 100),
            ModAnimation.AaCode(() =>
            {
                PanSelect.Visibility = Visibility.Collapsed;
                PanBack.IsHitTestVisible = true;
            }, after: true)
        }, "FrmInstanceInstall SelectPageSwitch");
    }

    // 页面切换触发
    public void MinecraftSelected(MyListItem sender, MouseButtonEventArgs e)
    {
        _vanillaName = sender.Title;
        _vanillaData = (JsonObject)sender.Tag;
        _vanillaIcon = sender.Logo;
        EnterSelectPage();
    }

    private void CardMinecraft_PreviewSwap(object sender, ModBase.RouteEventArgs e)
    {
        ExitSelectPage();
        e.handled = true;
    }

    #endregion

    #region 选择

    // Minecraft
    private string? _vanillaName;
    private JsonObject? _vanillaData;
    private string? _vanillaIcon;
    private int VanillaDrop => McInstanceInfo.VersionToDrop(_vanillaName, true);

    // OptiFine
    private ModDownload.DlOptiFineListEntry? selectedOptiFine;

    /// <summary>
    ///     选定的 Mod Loader 名称，内容应为 Forge / NeoForge / Fabric / Cleanroom / LabyMod / LegacyFabric
    /// </summary>
    private string? selectedLoaderName;

    /// <summary>
    ///     选定的 Mod Loader API 名称，内容应为 Fabric API
    /// </summary>
    private string? selectedAPIName;

    // LiteLoader
    private ModDownload.DlLiteLoaderListEntry? selectedLiteLoader;

    // Forge
    private ModDownload.DlForgeVersionEntry? selectedForge;

    // Cleanroom
    private ModDownload.DlCleanroomListEntry? selectedCleanroom;
    private string? selectedCleanroomVersion;

    // NeoForge
    private ModDownload.DlNeoForgeListEntry? selectedNeoForge;
    private string? selectedNeoForgeVersion;

    // Fabric
    private string? selectedFabric;

    // FabricApi
    private ModComp.CompFile? selectedFabricApi;

    // LegacyFabric
    private string? selectedLegacyFabric;

    // Legacy FabricApi
    private ModComp.CompFile? selectedLegacyFabricApi;

    // LabyMod
    private string? selectedLabyModChannel;
    private string? selectedLabyModCommitRef;
    private string? selectedLabyModVersion;

    // OptiFabric
    private ModComp.CompFile? selectedOptiFabric;

    private bool _ReloadSelected_Ongoing; // #3742 中，LoadOptiFineGetError 会初始化 LoadOptiFine，触发事件 LoadOptiFine.StateChanged，导致再次调用 SelectReload

    /// <summary>
    ///     重载已选择的项目的显示。
    /// </summary>
    private void ReloadSelected()
    {
        if (_vanillaName is null || _ReloadSelected_Ongoing)
            return;
        _ReloadSelected_Ongoing = true;
        try
        {
        var selectedInfo = GetSelectInfo();
        // 主预览
        ItemSelect.Title = PageInstanceLeft.McInstance.Name;
        ItemSelect.Logo = GetSelectLogo();
        _RefreshStartButtonState();
        if ((selectedInfo ?? "") == (currentInfo ?? ""))
        {
            ItemSelect.Info = selectedInfo;
            BtnSelectStart.Text = Lang.Text("Instance.Install.Action.StartReset");
            _installAction = InstallAction.Reset;
            BtnSelectStart.SvgIcon = "lucide/rotate-ccw";
        }
        else
        {
            ItemSelect.Info = currentInfo + " → " + selectedInfo;
            BtnSelectStart.Text = Lang.Text("Instance.Install.Action.StartModify");
            _installAction = InstallAction.Modify;
            BtnSelectStart.SvgIcon = "lucide/pencil";
        }

        // Minecraft
        ImgMinecraft.Source = new MyBitmap(_vanillaIcon);
        LabMinecraft.Text = _vanillaName;
        LabMinecraft.Foreground = ThemeManager.colorGray1;
        // OptiFine
        if (!McVersionComparer.CompareVersionGe(_vanillaName, "1.7.2"))
        {
            CardOptiFine.Visibility = Visibility.Collapsed;
        }
        else
        {
            CardOptiFine.Visibility = Visibility.Visible;
            var optiFineError = LoadOptiFineGetError();
            CardOptiFine.MainSwap.Visibility = optiFineError is null ? Visibility.Visible : Visibility.Collapsed;
            if (optiFineError is not null)
                CardOptiFine.IsSwapped = true;
            SetPanelVisibility(PanOptiFineInfo, CardOptiFine.IsSwapped);
            if (selectedOptiFine is null)
            {
                BtnOptiFineClear.Visibility = Visibility.Collapsed;
                ImgOptiFine.Visibility = Visibility.Collapsed;
                LabOptiFine.Text = optiFineError ?? Lang.Text("Download.Install.State.CanAdd");
                LabOptiFine.Foreground = ThemeManager.colorGray4;
            }
            else
            {
                BtnOptiFineClear.Visibility = Visibility.Visible;
                ImgOptiFine.Visibility = Visibility.Visible;
                LabOptiFine.Text = selectedOptiFine.DisplayName.Replace(_vanillaName + " ", "");
                LabOptiFine.Foreground = ThemeManager.colorGray1;
            }
        }

        // LiteLoader
        if (!McInstanceInfo.IsFormatFit(_vanillaName)
            || !McVersionComparer.CompareVersionGe(_vanillaName, "1.5.2")
            || !McVersionComparer.CompareVersionGe("1.12.2", _vanillaName))
        {
            CardLiteLoader.Visibility = Visibility.Collapsed;
        }
        else
        {
            CardLiteLoader.Visibility = Visibility.Visible;
            var liteLoaderError = LoadLiteLoaderGetError();
            CardLiteLoader.MainSwap.Visibility = liteLoaderError is null ? Visibility.Visible : Visibility.Collapsed;
            if (liteLoaderError is not null)
                CardLiteLoader.IsSwapped = true; // 例如在同时展开卡片时选择了不兼容项则强制折叠
            SetPanelVisibility(PanLiteLoaderInfo, CardLiteLoader.IsSwapped);
            if (selectedLiteLoader is null)
            {
                BtnLiteLoaderClear.Visibility = Visibility.Collapsed;
                ImgLiteLoader.Visibility = Visibility.Collapsed;
                LabLiteLoader.Text = liteLoaderError ?? Lang.Text("Download.Install.State.CanAdd");
                LabLiteLoader.Foreground = ThemeManager.colorGray4;
            }
            else
            {
                BtnLiteLoaderClear.Visibility = Visibility.Visible;
                ImgLiteLoader.Visibility = Visibility.Visible;
                LabLiteLoader.Text = selectedLiteLoader.Inherit;
                LabLiteLoader.Foreground = ThemeManager.colorGray1;
            }
        }

        // Forge
        if (!McInstanceInfo.IsFormatFit(_vanillaName)
            || !McVersionComparer.CompareVersionGe(_vanillaName, "1.1"))
        {
            CardForge.Visibility = Visibility.Collapsed;
        }
        else
        {
            CardForge.Visibility = Visibility.Visible;
            var forgeError = LoadForgeGetError();
            CardForge.MainSwap.Visibility = forgeError is null ? Visibility.Visible : Visibility.Collapsed;
            if (forgeError is not null)
                CardForge.IsSwapped = true;
            SetPanelVisibility(PanForgeInfo, CardForge.IsSwapped);
            if (selectedForge is null)
            {
                BtnForgeClear.Visibility = Visibility.Collapsed;
                ImgForge.Visibility = Visibility.Collapsed;
                LabForge.Text = forgeError ?? Lang.Text("Download.Install.State.CanAdd");
                LabForge.Foreground = ThemeManager.colorGray4;
            }
            else
            {
                BtnForgeClear.Visibility = Visibility.Visible;
                ImgForge.Visibility = Visibility.Visible;
                LabForge.Text = selectedForge.VersionName;
                LabForge.Foreground = ThemeManager.colorGray1;
            }
        }

        // Cleanroom
        if (_vanillaName == "1.12.2")
        {
            CardCleanroom.Visibility = Visibility.Visible;
            var cleanroomError = LoadCleanroomGetError();
            CardCleanroom.MainSwap.Visibility = cleanroomError is null ? Visibility.Visible : Visibility.Collapsed;
            if (cleanroomError is not null)
                CardCleanroom.IsSwapped = true;
            SetPanelVisibility(PanCleanroomInfo, CardCleanroom.IsSwapped);
            if (selectedCleanroom is null)
            {
                BtnCleanroomClear.Visibility = Visibility.Collapsed;
                ImgCleanroom.Visibility = Visibility.Collapsed;
                LabCleanroom.Text = cleanroomError ?? Lang.Text("Download.Install.State.CanAdd");
                LabCleanroom.Foreground = ThemeManager.colorGray4;
            }
            else
            {
                BtnCleanroomClear.Visibility = Visibility.Visible;
                ImgCleanroom.Visibility = Visibility.Visible;
                LabCleanroom.Text = selectedCleanroom.VersionName;
                LabCleanroom.Foreground = ThemeManager.colorGray1;
            }
        }
        else
        {
            CardCleanroom.Visibility = Visibility.Collapsed;
        }

        // NeoForge
        if (!McVersionComparer.CompareVersionGe(_vanillaName, "1.20.1"))
        {
            CardNeoForge.Visibility = Visibility.Collapsed;
        }
        else
        {
            CardNeoForge.Visibility = Visibility.Visible;
            var neoForgeError = LoadNeoForgeGetError();
            CardNeoForge.MainSwap.Visibility = neoForgeError is null ? Visibility.Visible : Visibility.Collapsed;
            if (neoForgeError is not null)
                CardNeoForge.IsSwapped = true;
            SetPanelVisibility(PanNeoForgeInfo, CardNeoForge.IsSwapped);
            if (selectedNeoForge is null)
            {
                BtnNeoForgeClear.Visibility = Visibility.Collapsed;
                ImgNeoForge.Visibility = Visibility.Collapsed;
                LabNeoForge.Text = neoForgeError ?? Lang.Text("Download.Install.State.CanAdd");
                LabNeoForge.Foreground = ThemeManager.colorGray4;
            }
            else
            {
                BtnNeoForgeClear.Visibility = Visibility.Visible;
                ImgNeoForge.Visibility = Visibility.Visible;
                LabNeoForge.Text = selectedNeoForge.VersionName;
                LabNeoForge.Foreground = ThemeManager.colorGray1;
            }
        }

        // Fabric
        if (VanillaDrop < 130
            || (VanillaDrop == 130 && !McVersionComparer.CompareVersionGe(_vanillaName, "18w43b")))
        {
            CardFabric.Visibility = Visibility.Collapsed;
        }
        else
        {
            CardFabric.Visibility = Visibility.Visible;
            var fabricError = LoadFabricGetError();
            CardFabric.MainSwap.Visibility = fabricError is null ? Visibility.Visible : Visibility.Collapsed;
            if (fabricError is not null)
                CardFabric.IsSwapped = true;
            SetPanelVisibility(PanFabricInfo, CardFabric.IsSwapped);
            if (selectedFabric is null)
            {
                BtnFabricClear.Visibility = Visibility.Collapsed;
                ImgFabric.Visibility = Visibility.Collapsed;
                LabFabric.Text = fabricError ?? Lang.Text("Download.Install.State.CanAdd");
                LabFabric.Foreground = ThemeManager.colorGray4;
            }
            else
            {
                BtnFabricClear.Visibility = Visibility.Visible;
                ImgFabric.Visibility = Visibility.Visible;
                LabFabric.Text = selectedFabric.Replace("+build", "");
                LabFabric.Foreground = ThemeManager.colorGray1;
            }
        }

        // FabricApi
        if (selectedFabric is null)
        {
            CardFabricApi.Visibility = Visibility.Collapsed;
        }
        else
        {
            CardFabricApi.Visibility = Visibility.Visible;
            var fabricApiError = LoadFabricApiGetError();
            CardFabricApi.MainSwap.Visibility = fabricApiError is null ? Visibility.Visible : Visibility.Collapsed;
            if (fabricApiError is not null || selectedFabric is null)
                CardFabricApi.IsSwapped = true;
            SetPanelVisibility(PanFabricApiInfo, CardFabricApi.IsSwapped);
            if (selectedFabricApi is null)
            {
                BtnFabricApiClear.Visibility = Visibility.Collapsed;
                ImgFabricApi.Visibility = Visibility.Collapsed;
                LabFabricApi.Text = fabricApiError ?? Lang.Text("Download.Install.State.CanAdd");
                LabFabricApi.Foreground = ThemeManager.colorGray4;
            }
            else
            {
                BtnFabricApiClear.Visibility = Visibility.Visible;
                ImgFabricApi.Visibility = Visibility.Visible;
                LabFabricApi.Text = selectedFabricApi.DisplayName.Split("]")[1].Replace("Fabric API ", "")
                    .Replace(" build ", ".").Split("+").First().Trim();
                LabFabricApi.Foreground = ThemeManager.colorGray1;
            }
        }

        // LegacyFabric
        if (VanillaDrop < 30 || VanillaDrop > 130)
        {
            CardLegacyFabric.Visibility = Visibility.Collapsed;
        }
        else
        {
            CardLegacyFabric.Visibility = Visibility.Visible;
            var legacyFabricError = LoadLegacyFabricGetError();
            CardLegacyFabric.MainSwap.Visibility =
                legacyFabricError is null ? Visibility.Visible : Visibility.Collapsed;
            if (legacyFabricError is not null)
                CardLegacyFabric.IsSwapped = true;
            SetPanelVisibility(PanLegacyFabricInfo, CardLegacyFabric.IsSwapped);
            if (selectedLegacyFabric is null)
            {
                BtnLegacyFabricClear.Visibility = Visibility.Collapsed;
                ImgLegacyFabric.Visibility = Visibility.Collapsed;
                LabLegacyFabric.Text = legacyFabricError ?? Lang.Text("Download.Install.State.CanAdd");
                LabLegacyFabric.Foreground = ThemeManager.colorGray4;
            }
            else
            {
                BtnLegacyFabricClear.Visibility = Visibility.Visible;
                ImgLegacyFabric.Visibility = Visibility.Visible;
                LabLegacyFabric.Text = selectedLegacyFabric.Replace("+build", "");
                LabLegacyFabric.Foreground = ThemeManager.colorGray1;
            }
        }

        // LegacyFabricApi
        if (selectedLegacyFabric is null)
        {
            CardLegacyFabricApi.Visibility = Visibility.Collapsed;
        }
        else
        {
            CardLegacyFabricApi.Visibility = Visibility.Visible;
            var legacyFabricApiError = LoadLegacyFabricApiGetError();
            CardLegacyFabricApi.MainSwap.Visibility =
                legacyFabricApiError is null ? Visibility.Visible : Visibility.Collapsed;
            if (legacyFabricApiError is not null || selectedLegacyFabric is null)
                CardLegacyFabricApi.IsSwapped = true;
            SetPanelVisibility(PanLegacyFabricApiInfo, CardLegacyFabricApi.IsSwapped);
            if (selectedLegacyFabricApi is null)
            {
                BtnLegacyFabricApiClear.Visibility = Visibility.Collapsed;
                ImgLegacyFabricApi.Visibility = Visibility.Collapsed;
                LabLegacyFabricApi.Text = legacyFabricApiError ?? Lang.Text("Download.Install.State.CanAdd");
                LabLegacyFabricApi.Foreground = ThemeManager.colorGray4;
            }
            else
            {
                BtnLegacyFabricApiClear.Visibility = Visibility.Visible;
                ImgLegacyFabricApi.Visibility = Visibility.Visible;
                LabLegacyFabricApi.Text = selectedLegacyFabricApi.DisplayName.Replace("Legacy Fabric API ", "");
                LabLegacyFabricApi.Foreground = ThemeManager.colorGray1;
            }
        }

        // LabyMod
        if (!McInstanceInfo.IsFormatFit(_vanillaName)
            || !McVersionComparer.CompareVersionGe(_vanillaName, "1.8.9"))
        {
            CardLabyMod.Visibility = Visibility.Collapsed;
        }
        else
        {
            CardLabyMod.Visibility = Visibility.Visible;
            var labyModError = LoadLabyModGetError();
            CardLabyMod.MainSwap.Visibility = labyModError is null ? Visibility.Visible : Visibility.Collapsed;
            if (labyModError is not null)
                CardLabyMod.IsSwapped = true;
            SetPanelVisibility(PanLabyModInfo, CardLabyMod.IsSwapped);
            if (selectedLabyModVersion is null)
            {
                BtnLabyModClear.Visibility = Visibility.Collapsed;
                ImgLabyMod.Visibility = Visibility.Collapsed;
                LabLabyMod.Text = labyModError ?? Lang.Text("Download.Install.State.CanAdd");
                LabLabyMod.Foreground = ThemeManager.colorGray4;
            }
            else
            {
                BtnLabyModClear.Visibility = Visibility.Visible;
                ImgLabyMod.Visibility = Visibility.Visible;
                LabLabyMod.Text = selectedLabyModVersion;
                LabLabyMod.Foreground = ThemeManager.colorGray1;
            }
        }

        // OptiFabric
        if (selectedFabric is null || selectedOptiFine is null)
        {
            CardOptiFabric.Visibility = Visibility.Collapsed;
        }
        else
        {
            CardOptiFabric.Visibility = Visibility.Visible;
            var optiFabricError = LoadOptiFabricGetError();
            CardOptiFabric.MainSwap.Visibility = optiFabricError is null ? Visibility.Visible : Visibility.Collapsed;
            if (optiFabricError is not null || selectedFabric is null)
                CardOptiFabric.IsSwapped = true;
            SetPanelVisibility(PanOptiFabricInfo, CardOptiFabric.IsSwapped);
            if (selectedOptiFabric is null)
            {
                BtnOptiFabricClear.Visibility = Visibility.Collapsed;
                ImgOptiFabric.Visibility = Visibility.Collapsed;
                LabOptiFabric.Text = optiFabricError ?? Lang.Text("Download.Install.State.CanAdd");
                LabOptiFabric.Foreground = ThemeManager.colorGray4;
            }
            else
            {
                BtnOptiFabricClear.Visibility = Visibility.Visible;
                ImgOptiFabric.Visibility = Visibility.Visible;
                LabOptiFabric.Text = selectedOptiFabric.DisplayName.ToLower().Replace("optifabric-", "")
                    .Replace(".jar", "").Trim().TrimStart('v');
                LabOptiFabric.Foreground = ThemeManager.colorGray1;
            }
        }

        // 主警告
        if (selectedFabric is not null && selectedFabricApi is null)
            HintFabricAPI.Visibility = Visibility.Visible;
        else
            HintFabricAPI.Visibility = Visibility.Collapsed;
        if (selectedLegacyFabric is not null && selectedLegacyFabricApi is null)
            HintLegacyFabricAPI.Visibility = Visibility.Visible;
        else
            HintLegacyFabricAPI.Visibility = Visibility.Collapsed;

        if ((selectedFabric is not null || selectedLegacyFabric is not null) && selectedOptiFine is not null &&
            selectedOptiFabric is null)
        {
            if (VanillaDrop >= 140 && VanillaDrop <= 150)
            {
                HintOptiFabric.Visibility = Visibility.Collapsed;
                HintLegacyOptiFabric.Visibility = Visibility.Collapsed;
                HintOptiFabricOld.Visibility = Visibility.Visible;
            }
            else if (selectedLegacyFabric is not null)
            {
                HintOptiFabric.Visibility = Visibility.Collapsed;
                HintLegacyOptiFabric.Visibility = Visibility.Visible;
                HintOptiFabricOld.Visibility = Visibility.Collapsed;
            }
            else
            {
                HintOptiFabric.Visibility = Visibility.Visible;
                HintOptiFabricOld.Visibility = Visibility.Collapsed;
                HintLegacyOptiFabric.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            HintOptiFabric.Visibility = Visibility.Collapsed;
            HintOptiFabricOld.Visibility = Visibility.Collapsed;
            HintLegacyOptiFabric.Visibility = Visibility.Collapsed;
        }

        if (VanillaDrop >= 160 && selectedOptiFine is not null &&
            (selectedForge is not null || selectedFabric is not null))
            HintModOptiFine.Visibility = Visibility.Visible;
        else
            HintModOptiFine.Visibility = Visibility.Collapsed;
        // 结束
        }
        finally
        {
            _ReloadSelected_Ongoing = false;
        }
    }

    /// <summary>
    ///     清空已选择的项目。
    /// </summary>
    private void ClearSelected()
    {
        _vanillaName = null;
        _vanillaData = null;
        _vanillaIcon = null;
        selectedOptiFine = null;
        selectedLiteLoader = null;
        selectedLoaderName = null;
        selectedAPIName = null;
        selectedForge = null;
        selectedNeoForge = null;
        selectedNeoForgeVersion = null;
        selectedCleanroom = null;
        selectedCleanroomVersion = null;
        selectedFabric = null;
        selectedFabricApi = null;
        selectedOptiFabric = null;
        selectedLabyModCommitRef = null;
        selectedLabyModVersion = null;
        selectedLabyModChannel = null;
        selectedLegacyFabric = null;
        selectedLegacyFabricApi = null;
    }

    // 信息栏动画
    private void SetPanelVisibility(Grid panel, bool visible)
    {
        if (Equals(panel.Tag, visible.ToString()))
            return;
        panel.Tag = visible.ToString();
        if (visible)
            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaTranslateY(panel, -((TranslateTransform)panel.RenderTransform).Y, 150,
                        ease: new ModAnimation.AniEaseOutFluent()),
                    ModAnimation.AaOpacity(panel, 1d - panel.Opacity, 60)
                }, "PageDownloadInstall Visibility " + panel.Name);
        else
            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaTranslateY(panel, 6d - ((TranslateTransform)panel.RenderTransform).Y, 60),
                    ModAnimation.AaOpacity(panel, -panel.Opacity, 60)
                }, "PageDownloadInstall Visibility " + panel.Name);
    }

    /// <summary>
    ///     获取实例图标。
    /// </summary>
    private string GetSelectLogo()
    {
        if (selectedFabric is not null) return "pack://application:,,,/images/Blocks/Fabric.png";

        if (selectedLegacyFabric is not null) return "pack://application:,,,/images/Blocks/Fabric.png";

        if (selectedForge is not null) return "pack://application:,,,/images/Blocks/Anvil.png";

        if (selectedNeoForge is not null) return "pack://application:,,,/images/Blocks/NeoForge.png";

        if (selectedLiteLoader is not null) return "pack://application:,,,/images/Blocks/Egg.png";

        if (selectedOptiFine is not null) return "pack://application:,,,/images/Blocks/GrassPath.png";

        if (selectedCleanroom is not null) return "pack://application:,,,/images/Blocks/Cleanroom.png";

        if (selectedLabyModVersion is not null) return "pack://application:,,,/images/Blocks/LabyMod.png";

        return _vanillaIcon;
    }

    /// <summary>
    ///     获取实例描述信息。
    /// </summary>
    private string GetSelectInfo()
    {
        var parts = new List<string>
        {
            _vanillaName
        };

        var loaderInfos = new (string NameKey, string? Version)[]
        {
            ("Common.Installation.Fabric", selectedFabric?.Replace("+build", "")),
            ("Common.Installation.LegacyFabric", selectedLegacyFabric),
            ("Common.Installation.Forge", selectedForge?.VersionName),
            ("Common.Installation.NeoForge",
                selectedNeoForge?.VersionName ?? VersionOrNull(selectedNeoForgeVersion)),
            ("Common.Installation.Cleanroom",
                selectedCleanroom?.VersionName ?? VersionOrNull(selectedCleanroomVersion)),
            ("Common.Installation.LabyMod", selectedLabyModVersion),
            ("Common.Installation.OptiFine",
                selectedOptiFine?.DisplayName.Replace(_vanillaName + " ", ""))
        };

        parts.AddRange(
            loaderInfos
                .Where(info => !string.IsNullOrWhiteSpace(info.Version))
                .Select(info => $"{Lang.Text(info.NameKey)} {info.Version}")
        );

        if (selectedLiteLoader is not null) parts.Add(Lang.Text("Common.Installation.LiteLoader"));

        if (parts.Count == 1) parts.Add(Lang.Text("Instance.Install.NoExtraInstall"));

        return string.Join("  |  ", parts);
    }

    private static string? VersionOrNull<T>(T version)
    {
        return EqualityComparer<T>.Default.Equals(version, default!)
            ? null
            : version?.ToString();
    }

    #endregion

    #region 当前信息获取

    private ModComp.CompFile _currentFabricApi; // 加载完成后直接调用以提高性能
    private string _currentFabricApiPath;

    private object GetCurrentFabricApi() // 进入页面和联网加载时调用
    {
        var loaderOutput = ModDownload.dlFabricApiLoader.output;
        if (loaderOutput is null)
            return null; // 确保联网信息已加载
        var localComp = ModLocalComp.GetModLocalCompByKeywords(PageInstanceLeft.McInstance,
            new[] { "fabric-api", "fabric" }, "fabric", "api");
        if (localComp is null)
            return null;
        var result = loaderOutput.FirstOrDefault(comp => (comp.Hash ?? "") == (localComp.ModrinthHash ?? ""));
        if (result is not null)
        {
            _currentFabricApi = result;
            _currentFabricApiPath = localComp.path;
        }

        return result;
    }

    private ModComp.CompFile _currentLegacyFabricApi; // 加载完成后直接调用以提高性能
    private string _currentLegacyFabricApiPath;

    private object GetCurrentLegacyFabricApi() // 进入页面和联网加载时调用
    {
        var loaderOutput = ModDownload.dlLegacyFabricApiLoader.output;
        if (loaderOutput is null)
            return null; // 确保联网信息已加载
        var localComp = ModLocalComp.GetModLocalCompByKeywords(PageInstanceLeft.McInstance,
            new[] { "legacy-fabric-api", "legacy-fabric" }, "legacy-fabric", "api");
        if (localComp is null)
            return null;
        var result = loaderOutput.FirstOrDefault(comp => (comp.Hash ?? "") == (localComp.ModrinthHash ?? ""));
        if (result is not null)
        {
            _currentLegacyFabricApi = result;
            _currentLegacyFabricApiPath = localComp.path;
        }

        return result;
    }

    private ModComp.CompFile _currentOptiFabric;
    private string _currentOptiFabricPath;

    private object GetCurrentOptiFabric()
    {
        var loaderOutput = ModDownload.dlOptiFabricLoader.output;
        if (loaderOutput is null)
            return null;
        var localComp =
            ModLocalComp.GetModLocalCompByKeywords(PageInstanceLeft.McInstance, "optifabric", "optifabric", "opti");
        if (localComp is null)
            return null;
        var result = loaderOutput.FirstOrDefault(comp => (comp.Hash ?? "") == (localComp.ModrinthHash ?? ""));
        if (result is not null)
        {
            _currentOptiFabric = result;
            _currentOptiFabricPath = localComp.path;
        }

        return result;
    }

    // 当前信息获取
    public void GetCurrentInfo()
    {
        ClearSelected();
        _RefreshStartButtonState();
        var currentInstance = PageInstanceLeft.McInstance.Info;
        _vanillaName = currentInstance.VanillaName;
        if (currentInstance.HasLiteLoader)
            selectedLiteLoader = new ModDownload.DlLiteLoaderListEntry { Inherit = currentInstance.VanillaName };
        if (currentInstance.HasOptiFine)
            selectedOptiFine = new ModDownload.DlOptiFineListEntry
            {
                DisplayName = currentInstance.VanillaName + " " + currentInstance.OptiFine.Replace("_", " "),
                IsPreview = currentInstance.OptiFine.ContainsF("pre"), Inherit = currentInstance.VanillaName,
                NameVersion = currentInstance.VanillaName + "-OptiFine_HD_U_" + currentInstance.OptiFine
            };
        if (currentInstance.HasCleanroom)
        {
            selectedLoaderName = "Cleanroom";
            selectedAPIName = "Cleanroom";
            selectedCleanroomVersion = currentInstance.Cleanroom;
            selectedCleanroom = new ModDownload.DlCleanroomListEntry(selectedCleanroomVersion);
        }
        else if (currentInstance.HasForge)
        {
            selectedLoaderName = "Forge";
            selectedForge =
                new ModDownload.DlForgeVersionEntry(currentInstance.Forge, null, currentInstance.VanillaName)
                {
                    Category = "installer", forgeType = ModDownload.DlForgelikeEntry.ForgelikeType.Forge,
                    Inherit = currentInstance.VanillaName
                };
        }
        else if (currentInstance.HasLegacyFabric)
        {
            selectedLoaderName = "LegacyFabric";
            selectedLegacyFabric = currentInstance.LegacyFabric;
            selectedLegacyFabricApi = (ModComp.CompFile)GetCurrentLegacyFabricApi();
        }
        else if (currentInstance.HasFabric)
        {
            selectedLoaderName = "Fabric";
            selectedFabric = currentInstance.Fabric;
            selectedFabricApi = (ModComp.CompFile)GetCurrentFabricApi();
        }
        else if (currentInstance.HasLabyMod)
        {
            selectedLoaderName = "LabyMod";
            selectedLabyModVersion = currentInstance.LabyMod;
        }
        else if (currentInstance.HasNeoForge)
        {
            selectedLoaderName = "NeoForge";
            selectedNeoForgeVersion = currentInstance.NeoForge;
            selectedNeoForge = new ModDownload.DlNeoForgeListEntry(currentInstance.NeoForge)
            {
                VersionName = currentInstance.NeoForge, Inherit = currentInstance.VanillaName,
                forgeType = ModDownload.DlForgelikeEntry.ForgelikeType.NeoForge
            };
        }

        if (currentInstance.HasFabric && currentInstance.HasOptiFine)
            selectedOptiFabric = (ModComp.CompFile)GetCurrentOptiFabric();
        _vanillaIcon = "pack://application:,,,/images/Blocks/Grass.png"; // TODO: 需要判断 Icon
        currentInfo = GetSelectInfo();
        EnterSelectPage();
    }

    private string currentInfo;

    #endregion

    #region 加载器

    // 结果数据化
    private static string GetVersionTypeTitle(string key) => key switch
    {
        "正式版" => Lang.Text("Download.Version.Type.Release"),
        "预览版" => Lang.Text("Download.Version.Type.Development"),
        "远古版" => Lang.Text("Download.Version.Type.BeforeRelease"),
        "愚人节版" => Lang.Text("Download.Version.Type.AprilFools"),
        _ => key
    };

    private void LoadMinecraft_OnFinish()
    {
        ExitSelectPage(); // 返回
        do
        {
            try
            {
                var dict = new Dictionary<string, List<JsonObject>>
                {
                    { "正式版", new List<JsonObject>() }, { "预览版", new List<JsonObject>() }, { "远古版", new List<JsonObject>() },
                    { "愚人节版", new List<JsonObject>() }
                };
                var versions = (JsonArray)ModDownload.dlClientListLoader.output.Value["versions"];
                foreach (JsonObject Version in versions)
                {
                    // 确定分类
                    var type = Version["type"].ToString();
                    var versionId = Version["id"].ToString().ToLower();
                    switch (type ?? "")
                    {
                        case "release":
                        {
                            type = "正式版";
                            break;
                        }
                        case "snapshot":
                        case "pending":
                        {
                            type = "预览版";
                            // Mojang 误分类
                            if (versionId.StartsWith("1.") && !versionId.Contains("combat") &&
                                !versionId.Contains("rc") && !versionId.Contains("experimental") &&
                                !versionId.Equals("1.2") && !versionId.Contains("pre"))
                            {
                                type = "正式版";
                                Version["type"] = "release";
                            }

                            // 愚人节版本
                            switch (Version["id"].ToString().ToLower() ?? "")
                            {
                                case "2point0_blue":
                                case "2point0_red":
                                case "2point0_purple":
                                case "2.0_blue":
                                case "2.0_red":
                                case "2.0_purple":
                                case "2.0":
                                {
                                    type = "愚人节版";
                                    Version["id"] = Version["id"].ToString().Replace("point", ".");
                                    Version["type"] = "special";
                                    Version.Add("lore", McVersionClassifier.GetMcFoolName((string)Version["id"]));
                                    break;
                                }
                                case "20w14infinite":
                                case "20w14∞":
                                {
                                    type = "愚人节版";
                                    Version["id"] = "20w14∞";
                                    Version["type"] = "special";
                                    Version.Add("lore", McVersionClassifier.GetMcFoolName((string)Version["id"]));
                                    break;
                                }
                                case "3d shareware v1.34":
                                case "1.rv-pre1":
                                case "15w14a":
                                case var @case when @case == "2.0":
                                case "22w13oneblockatatime":
                                case "23w13a_or_b":
                                case "24w14potato":
                                case "25w14craftmine":
                                case "26w14a":
                                {
                                    type = "愚人节版";
                                    Version["type"] = "special";
                                    Version.Add("lore",
                                        McVersionClassifier.GetMcFoolName((string)Version["id"])); // 4/1 自动视作愚人节版
                                    break;
                                }

                                default:
                                {
                                    var releaseDate = McVersionClassifier.GetReleaseTime(Version).ToUniversalTime().AddHours(2d);
                                    if (releaseDate.Month == 4 && releaseDate.Day == 1)
                                    {
                                        type = "愚人节版";
                                        Version["type"] = "special";
                                    }

                                    break;
                                }
                            }

                            break;
                        }
                        case "special":
                        {
                            // 已被处理的愚人节版
                            type = "愚人节版";
                            break;
                        }

                        default:
                        {
                            type = "远古版";
                            break;
                        }
                    }

                    // 加入辞典
                    dict[type].Add(Version);
                }

                // 排序
                foreach (var Pair in dict.ToList())
                    dict[Pair.Key] = Pair.Value.OrderByDescending(McVersionClassifier.GetReleaseTime).ToList();
                // 清空当前
                PanMinecraft.Children.Clear();
                // 添加最新版本
                var cardInfo = new MyCard { Title = Lang.Text("Download.Version.Latest.Title"), Margin = new Thickness(0d, 15d, 0d, 15d) };
                var topestVersions = new List<JsonObject>();
                var release = (JsonObject)dict["正式版"][0].DeepClone();
                release["lore"] = Lang.Text("Download.Version.Latest.Release", Lang.Date(release["releaseTime"].ToObject<DateTime>(), "g"));
                topestVersions.Add(release);
                if (dict["正式版"][0]["releaseTime"].ToObject<DateTime>() < dict["预览版"][0]["releaseTime"].ToObject<DateTime>())
                {
                    var snapshot = (JsonObject)dict["预览版"][0].DeepClone();
                    snapshot["lore"] = Lang.Text("Download.Version.Latest.Development", Lang.Date(snapshot["releaseTime"].ToObject<DateTime>(), "g"));
                    topestVersions.Add(snapshot);
                }

                var panInfo = new StackPanel
                {
                    Margin = new Thickness(20d, MyCard.SwapedHeight, 18d, 0d),
                    VerticalAlignment = VerticalAlignment.Top, RenderTransform = new TranslateTransform(0d, 0d),
                    Tag = topestVersions
                };

                void StackInstall(StackPanel stack)
                {
                    foreach (var item in (IEnumerable)stack.Tag)
                        stack.Children.Add(ModDownloadLib.McDownloadListItem((JsonObject)item,
                            (sender, e) => MinecraftSelected((MyListItem)sender, e), false));
                }

                ;
                MyCard.StackInstall(ref panInfo, StackInstall);
                cardInfo.Children.Add(panInfo);
                PanMinecraft.Children.Insert(0, cardInfo);
                // 添加其他版本
                foreach (var Pair in dict)
                {
                    if (!Pair.Value.Any())
                        continue;
                    // 增加卡片
                    var newCard = new MyCard
                        { Title = GetVersionTypeTitle(Pair.Key) + " (" + Pair.Value.Count + ")", Margin = new Thickness(0d, 0d, 0d, 15d) };
                    var newStack = new StackPanel
                    {
                        Margin = new Thickness(20d, MyCard.SwapedHeight, 18d, 0d),
                        VerticalAlignment = VerticalAlignment.Top, RenderTransform = new TranslateTransform(0d, 0d),
                        Tag = Pair.Value
                    };
                    newCard.Children.Add(newStack);
                    newCard.SwapControl = newStack;
                    // 不能使用 AddressOf，这导致了 #535，原因完全不明，疑似是编译器 Bug
                    newCard.InstallMethod = StackInstall;
                    newCard.IsSwapped = true;
                    PanMinecraft.Children.Add(newCard);
                }

                // 自动选择版本
                if (mcVersionWaitingForSelect is null)
                    break;
                ModBase.Log("[Download] 自动选择 MC 版本：" + mcVersionWaitingForSelect);
                foreach (JsonObject Version in versions)
                {
                    if ((Version["id"].ToString() ?? "") != (mcVersionWaitingForSelect ?? ""))
                        continue;
                    var item = ModDownloadLib.McDownloadListItem(Version, (_, _) => { }, false);
                    MinecraftSelected(item, null);
                }
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    "可视化安装版本列表出错",
                    ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Instance.Install.Error.OperationFailed"));
            }
        } while (false);
    }

    /// <summary>
    ///     当 MC 版本列表加载完时，立即自动选择的版本。用于外部调用。
    /// </summary>
    public static string mcVersionWaitingForSelect = null;

    #endregion

    #region OptiFine 列表

    /// <summary>
    ///     获取 OptiFine 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
    private string LoadOptiFineGetError()
    {
        if (selectedLoaderName == "NeoForge" || selectedLoaderName == "LabyMod" || selectedLoaderName == "Cleanroom")
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedLoaderName);
        if (LoadOptiFine is null || LoadOptiFine.State.LoadingState == MyLoading.MyLoadingState.Run)
            return Lang.Text("Download.Install.State.Loading");
        if (LoadOptiFine.State.LoadingState == MyLoading.MyLoadingState.Error)
            return $"{Lang.Text("Download.Install.State.GetVersionListFailed")}{((ModLoader.LoaderBase)LoadOptiFine.State).Error.Message}";
        // 检查 Forge 1.13 - 1.14.3：全部不兼容
        if (selectedLoaderName == "Forge" && McVersionComparer.CompareVersion(_vanillaName, "1.13") >= 0 &&
            McVersionComparer.CompareVersion("1.14.3", _vanillaName) >= 0) return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedLoaderName);
        // 检查 Fabric 1.20.5+: 全部不兼容
        if (selectedFabric is not null && McVersionComparer.CompareVersion(_vanillaName, "1.20.4") > 0)
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedLoaderName);
        // 检查 Loader
        if (GetLoaderError(LoadOptiFine) is not null)
            return GetLoaderError(LoadOptiFine);
        // 检查 Forge 版本
        var hasAny = false;
        var hasRequiredVersion = false;
        foreach (var OptiFineVersion in ModDownload.dlOptiFineListLoader.output.Value)
        {
            if (!OptiFineVersion.DisplayName.StartsWith(_vanillaName + " "))
                continue; // 不是同一个大版本
            hasAny = true;
            if (selectedForge is null)
                return null; // 未选择 Forge
            if ((bool)IsOptiFineSuitForForge(OptiFineVersion, selectedForge))
                return null; // 该版本可用
            if (OptiFineVersion.RequiredForgeVersion is not null)
                hasRequiredVersion = true;
        }

        if (!hasAny) return Lang.Text("Download.Install.State.NoVersion");

        if (hasRequiredVersion) return Lang.Text("Download.Install.Compat.CompatForgeSpecificOnly");

        return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedLoaderName);
    }

    // 检查某个 OptiFine 是否与某个 Forge 兼容
    private object IsOptiFineSuitForForge(ModDownload.DlOptiFineListEntry optiFine,
        ModDownload.DlForgeVersionEntry forge)
    {
        if ((forge.Inherit ?? "") != (optiFine.Inherit ?? ""))
            return false; // 不是同一个大版本
        if (optiFine.RequiredForgeVersion is null)
            return false; // 不兼容 Forge
        if (string.IsNullOrWhiteSpace(optiFine.RequiredForgeVersion))
            return true; // #4183
        if (optiFine.RequiredForgeVersion.Contains(".")) // XX.X.XXX
            return McVersionComparer.CompareVersion(forge.version.ToString(), optiFine.RequiredForgeVersion) == 0;

        // XXXX
        return forge.version.Revision == Convert.ToDouble(optiFine.RequiredForgeVersion);
    }

    // 限制展开
    private void CardOptiFine_PreviewSwap(object sender, ModBase.RouteEventArgs e)
    {
        if (LoadOptiFineGetError() is not null)
            e.handled = true;
    }

    /// <summary>
    ///     尝试重新可视化 OptiFine 版本列表。
    /// </summary>
    private void OptiFine_Loaded()
    {
        try
        {
            if (ModDownload.dlOptiFineListLoader.State != ModBase.LoadState.Finished)
                return;

            // 获取版本列表
            var versions = new List<ModDownload.DlOptiFineListEntry>();
            foreach (var Version in ModDownload.dlOptiFineListLoader.output.Value)
            {
                if (selectedForge is not null &&
                                          !(bool)IsOptiFineSuitForForge(Version, selectedForge))
                    continue;
                if (Version.DisplayName.StartsWith(_vanillaName + " "))
                    versions.Add(Version);
            }

            if (!versions.Any())
                return;
            // 排序
            versions.Sort((left, right) =>
            {
                if (!left.IsPreview && right.IsPreview)
                    return true;
                if (left.IsPreview && !right.IsPreview)
                    return false;
                return McVersionComparer.CompareVersion(left.DisplayName, right.DisplayName) != 0;
            });
            // 可视化
            PanOptiFine.Children.Clear();
            foreach (var Version in versions)
                PanOptiFine.Children.Add(
                    ModDownloadLib.OptiFineDownloadListItem(Version, (a, b) =>
                        this.OptiFine_Selected((dynamic)a, b), false));
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化 OptiFine 安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void OptiFine_Selected(MyListItem sender, EventArgs e)
    {
        selectedOptiFine = (ModDownload.DlOptiFineListEntry)sender.Tag;
        if (selectedForge is not null &&
                                  !(bool)IsOptiFineSuitForForge(selectedOptiFine, selectedForge))
            selectedForge = null;
        OptiFabric_Loaded();
        Forge_Loaded();
        NeoForge_Loaded();
        CardOptiFine.IsSwapped = true;
        ReloadSelected();
    }

    private void OptiFine_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedOptiFine = null;
        selectedOptiFabric = null;
        autoSelectedOptiFabric = false;
        CardOptiFine.IsSwapped = true;
        e.Handled = true;
        Forge_Loaded();
        NeoForge_Loaded();
        ReloadSelected();
    }

    #endregion

    #region LiteLoader 列表

    /// <summary>
    ///     获取 LiteLoader 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
    private string LoadLiteLoaderGetError()
    {
        // 检查 Loader
        if (GetLoaderError(LoadLiteLoader) is not null)
            return GetLoaderError(LoadLiteLoader);
        if (selectedLoaderName == "NeoForge" || selectedLoaderName == "LegacyFabric" || selectedLoaderName == "LabyMod" || selectedLoaderName == "Cleanroom")
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedLoaderName);
        // 检查版本
        return ModDownload.dlLiteLoaderListLoader.output.Value.Any(v => (v.Inherit ?? "") == (_vanillaName ?? ""))
            ? null
            : Lang.Text("Download.Install.State.NoVersion");
    }

    // 限制展开
    private void CardLiteLoader_PreviewSwap(object sender, ModBase.RouteEventArgs e)
    {
        if (LoadLiteLoaderGetError() is not null)
            e.handled = true;
    }

    /// <summary>
    ///     尝试重新可视化 LiteLoader 版本列表。
    /// </summary>
    private void LiteLoader_Loaded()
    {
        try
        {
            if (ModDownload.dlLiteLoaderListLoader.State != ModBase.LoadState.Finished)
                return;
            // 获取版本列表
            var versions = new List<ModDownload.DlLiteLoaderListEntry>();
            foreach (var Version in ModDownload.dlLiteLoaderListLoader.output.Value)
                if ((Version.Inherit ?? "") == (_vanillaName ?? ""))
                    versions.Add(Version);
            if (!versions.Any())
                return;
            // 可视化
            PanLiteLoader.Children.Clear();
            foreach (var Version in versions)
                PanLiteLoader.Children.Add(ModDownloadLib.LiteLoaderDownloadListItem(Version,
                    (a, b) => this.LiteLoader_Selected((dynamic)a, b), false));
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化 LiteLoader 安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void LiteLoader_Selected(MyListItem sender, EventArgs e)
    {
        selectedLiteLoader = (ModDownload.DlLiteLoaderListEntry)sender.Tag;
        CardLiteLoader.IsSwapped = true;
        ReloadSelected();
    }

    private void LiteLoader_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedLiteLoader = null;
        CardLiteLoader.IsSwapped = true;
        e.Handled = true;
        ReloadSelected();
    }

    #endregion

    #region Forge 列表

    /// <summary>
    ///     获取 Forge 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
    private string LoadForgeGetError()
    {
        if (McVersionComparer.CompareVersionGe("1.5.1", _vanillaName) && McVersionComparer.CompareVersionGe(_vanillaName, "1.1"))
            return Lang.Text("Download.Install.State.NoVersion");
                
        if (selectedLoaderName is not null && !ReferenceEquals(selectedLoaderName, "Forge"))
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedLoaderName);

        // 检查 Loader
        if (GetLoaderError(LoadForge) is not null)
            return GetLoaderError(LoadForge);
        var loader = (ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>>)LoadForge.State;
        if ((_vanillaName ?? "") != (loader.input ?? ""))
            return Lang.Text("Download.Install.State.Getting");
        // 检查版本
        foreach (var Version in loader.output)
        {
            if (Version.Category == "universal" || Version.Category == "client")
                continue; // 跳过无法自动安装的版本
            if (selectedLoaderName is not null && selectedLoaderName != "Forge")
                return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedLoaderName);
            if (selectedOptiFine is not null && McVersionComparer.CompareVersionGe(_vanillaName, "1.13") &&
                McVersionComparer.CompareVersionGe("1.14.3", _vanillaName))
                return Lang.Text("Download.Install.Compat.IncompatibleWithOptiFine"); // 1.13 ~ 1.14.3 OptiFine 检查
            if (selectedOptiFine is not null && !(bool)IsOptiFineSuitForForge(selectedOptiFine, Version))
                continue;
            return null;
        }

        return Lang.Text("Download.Install.Compat.IncompatibleWithOptiFine");
    }

    // 限制展开
    private void CardForge_PreviewSwap(object sender, ModBase.RouteEventArgs e)
    {
        if (LoadForgeGetError() is not null)
            e.handled = true;
    }

    /// <summary>
    ///     尝试重新可视化 Forge 版本列表。
    /// </summary>
    private void Forge_Loaded()
    {
        try
        {
            if (!LoadForge.State.IsLoader)
                return;
            var loader = (ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>>)LoadForge.State;
            if ((_vanillaName ?? "") != (loader.input ?? ""))
                return;
            if (loader.State != ModBase.LoadState.Finished)
                return;
            // 获取要显示的版本
            var versions = loader.output.ToList(); // 复制数组，以免 Output 在实例化后变空
            if (!loader.output.Any())
                return;
            PanForge.Children.Clear();
            versions = versions.Where(v =>
            {
                if (v.Category == "universal" || v.Category == "client")
                    return false; // 跳过无法自动安装的版本
                if (selectedOptiFine is not null &&
                                          !(bool)IsOptiFineSuitForForge(selectedOptiFine, v))
                    return false;
                return true;
            }).OrderByDescending(v => v).ToList();
            ModDownloadLib.ForgeDownloadListItemPreload(PanForge, versions,
                (a, b) => this.Forge_Selected((dynamic)a, b), false);
            foreach (var Version in versions)
                PanForge.Children.Add(
                    ModDownloadLib.ForgeDownloadListItem(Version, (a, b) => this.Forge_Selected((dynamic)a, b), false));
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化 Forge 安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void Forge_Selected(MyListItem sender, EventArgs e)
    {
        selectedForge = (ModDownload.DlForgeVersionEntry)sender.Tag;
        selectedLoaderName = "Forge";
        CardForge.IsSwapped = true;
        if (selectedOptiFine is not null &&
                                  !(bool)IsOptiFineSuitForForge(selectedOptiFine, selectedForge))
            selectedOptiFine = null;
        OptiFine_Loaded();
        ReloadSelected();
    }

    private void Forge_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedForge = null;
        selectedLoaderName = null;
        CardForge.IsSwapped = true;
        e.Handled = true;
        OptiFine_Loaded();
        ReloadSelected();
    }

    #endregion

    #region NeoForge 列表

    /// <summary>
    ///     获取 NeoForge 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
    private string LoadNeoForgeGetError()
    {
        if (selectedOptiFine is not null)
            return Lang.Text("Download.Install.Compat.IncompatibleWithOptiFine");
        if (selectedLoaderName is not null && !ReferenceEquals(selectedLoaderName, "NeoForge"))
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedLoaderName);
        // 检查 Loader
        if (GetLoaderError(LoadNeoForge) is not null)
            return GetLoaderError(LoadNeoForge);
        // 检查版本
        return ModDownload.dlNeoForgeListLoader.output.Value.Any(v => (v.Inherit ?? "") == (_vanillaName ?? ""))
            ? null
            : Lang.Text("Download.Install.State.NoVersion");
    }

    // 限制展开
    private void CardNeoForge_PreviewSwap(object sender, ModBase.RouteEventArgs e)
    {
        if (LoadNeoForgeGetError() is not null)
            e.handled = true;
    }

    /// <summary>
    ///     尝试重新可视化 NeoForge 版本列表。
    /// </summary>
    private void NeoForge_Loaded()
    {
        try
        {
            // 获取版本列表
            if (ModDownload.dlNeoForgeListLoader.State != ModBase.LoadState.Finished)
                return;
            var versions = ModDownload.dlNeoForgeListLoader.output.Value
                .Where(v => (v.Inherit ?? "") == (_vanillaName ?? "")).ToList();
            if (!versions.Any())
                return;
            // 可视化
            PanNeoForge.Children.Clear();
            ModDownloadLib.NeoForgeDownloadListItemPreload(PanNeoForge, versions,
                (a, b) => this.NeoForge_Selected((dynamic)a, b),
                false);
            foreach (var Version in versions)
                PanNeoForge.Children.Add(
                    ModDownloadLib.NeoForgeDownloadListItem(Version, (a, b) => this.NeoForge_Selected((dynamic)a, b),
                        false));
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化 NeoForge 安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void NeoForge_Selected(MyListItem sender, EventArgs e)
    {
        selectedNeoForge = (ModDownload.DlNeoForgeListEntry)sender.Tag;
        selectedLoaderName = "NeoForge";
        CardNeoForge.IsSwapped = true;
        OptiFine_Loaded();
        ReloadSelected();
    }

    private void NeoForge_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedNeoForge = null;
        selectedLoaderName = null;
        CardNeoForge.IsSwapped = true;
        e.Handled = true;
        OptiFine_Loaded();
        ReloadSelected();
    }

    #endregion

    #region Cleanroom 列表

    /// <summary>
    ///     获取 Cleanroom 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
    private string LoadCleanroomGetError()
    {
        if (!_vanillaName.StartsWith("1."))
            return Lang.Text("Download.Install.State.NoAvailableVersion");
        if (selectedOptiFine is not null)
            return Lang.Text("Download.Install.Compat.IncompatibleWithOptiFine");
        if (selectedLoaderName is not null && selectedLoaderName != "Cleanroom")
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedLoaderName);
        if (selectedLiteLoader is not null) 
            return Lang.Text("Download.Install.Compat.IncompatibleWithLiteLoader");
        // 检查 Loader
        if (GetLoaderError(LoadCleanroom) is not null)
            return GetLoaderError(LoadCleanroom);
        // 检查版本
        return ModDownload.dlCleanroomListLoader.output.Value.Any(v => (v.Inherit ?? "") == (_vanillaName ?? ""))
            ? null
            : Lang.Text("Download.Install.State.NoVersion");
    }

    // 限制展开
    private void CardCleanroom_PreviewSwap(object sender, ModBase.RouteEventArgs e)
    {
        if (LoadCleanroomGetError() is not null)
            e.handled = true;
    }

    /// <summary>
    ///     尝试重新可视化 Cleanroom 版本列表。
    /// </summary>
    private void Cleanroom_Loaded()
    {
        try
        {
            // 获取版本列表
            if (ModDownload.dlCleanroomListLoader.State != ModBase.LoadState.Finished)
                return;
            var versions = ModDownload.dlCleanroomListLoader.output.Value
                .Where(v => (v.Inherit ?? "") == (_vanillaName ?? "")).ToList();
            if (!versions.Any())
                return;
            // 可视化
            PanCleanroom.Children.Clear();
            ModDownloadLib.CleanroomDownloadListItemPreload(PanCleanroom, versions,
                (a, b) => this.Cleanroom_Selected((dynamic)a, b), false);
            foreach (var Version in versions)
                PanCleanroom.Children.Add(
                    ModDownloadLib.CleanroomDownloadListItem(Version, (a, b) => this.Cleanroom_Selected((dynamic)a, b),
                        false));
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化 Cleanroom 安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void Cleanroom_Selected(MyListItem sender, EventArgs e)
    {
        selectedCleanroom = (ModDownload.DlCleanroomListEntry)sender.Tag;
        selectedLoaderName = "Cleanroom";
        CardCleanroom.IsSwapped = true;
        OptiFine_Loaded();
        ReloadSelected();
    }

    private void Cleanroom_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedCleanroom = null;
        selectedCleanroomVersion = null;
        selectedLoaderName = null;
        selectedAPIName = null;
        CardCleanroom.IsSwapped = true;
        e.Handled = true;
        OptiFine_Loaded();
        ReloadSelected();
    }

    #endregion

    #region Fabric 列表

    /// <summary>
    ///     获取 Fabric 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
    private string LoadFabricGetError()
    {
        // 检查 OptiFine 1.20.5+：没有 OptiFabric 故全部不兼容
        if (selectedOptiFine is not null && McVersionComparer.CompareVersionGe(_vanillaName, "1.20.5"))
            return Lang.Text("Download.Install.Compat.IncompatibleWithOptiFine");
        // 检查 Loader
        if (GetLoaderError(LoadFabric) is not null)
            return GetLoaderError(LoadFabric);
        // 检查版本
        foreach (JsonObject version in ModDownload.dlFabricListLoader.output.Value["game"].AsArray())
            if ((version["version"].ToString() ?? "") ==
                (_vanillaName.Replace("∞", "infinite").Replace("Combat Test 7c", "1.16_combat-3") ?? ""))
            {
                if (selectedLoaderName is not null && !ReferenceEquals(selectedLoaderName, "Fabric"))
                    return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedLoaderName);
                return null;
            }

        return Lang.Text("Download.Install.State.NoVersion");
    }

    // 限制展开
    private void CardFabric_PreviewSwap(object sender, ModBase.RouteEventArgs e)
    {
        if (LoadFabricGetError() is not null)
            e.handled = true;
    }

    /// <summary>
    ///     尝试重新可视化 Fabric 版本列表。
    /// </summary>
    private void Fabric_Loaded()
    {
        try
        {
            if (ModDownload.dlFabricListLoader.State != ModBase.LoadState.Finished)
                return;
            // 获取版本列表
            var versions = (JsonArray)ModDownload.dlFabricListLoader.output.Value["loader"];
            if (!versions.Any())
                return;
            // 可视化
            PanFabric.Children.Clear();
            PanFabric.Tag = versions;
            CardFabric.SwapControl = PanFabric;
            CardFabric.InstallMethod = stack =>
            {
                foreach (var item in (IEnumerable)stack.Tag)
                    stack.Children.Add(
                        ModDownloadLib.FabricDownloadListItem((JsonObject)item,
                            (a, b) => this.Fabric_Selected((dynamic)a, b)));
            };
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化 Fabric 安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    public void Fabric_Selected(MyListItem sender, EventArgs e)
    {
        selectedFabric = ((dynamic)sender.Tag)["version"].ToString();
        selectedLoaderName = "Fabric";
        FabricApi_Loaded();
        OptiFabric_Loaded();
        CardFabric.IsSwapped = true;
        ReloadSelected();
    }

    private void Fabric_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedFabric = null;
        selectedFabricApi = null;
        autoSelectedFabricApi = false;
        selectedOptiFabric = null;
        autoSelectedOptiFabric = false;
        selectedLoaderName = null;
        selectedAPIName = null;
        CardFabric.IsSwapped = true;
        e.Handled = true;
        ReloadSelected();
    }

    #endregion

    #region Fabric API 列表

    /// <summary>
    ///     判断某 Fabric API 是否适配当前选择的原版版本。
    /// </summary>
    public bool IsFabricApiCompatible(ModComp.CompFile fabricApi)
    {
        var fabricApiName = fabricApi.DisplayName;
        try
        {
            if (fabricApiName is null || _vanillaName is null)
                return false;
            fabricApiName = fabricApiName.ToLower();
            _vanillaName = _vanillaName.Replace("∞", "infinite").Replace("Combat Test 7c", "1.16_combat-3").ToLower();
            if (fabricApiName.StartsWith("[" + _vanillaName + "]"))
                return true;
            if (!fabricApiName.Contains("/") || !fabricApiName.Contains("]"))
                return false;
            // 直接的判断（例如 1.18.1/22w03a）
            foreach (var part in fabricApiName.BeforeFirst("]").TrimStart('[').Split("/"))
                if ((part ?? "") == (_vanillaName ?? ""))
                    return true;
            // 将版本名分割语素（例如 1.16.4/5）
            var lefts = fabricApiName.BeforeFirst("]").RegexSearch("[a-z/]+|[0-9/]+");
            var rights = _vanillaName.BeforeFirst("]").RegexSearch("[a-z/]+|[0-9/]+");
            // 对每段进行判断
            var i = 0;
            while (true)
            {
                // 两边均缺失，感觉是一个东西
                if (lefts.Count - 1 < i && rights.Count - 1 < i)
                    return true;
                // 确定两边是否一致
                var leftValue = lefts.Count - 1 < i ? "-1" : lefts[i];
                var rightValue = rights.Count - 1 < i ? "-1" : rights[i];
                if (!leftValue.Contains("/"))
                {
                    if ((leftValue ?? "") != (rightValue ?? ""))
                        return false;
                }
                // 左边存在斜杠
                else if (!leftValue.Contains(rightValue))
                {
                    return false;
                }

                i += 1;
            }

            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "判断 Fabric API 版本适配性出错（" + fabricApiName + ", " + _vanillaName + "）");
            return false;
        }
    }

    /// <summary>
    ///     获取 FabricApi 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
    private string LoadFabricApiGetError()
    {
        // 检查 Loader
        if (GetLoaderError(LoadFabricApi) is not null)
            return GetLoaderError(LoadFabricApi);
        if (ModDownload.dlFabricApiLoader.output is null)
            return selectedFabric is null ? Lang.Text("Download.Install.Compat.RequiresFabric") : Lang.Text("Download.Install.State.Getting");
        // 检查版本
        if (ModDownload.dlFabricApiLoader.output.Any(f => IsFabricApiCompatible(f)))
            return selectedFabric is null ? Lang.Text("Download.Install.Compat.RequiresFabric") : null;

        return Lang.Text("Download.Install.State.NoVersion");
    }

    // 限制展开
    private void CardFabricApi_PreviewSwap(object sender, ModBase.RouteEventArgs e)
    {
        if (LoadFabricApiGetError() is not null)
            e.handled = true;
    }

    private bool autoSelectedFabricApi;

    /// <summary>
    ///     尝试重新可视化 FabricApi 版本列表。
    /// </summary>
    private void FabricApi_Loaded()
    {
        try
        {
            if (ModDownload.dlFabricApiLoader.State != ModBase.LoadState.Finished)
                return;
            if (_vanillaName is null || selectedFabric is null)
                return;
            // 获取版本列表
            var versions = new List<ModComp.CompFile>();
            foreach (var version in ModDownload.dlFabricApiLoader.output)
                if (IsFabricApiCompatible(version))
                {
                    if (!version.DisplayName.StartsWith("["))
                    {
                        ModBase.Log("[Download] 已特判修改 Fabric API 显示名：" + version.DisplayName, ModBase.LogLevel.Debug);
                        version.DisplayName = "[" + _vanillaName + "] " + version.DisplayName;
                    }

                    versions.Add(version);
                }

            if (!versions.Any())
                return;
            versions = versions.OrderByDescending(v => v.ReleaseDate).ToList();
            // 可视化
            PanFabricApi.Children.Clear();
            foreach (var version in versions)
            {
                if (!IsFabricApiCompatible(version))
                    continue;
                PanFabricApi.Children.Add(
                    ModDownloadLib.FabricApiDownloadListItem(version,
                        (a, b) => this.FabricApi_Selected((dynamic)a, b)));
            }

            // 自动选择 Fabric API
            if (!autoSelectedFabricApi)
            {
                autoSelectedFabricApi = true;
                ModBase.Log($"[Download] 已自动选择 Fabric API：{((MyListItem)PanFabricApi.Children[0]).Title}");
                FabricApi_Selected((MyListItem)PanFabricApi.Children[0], null);
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化 Fabric API 安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void FabricApi_Selected(MyListItem sender, EventArgs e)
    {
        selectedFabricApi = (ModComp.CompFile)sender.Tag;
        selectedAPIName = "Fabric API";
        CardFabricApi.IsSwapped = true;
        ReloadSelected();
    }

    private void FabricApi_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedFabricApi = null;
        selectedAPIName = null;
        CardFabricApi.IsSwapped = true;
        e.Handled = true;
        ReloadSelected();
    }

    #endregion

    #region LegacyFabric 列表

    /// <summary>
    ///     获取 LegacyFabric 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
    private string LoadLegacyFabricGetError()
    {
        if (LoadLegacyFabric is null || LoadLegacyFabric.State.LoadingState == MyLoading.MyLoadingState.Run)
            return Lang.Text("Download.Install.State.Loading");
        if (LoadLegacyFabric.State.LoadingState == MyLoading.MyLoadingState.Error)
            return Lang.Text("Download.Install.State.GetVersionListFailed", ((ModLoader.LoaderBase)LoadLegacyFabric.State).Error.Message);
        foreach (JsonObject Version in ModDownload.dlLegacyFabricListLoader.output.Value["game"].AsArray())
            if ((Version["version"].ToString() ?? "") == (_vanillaName ?? ""))
            {
                if (selectedLiteLoader is not null)
                    return Lang.Text("Download.Install.Compat.IncompatibleWithLiteLoader");
                if (selectedLoaderName is not null && !ReferenceEquals(selectedLoaderName, "LegacyFabric"))
                    return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedLoaderName);
                return null;
            }

        return Lang.Text("Download.Install.State.NoVersion");
    }

    // 限制展开
    private void CardLegacyFabric_PreviewSwap(object sender, ModBase.RouteEventArgs e)
    {
        if (LoadLegacyFabricGetError() is not null)
            e.handled = true;
    }

    /// <summary>
    ///     尝试重新可视化 LegacyFabric 版本列表。
    /// </summary>
    private void LegacyFabric_Loaded()
    {
        try
        {
            if (ModDownload.dlLegacyFabricListLoader.State != ModBase.LoadState.Finished)
                return;
            // 获取版本列表
            var versions = (JsonArray)ModDownload.dlLegacyFabricListLoader.output.Value["loader"];
            if (!versions.Any())
                return;
            // 可视化
            PanLegacyFabric.Children.Clear();
            PanLegacyFabric.Tag = versions;
            CardLegacyFabric.SwapControl = PanLegacyFabric;
            CardLegacyFabric.InstallMethod = stack =>
            {
                foreach (var item in (IEnumerable)stack.Tag)
                    stack.Children.Add(ModDownloadLib.LegacyFabricDownloadListItem((JsonObject)item,
                        (a, b) => this.LegacyFabric_Selected((dynamic)a, b)));
            };
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化 LegacyFabric 安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    public void LegacyFabric_Selected(MyListItem sender, EventArgs e)
    {
        selectedLegacyFabric = ((dynamic)sender.Tag)["version"].ToString();
        selectedLoaderName = "LegacyFabric";
        LegacyFabricApi_Loaded();
        CardLegacyFabric.IsSwapped = true;
        ReloadSelected();
    }

    private void LegacyFabric_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedLegacyFabric = null;
        selectedLegacyFabricApi = null;
        autoSelectedLegacyFabricApi = false;
        selectedLoaderName = null;
        selectedAPIName = null;
        CardLegacyFabric.IsSwapped = true;
        e.Handled = true;
        ReloadSelected();
    }

    #endregion

    #region Legacy Fabric API 列表

    /// <summary>
    ///     从显示名判断该 API 是否与某版本适配。
    /// </summary>
    public static bool IsSuitableLegacyFabricApi(List<string> supportVersions, string minecraftVersion)
    {
        try
        {
            if (supportVersions.Contains(minecraftVersion)) return true;

            return false;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "判断 Legacy Fabric API 版本适配性出错（" + supportVersions + ", " + minecraftVersion + "）");
            return false;
        }
    }

    /// <summary>
    ///     获取 LegacyFabricApi 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
    private string LoadLegacyFabricApiGetError()
    {
        if (LoadLegacyFabricApi is null || LoadLegacyFabricApi.State.LoadingState == MyLoading.MyLoadingState.Run)
            return Lang.Text("Download.Install.State.Loading");
        if (LoadLegacyFabricApi.State.LoadingState == MyLoading.MyLoadingState.Error)
            return Lang.Text("Download.Install.State.GetVersionListFailed", ((ModLoader.LoaderBase)LoadLegacyFabricApi.State).Error.Message);
        if (selectedAPIName is not null && !ReferenceEquals(selectedAPIName, "Legacy Fabric API"))
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedAPIName);
        if (ModDownload.dlLegacyFabricApiLoader.output is null)
        {
            if (selectedLegacyFabric is null)
                return Lang.Text("Download.Install.Compat.RequiresLegacyFabric");
            return Lang.Text("Download.Install.State.Loading");
        }

        foreach (var Version in ModDownload.dlLegacyFabricApiLoader.output)
        {
            if (!IsSuitableLegacyFabricApi(Version.GameVersions, _vanillaName))
                continue;
            if (selectedLegacyFabric is null)
                return Lang.Text("Download.Install.Compat.RequiresLegacyFabric");
            return null;
        }

        return Lang.Text("Download.Install.State.NoVersion");
    }

    // 限制展开
    private void CardLegacyFabricApi_PreviewSwap(object sender, ModBase.RouteEventArgs e)
    {
        if (LoadLegacyFabricApiGetError() is not null)
            e.handled = true;
    }

    private bool autoSelectedLegacyFabricApi;

    /// <summary>
    ///     尝试重新可视化 LegacyFabricApi 版本列表。
    /// </summary>
    private void LegacyFabricApi_Loaded()
    {
        try
        {
            if (ModDownload.dlLegacyFabricApiLoader.State != ModBase.LoadState.Finished)
                return;
            if (_vanillaName is null || selectedLegacyFabric is null)
                return;
            // 获取版本列表
            var versions = new List<ModComp.CompFile>();
            foreach (var Version in ModDownload.dlLegacyFabricApiLoader.output)
                if (IsSuitableLegacyFabricApi(Version.GameVersions, _vanillaName))
                    versions.Add(Version);

            if (!versions.Any())
                return;
            versions = versions.OrderByDescending(v => v.ReleaseDate).ToList();
            // 可视化
            PanLegacyFabricApi.Children.Clear();
            foreach (var Version in versions)
            {
                if (!IsSuitableLegacyFabricApi(Version.GameVersions, _vanillaName))
                    continue;
                PanLegacyFabricApi.Children.Add(
                    ModDownloadLib.LegacyFabricApiDownloadListItem(Version,
                        (a, b) => this.LegacyFabricApi_Selected((dynamic)a, b)));
            }

            // 自动选择 Legacy Fabric API
            if (!autoSelectedLegacyFabricApi)
            {
                autoSelectedLegacyFabricApi = true;
                ModBase.Log($"[Download] 已自动选择 Legacy Fabric API：{((MyListItem)PanLegacyFabricApi.Children[0]).Title}");
                LegacyFabricApi_Selected((MyListItem)PanLegacyFabricApi.Children[0], null);
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化 Legacy Fabric API 安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void LegacyFabricApi_Selected(MyListItem sender, EventArgs e)
    {
        selectedLegacyFabricApi = (ModComp.CompFile)sender.Tag;
        selectedAPIName = "Legacy Fabric API";
        CardLegacyFabricApi.IsSwapped = true;
        ReloadSelected();
    }

    private void LegacyFabricApi_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedLegacyFabricApi = null;
        selectedAPIName = null;
        CardLegacyFabricApi.IsSwapped = true;
        e.Handled = true;
        ReloadSelected();
    }

    #endregion


    #region OptiFabric 列表

    /// <summary>
    ///     判断某 OptiFabric 是否适配当前选择的原版版本。
    /// </summary>
    private bool IsOptiFabricCompatible(ModComp.CompFile modFile)
    {
        try
        {
            if (_vanillaName is null)
                return false;
            return modFile.GameVersions.Contains(_vanillaName);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "判断 OptiFabric 版本适配性出错（" + _vanillaName + "）");
            return false;
        }
    }

    private bool autoSelectedOptiFabric;

    /// <summary>
    ///     获取 OptiFabric 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
    private string LoadOptiFabricGetError()
    {
        if (VanillaDrop >= 140 && VanillaDrop <= 150)
            return Lang.Text("Download.Install.Compat.OptiFabricOriginsRequired");
        // 检查 Loader
        if (GetLoaderError(LoadOptiFabric) is not null)
            return GetLoaderError(LoadOptiFabric);
        // 检查版本
        if (ModDownload.dlOptiFabricLoader.output is null)
        {
            if (selectedFabric is null && selectedOptiFine is null)
                return Lang.Text("Download.Install.Compat.RequiresOptiFineAndFabric");
            if (selectedFabric is null)
                return Lang.Text("Download.Install.Compat.RequiresFabric");
            if (selectedOptiFine is null)
                return Lang.Text("Download.Install.Compat.RequiresOptiFine");
            return Lang.Text("Download.Install.State.Getting");
        }

        foreach (var version in ModDownload.dlOptiFabricLoader.output)
        {
            if (!IsOptiFabricCompatible(version))
                continue; // 2135#
            if (selectedFabric is null && selectedOptiFine is null)
                return Lang.Text("Download.Install.Compat.RequiresOptiFineAndFabric");
            if (selectedFabric is null)
                return Lang.Text("Download.Install.Compat.RequiresFabric");
            if (selectedOptiFine is null)
                return Lang.Text("Download.Install.Compat.RequiresOptiFine");
            return null; // 通过检查
        }

        return Lang.Text("Download.Install.State.NoVersion");
    }

    // 限制展开
    private void CardOptiFabric_PreviewSwap(object sender, ModBase.RouteEventArgs e)
    {
        if (LoadOptiFabricGetError() is not null)
            e.handled = true;
    }

    /// <summary>
    ///     尝试重新可视化 OptiFabric 版本列表。
    /// </summary>
    private void OptiFabric_Loaded()
    {
        try
        {
            if (ModDownload.dlOptiFabricLoader.State != ModBase.LoadState.Finished)
                return;
            if (_vanillaName is null || selectedFabric is null || selectedOptiFine is null)
                return;
            // 获取版本列表
            var versions = new List<ModComp.CompFile>();
            foreach (var Version in ModDownload.dlOptiFabricLoader.output)
                if (IsOptiFabricCompatible(Version))
                    versions.Add(Version);
            if (!versions.Any())
                return;
            // 排序
            versions = versions.OrderByDescending(v => v.ReleaseDate).ToList();
            // 可视化
            PanOptiFabric.Children.Clear();
            foreach (var Version in versions)
            {
                if (!IsOptiFabricCompatible(Version))
                    continue;
                PanOptiFabric.Children.Add(
                    ModDownloadLib.OptiFabricDownloadListItem(Version,
                        (a, b) => this.OptiFabric_Selected((dynamic)a, b)));
            }

            // 自动选择 OptiFabric
            if (autoSelectedOptiFabric || (VanillaDrop >= 140 && VanillaDrop <= 150))
                return; // 1.14~15 不自动选择
            autoSelectedOptiFabric = true;
            ModBase.Log($"[Download] 已自动选择 OptiFabric：{((MyListItem)PanOptiFabric.Children[0]).Title}");
            OptiFabric_Selected((MyListItem)PanOptiFabric.Children[0], null);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化 OptiFabric 安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void OptiFabric_Selected(MyListItem sender, EventArgs e)
    {
        selectedOptiFabric = (ModComp.CompFile)sender.Tag;
        CardOptiFabric.IsSwapped = true;
        ReloadSelected();
    }

    private void OptiFabric_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedOptiFabric = null;
        CardOptiFabric.IsSwapped = true;
        e.Handled = true;
        ReloadSelected();
    }

    #endregion

    #region LabyMod 列表

    /// <summary>
    ///     获取 LabyMod 的加载异常信息。若正常则返回 Nothing。
    /// </summary>
    private string LoadLabyModGetError()
    {
        if (LoadLabyMod is null || LoadLabyMod.State.LoadingState == MyLoading.MyLoadingState.Run)
            return Lang.Text("Download.Install.State.Loading");
        if (LoadLabyMod.State.LoadingState == MyLoading.MyLoadingState.Error)
            return Lang.Text("Download.Install.State.GetVersionListFailed", ((ModLoader.LoaderBase)LoadLabyMod.State).Error.Message);
        // 检查 Loader
        if (GetLoaderError(LoadLabyMod) is not null)
            return GetLoaderError(LoadLabyMod);
        if (selectedOptiFine is not null)
            return Lang.Text("Download.Install.Compat.IncompatibleWithOptiFine");
        if (selectedLoaderName is not null && selectedLoaderName != "LabyMod")
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", selectedLoaderName);
        if (selectedLiteLoader is not null)
            return Lang.Text("Download.Install.Compat.IncompatibleWithLiteLoader");
        foreach (JsonObject Version in ModDownload.dlLabyModListLoader.output.Value["production"]["minecraftVersions"].AsArray())
            if ((Version["version"].ToString() ?? "") == (_vanillaName ?? ""))
                return null;
        foreach (JsonObject Version in ModDownload.dlLabyModListLoader.output.Value["snapshot"]["minecraftVersions"].AsArray())
            if ((Version["version"].ToString() ?? "") == (_vanillaName ?? ""))
                return null;
        return Lang.Text("Download.Install.State.NoVersion");
    }

    // 限制展开
    private void CardLabyMod_PreviewSwap(object sender, ModBase.RouteEventArgs e)
    {
        if (LoadLabyModGetError() is not null)
            e.handled = true;
    }

    /// <summary>
    ///     尝试重新可视化 LabyMod 版本列表。
    /// </summary>
    private void LabyMod_Loaded()
    {
        try
        {
            if (LoadLabyMod.State.LoadingState == MyLoading.MyLoadingState.Run)
                return;
            // 获取版本列表
            var versions = ModDownload.dlLabyModListLoader.output.Value;
            if (versions is null || versions["production"] is null || versions["snapshot"] is null)
                return;
            // 可视化
            var processedVersions = new JsonArray();
            foreach (JsonObject Production in versions["production"]["minecraftVersions"].AsArray())
                if ((Production["version"].ToString() ?? "") == (_vanillaName ?? ""))
                {
                    var productionVersion = new JsonObject();
                    productionVersion.Add("version", versions["production"]["labyModVersion"].ToString());
                    productionVersion.Add("channel", "production");
                    productionVersion.Add("commitReference", versions["production"]["commitReference"].ToString());
                    processedVersions.Add(productionVersion);
                }

            foreach (JsonObject Snapshot in versions["snapshot"]["minecraftVersions"].AsArray())
                if ((Snapshot["version"].ToString() ?? "") == (_vanillaName ?? ""))
                {
                    var snapshotVersion = new JsonObject();
                    snapshotVersion.Add("version", versions["snapshot"]["labyModVersion"].ToString());
                    snapshotVersion.Add("channel", "snapshot");
                    snapshotVersion.Add("commitReference", versions["snapshot"]["commitReference"].ToString());
                    processedVersions.Add(snapshotVersion);
                }

            // MyMsgBox(If(ProcessedVersions.ToString, "Nothing"))
            PanLabyMod.Children.Clear();
            PanLabyMod.Tag = processedVersions;
            CardLabyMod.SwapControl = PanLabyMod;
            CardLabyMod.InstallMethod = stack =>
            {
                foreach (JsonObject item in (IEnumerable)stack.Tag)
                    stack.Children.Add(
                        ModDownloadLib.LabyModDownloadListItem(item, (a, b) => this.LabyMod_Selected((dynamic)a, b)));
            };
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化 LabyMod 安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    public void LabyMod_Selected(MyListItem sender, EventArgs e)
    {
        selectedLabyModChannel = ((dynamic)sender.Tag)("channel").ToString();
        selectedLabyModCommitRef = ((dynamic)sender.Tag)("commitReference").ToString();
        selectedLabyModVersion =
            ((dynamic)sender.Tag)("version").ToString() + (selectedLabyModChannel == "snapshot" ? " " + Lang.Text("Download.Version.Type.Snapshot") : " " + Lang.Text("Download.Version.Type.Stable"));
        selectedLoaderName = "LabyMod";
        CardLabyMod.IsSwapped = true;
        ReloadSelected();
    }

    private void LabyMod_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedLabyModCommitRef = null;
        selectedLabyModVersion = null;
        selectedLabyModChannel = null;
        
        if (selectedLoaderName == "LabyMod")
        {
            selectedLoaderName = null;
        }    

        selectedAPIName = null;
        CardLabyMod.IsSwapped = true;
        e.Handled = true;
        ReloadSelected();
    }

    #endregion
}

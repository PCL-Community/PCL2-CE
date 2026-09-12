using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PCL.Core.App;
using PCL.Core.Utils.Validate;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageDownloadInstall
{
    private bool isLoad;

    public PageDownloadInstall()
    {
        Initialized += (_, _) => LoaderInit();
        Loaded += (_, _) => Init();
        InitializeComponent();
        PanScroll = PanBack;
        LoadMinecraft.Text = Lang.Text("Download.Version.LoadingList");
        WeakLanguageChanged.Add(this, OnLanguageChanged);
        BtnBack.Click += (_, _) => ExitSelectPage();
        CardOptiFine.Swap += (_, _) => ReloadSelected();
        LoadOptiFine.StateChanged += (_, _, _) => ReloadSelected();
        CardForge.Swap += (_, _) => ReloadSelected();
        LoadForge.StateChanged += (_, _, _) => ReloadSelected();
        CardNeoForge.Swap += (_, _) => ReloadSelected();
        LoadNeoForge.StateChanged += (_, _, _) => ReloadSelected();
        CardFabric.Swap += (_, _) => ReloadSelected();
        LoadFabric.StateChanged += (_, _, _) => ReloadSelected();
        CardFabricApi.Swap += (_, _) => ReloadSelected();
        LoadFabricApi.StateChanged += (_, _, _) => ReloadSelected();
        CardOptiFabric.Swap += (_, _) => ReloadSelected();
        LoadOptiFabric.StateChanged += (_, _, _) => ReloadSelected();
        CardLiteLoader.Swap += (_, _) => ReloadSelected();
        LoadLiteLoader.StateChanged += (_, _, _) => ReloadSelected();
        LoadCleanroom.StateChanged += (_, _, _) => ReloadSelected();
        CardCleanroom.Swap += (_, _) => ReloadSelected();
        LoadLabyMod.StateChanged += (_, _, _) => ReloadSelected();
        CardLabyMod.Swap += (_, _) => ReloadSelected();
        TextSelectName.TextChanged += TextSelectName_TextChanged;
        TextSelectName.ValidateChanged += TextSelectName_ValidateChanged;
        CardOptiFine.PreviewSwap += CardOptiFine_PreviewSwap;
        LoadOptiFine.StateChanged += (_, _, _) => OptiFine_Loaded();
        BtnOptiFineClear.MouseLeftButtonUp += OptiFine_Clear;
        CardLiteLoader.PreviewSwap += CardLiteLoader_PreviewSwap;
        LoadLiteLoader.StateChanged += (_, _, _) => LiteLoader_Loaded();
        BtnLiteLoaderClear.MouseLeftButtonUp += LiteLoader_Clear;
        CardForge.PreviewSwap += CardForge_PreviewSwap;
        LoadForge.StateChanged += (_, _, _) => Forge_Loaded();
        BtnForgeClear.MouseLeftButtonUp += Forge_Clear;
        CardNeoForge.PreviewSwap += CardNeoForge_PreviewSwap;
        LoadNeoForge.StateChanged += (_, _, _) => NeoForge_Loaded();
        BtnNeoForgeClear.MouseLeftButtonUp += NeoForge_Clear;
        CardCleanroom.PreviewSwap += CardCleanroom_PreviewSwap;
        LoadCleanroom.StateChanged += (_, _, _) => Cleanroom_Loaded();
        BtnCleanroomClear.MouseLeftButtonUp += Cleanroom_Clear;
        CardFabric.PreviewSwap += CardFabric_PreviewSwap;
        LoadFabric.StateChanged += (_, _, _) => Fabric_Loaded();
        BtnFabricClear.MouseLeftButtonUp += Fabric_Clear;
        CardFabricApi.PreviewSwap += CardFabricApi_PreviewSwap;
        LoadFabricApi.StateChanged += (_, _, _) => FabricApi_Loaded();
        BtnFabricApiClear.MouseLeftButtonUp += FabricApi_Clear;
        CardLegacyFabric.PreviewSwap += CardLegacyFabric_PreviewSwap;
        LoadLegacyFabric.StateChanged += (_, _, _) => LegacyFabric_Loaded();
        BtnLegacyFabricClear.MouseLeftButtonUp += LegacyFabric_Clear;
        CardLegacyFabricApi.PreviewSwap += CardLegacyFabricApi_PreviewSwap;
        LoadLegacyFabricApi.StateChanged += (_, _, _) => LegacyFabricApi_Loaded();
        BtnLegacyFabricApiClear.MouseLeftButtonUp += LegacyFabricApi_Clear;
        CardOptiFabric.PreviewSwap += CardOptiFabric_PreviewSwap;
        LoadOptiFabric.StateChanged += (_, _, _) => OptiFabric_Loaded();
        BtnOptiFabricClear.MouseLeftButtonUp += OptiFabric_Clear;
        CardLabyMod.PreviewSwap += CardLabyMod_PreviewSwap;
        LoadLabyMod.StateChanged += (_, _, _) => LabyMod_Loaded();
        BtnLabyModClear.MouseLeftButtonUp += LabyMod_Clear;
        TextSelectName.KeyDown += TextSelectName_KeyDown;
        BtnStart.Click += (_, _) => BtnStart_Click();
    }
    private static void OnLanguageChanged(PageDownloadInstall page) => page._OnLanguageChanged();

    private void LoaderInit()
    {
        disabledPageAnimControls.Add(BtnStart);
        PageLoaderInit(LoadMinecraft, PanLoad, PanAllBack, null, ModDownload.dlClientListLoader,
            _ => LoadMinecraft_OnFinish());
    }

    private void Init()
    {
        PanBack.ScrollToHome();
        ModDownload.dlOptiFineListLoader.Start();
        ModDownload.dlLiteLoaderListLoader.Start();
        ModDownload.dlFabricListLoader.Start();
        ModDownload.dlNeoForgeListLoader.Start();
        ModDownload.dlCleanroomListLoader.Start();
        ModDownload.dlLabyModListLoader.Start();
        ModDownload.dlLegacyFabricListLoader.Start();

        // 重载预览
        TextSelectName.ValidateRules = [new FolderNameValidator(ModFolder.mcFolderSelected + "versions")];
        TextSelectName.Validate();
        ReloadSelected();

        // 非重复加载部分
        if (isLoad)
            return;
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
                return message == Lang.Text("Download.Install.State.NoVersion")
                    ? Lang.Text("Download.Install.State.NoVersion")
                    : Lang.Text("Download.Install.State.GetFailed", message);
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

    private void BtnBack_Click(object sender, EventArgs e)
    {
        ExitSelectPage();
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

        PanInner.Margin = new Thickness(25d, 10d, 25d, 40d);

        autoSelectedFabricApi = false;
        autoSelectedOptiFabric = false;
        isSelectNameEdited = false;
        PanSelect.Visibility = Visibility.Visible;
        PanSelect.IsHitTestVisible = true;
        PanMinecraft.IsHitTestVisible = false;
        PanBack.IsHitTestVisible = false;
        PanBack.ScrollToHome();

        disabledPageAnimControls.Remove(BtnStart);
        BtnStart.Show = true;
        CardOptiFine.IsSwapped = true;
        CardLiteLoader.IsSwapped = true;
        CardForge.IsSwapped = true;
        CardNeoForge.IsSwapped = true;
        CardCleanroom.IsSwapped = true;
        CardFabric.IsSwapped = true;
        CardFabricApi.IsSwapped = true;
        CardOptiFabric.IsSwapped = true;
        CardLabyMod.IsSwapped = true;

        if (!States.Hint.InstallPageBack)
        {
            States.Hint.InstallPageBack = true;
            HintService.Hint(Lang.Text("Download.Install.Hint.MinecraftBack"));
        }

        // 如果在选择页面按了刷新键，选择页的东西可能会由于动画被隐藏，但不会由于加载结束而再次显示，因此这里需要手动恢复
        foreach (var control in GetAllAnimControls(PanSelect))
        {
            control.Opacity = 1d;
            if (control.RenderTransform is null || control.RenderTransform is TranslateTransform)
                control.RenderTransform = new TranslateTransform();
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

        // 启动 Fabric API、Legacy Fabric API、OptiFabric、LabyMod 加载
        ModDownload.dlFabricApiLoader.Start();
        ModDownload.dlLegacyFabricApiLoader.Start();
        ModDownload.dlOptiFabricLoader.Start();
        ModDownload.dlLabyModListLoader.Start();

        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaOpacity(PanMinecraft, -PanMinecraft.Opacity, 70, 10),
            ModAnimation.AaTranslateX(PanMinecraft, -50 - ((TranslateTransform)PanMinecraft.RenderTransform).X, 90, 10),
            ModAnimation.AaCode(() =>
            {
                PanBack.ScrollToHome();
                TextSelectName.Validate();
                OptiFine_Loaded();
                LiteLoader_Loaded();
                Forge_Loaded();
                NeoForge_Loaded();
                Cleanroom_Loaded();
                Fabric_Loaded();
                LegacyFabric_Loaded();
                FabricApi_Loaded();
                LegacyFabricApi_Loaded();
                OptiFabric_Loaded();
                LabyMod_Loaded();
                ReloadSelected();
                PanMinecraft.Visibility = Visibility.Collapsed;
            }, after: true),
            ModAnimation.AaOpacity(PanSelect, 1d - PanSelect.Opacity, 70, 100),
            ModAnimation.AaTranslateX(PanSelect, -((TranslateTransform)PanSelect.RenderTransform).X, 160, 100,
                new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.ExtraStrong)),
            ModAnimation.AaCode(() =>
            {
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
                BtnNeoForgeClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardNeoForge.MainTextBlock, Mode = BindingMode.OneWay });
                BtnCleanroomClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardCleanroom.MainTextBlock, Mode = BindingMode.OneWay });
                BtnFabricClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardFabric.MainTextBlock, Mode = BindingMode.OneWay });
                BtnLegacyFabricClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardLegacyFabric.MainTextBlock, Mode = BindingMode.OneWay });
                BtnFabricApiClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardFabricApi.MainTextBlock, Mode = BindingMode.OneWay });
                BtnLegacyFabricApiClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground")
                        { Source = CardLegacyFabricApi.MainTextBlock, Mode = BindingMode.OneWay });
                BtnLabyModClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardLabyMod.MainTextBlock, Mode = BindingMode.OneWay });
                BtnOptiFabricClearInner.SetBinding(Shape.FillProperty,
                    new Binding("Foreground") { Source = CardOptiFabric.MainTextBlock, Mode = BindingMode.OneWay });
            }, after: true)
        }, "FrmDownloadInstall SelectPageSwitch", true);
    }

    public void ExitSelectPage()
    {
        if (!isInSelectPage)
            return;
        isInSelectPage = false;

        PanInner.Margin = new Thickness(25d, 10d, 25d, 25d);

        disabledPageAnimControls.Add(BtnStart);
        BtnStart.Show = false;
        ClearSelected(); // 清除已选择项
        PanMinecraft.Visibility = Visibility.Visible;
        PanSelect.IsHitTestVisible = false;
        PanMinecraft.IsHitTestVisible = true;
        PanBack.IsHitTestVisible = false;
        PanBack.ScrollToHome();

        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaOpacity(PanSelect, -PanSelect.Opacity, 70, 10),
            ModAnimation.AaTranslateX(PanSelect, 50d - ((TranslateTransform)PanSelect.RenderTransform).X, 90, 10),
            ModAnimation.AaCode(() => PanBack.ScrollToHome(), after: true),
            ModAnimation.AaOpacity(PanMinecraft, 1d - PanMinecraft.Opacity, 70, 100),
            ModAnimation.AaTranslateX(PanMinecraft, -((TranslateTransform)PanMinecraft.RenderTransform).X, 160, 100,
                new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.ExtraStrong)),
            ModAnimation.AaCode(() =>
            {
                PanSelect.Visibility = Visibility.Collapsed;
                PanBack.IsHitTestVisible = true;
            }, after: true)
        }, "FrmDownloadInstall SelectPageSwitch");
    }

    public void MinecraftSelected(MyListItem sender, MouseButtonEventArgs e)
    {
        _vanillaName = sender.Title;
        _vanillaData = (JsonObject)(dynamic)sender.Tag;
        _vanillaIcon = sender.Logo;
        EnterSelectPage();
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
    ///     选定的主 Mod 加载器。
    /// </summary>
    private ModLoaderType? _selectedLoaderType;

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

    // NeoForge
    private ModDownload.DlNeoForgeListEntry? selectedNeoForge;

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
    private string? selectedLabyModBaseVersion;

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
        // 主预览
        try
        {
        SelectNameUpdate();
        ImgLogo.Source = GetSelectLogo();
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
                    .Replace(" build ", ".").Trim();
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
        _selectedLoaderType = null;
        selectedAPIName = null;
        selectedForge = null;
        selectedNeoForge = null;
        selectedCleanroom = null;
        selectedFabric = null;
        selectedFabricApi = null;
        selectedOptiFabric = null;
        selectedLabyModCommitRef = null;
        selectedLabyModVersion = null;
        selectedLabyModBaseVersion = null;
        selectedLabyModChannel = null;
        selectedLegacyFabric = null;
        selectedLegacyFabricApi = null;
    }

    /// <summary>
    ///     选择主 Mod 加载器时，清除其它主加载器及其专属组件。
    ///     OptiFine 与 LiteLoader 可与部分加载器共存，故不在此处清除。
    /// </summary>
    private void SelectModLoader(ModLoaderType loaderType)
    {
        if (loaderType != ModLoaderType.Forge)
            selectedForge = null;

        if (loaderType != ModLoaderType.NeoForge)
            selectedNeoForge = null;

        if (loaderType != ModLoaderType.Cleanroom)
            selectedCleanroom = null;

        if (loaderType != ModLoaderType.Fabric)
        {
            selectedFabric = null;
            selectedFabricApi = null;
            selectedOptiFabric = null;
            autoSelectedFabricApi = false;
            autoSelectedOptiFabric = false;
        }

        if (loaderType != ModLoaderType.LegacyFabric)
        {
            selectedLegacyFabric = null;
            selectedLegacyFabricApi = null;
            autoSelectedLegacyFabricApi = false;
        }

        if (loaderType != ModLoaderType.LabyMod)
        {
            selectedLabyModChannel = null;
            selectedLabyModCommitRef = null;
            selectedLabyModBaseVersion = null;
            selectedLabyModVersion = null;
        }

        _selectedLoaderType = loaderType;
        selectedAPIName = null;
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

    // 实例名处理
    /// <summary>
    ///     获取默认实例名。
    /// </summary>
    private string GetSelectName()
    {
        var name = _vanillaName;
        if (selectedFabric is not null) name += "-Fabric_" + selectedFabric.Replace("+build", "");
        if (selectedLegacyFabric is not null) name += "-LegacyFabric_" + selectedLegacyFabric;
        if (selectedLabyModVersion is not null)
            name += "-LabyMod_" + selectedLabyModBaseVersion + (selectedLabyModChannel == "snapshot" ? "_Snapshot" : "_Production");
        if (selectedForge is not null) name += "-Forge_" + selectedForge.VersionName;
        if (selectedNeoForge is not null) name += "-NeoForge_" + selectedNeoForge.VersionName;
        if (selectedCleanroom is not null) name += "-Cleanroom_" + selectedCleanroom.VersionName;
        if (selectedLiteLoader is not null) name += "-LiteLoader";
        if (selectedOptiFine is not null)
            name += "-OptiFine_" + selectedOptiFine.DisplayName.Replace(_vanillaName + " ", "").Replace(" ", "_");
        return name;
    }

    private bool isSelectNameEdited;
    private bool isSelectNameChanging;

    private void SelectNameUpdate()
    {
        if (isSelectNameEdited || isSelectNameChanging)
            return;
        isSelectNameChanging = true;
        TextSelectName.Text = GetSelectName();
        isSelectNameChanging = false;
    }

    private void TextSelectName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (isSelectNameChanging)
            return;
        isSelectNameEdited = true;
        ReloadSelected();
    }

    private void TextSelectName_ValidateChanged(object sender, EventArgs e)
    {
        BtnStart.IsEnabled = TextSelectName.IsValidated;
    }

    #endregion

    #region 加载器

    // 结果数据化
    private void LoadMinecraft_OnFinish()
    {
        ExitSelectPage(); // 返回

        try
        {
            if (ModDownload.dlClientListLoader.output.Value["versions"] is not JsonArray versions)
                return;

            _RebuildVersionCards(versions);

            if (mcVersionWaitingForSelect is null) return;

            ModBase.Log("[Download] 自动选择 MC 版本：" + mcVersionWaitingForSelect);

            foreach (JsonObject version1 in versions)
            {
                if (((string)version1["id"] ?? "") != mcVersionWaitingForSelect)
                    continue;

                var item = ModDownloadLib.McDownloadListItem(version1, (_, _) => { }, false);
                MinecraftSelected(item, null);
                break;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Download.Install.Error.OperationFailed"));
        }
    }

    private void _RebuildVersionCards(JsonArray versions)
    {
        var categoryOrder = new[]
        {
            McVersionCategory.Release,
            McVersionCategory.Snapshot,
            McVersionCategory.BeforeRelease,
            McVersionCategory.AprilFools
        };

        var dict = categoryOrder.ToDictionary(
            category => category,
            _ => new List<JsonObject>()
        );

        foreach (JsonObject version in versions)
        {
            var category = McVersionClassifier.ClassifyVersion(version);
            dict[category].Add(version);
        }

        foreach (var category in categoryOrder)
            dict[category] = dict[category]
                .OrderByDescending(McVersionClassifier.GetReleaseTime)
                .ToList();

        PanMinecraft.Children.Clear();

        _AddLatestVersionCard(dict);
        _AddCategoryCards(dict, categoryOrder);
    }

    private void _OnLanguageChanged() => ModBase.RunInUi(() =>
    {
        LoadMinecraft.Text = Lang.Text("Download.Version.LoadingList");
        if (ModDownload.dlClientListLoader.output.Value?["versions"] is JsonArray versions)
            _RebuildVersionCards(versions);
    });

    private void _AddLatestVersionCard(Dictionary<McVersionCategory, List<JsonObject>> dict)
    {
        var latestRelease = dict[McVersionCategory.Release].FirstOrDefault();
        var latestSnapshot = dict[McVersionCategory.Snapshot].FirstOrDefault();

        var latestVersions = new List<JsonObject>();

        if (latestRelease is not null)
        {
            var release = (JsonObject)latestRelease.DeepClone();
            release["lore"] = Lang.Text(
                "Download.Version.Latest.Release",
                Lang.Date(McVersionClassifier.GetReleaseTime(release), "g")
            );
            latestVersions.Add(release);
        }

        if (latestSnapshot is not null &&
            (latestRelease is null || McVersionClassifier.GetReleaseTime(latestRelease) <
                McVersionClassifier.GetReleaseTime(latestSnapshot)))
        {
            var snapshot = (JsonObject)latestSnapshot.DeepClone();
            snapshot["lore"] = Lang.Text(
                "Download.Version.Latest.Development",
                Lang.Date(McVersionClassifier.GetReleaseTime(snapshot), "g")
            );
            latestVersions.Add(snapshot);
        }

        if (latestVersions.Count == 0)
            return;

        var cardInfo = new MyCard
        {
            Title = Lang.Text("Download.Version.Latest.Title"),
            Margin = new Thickness(0d, 15d, 0d, 15d)
        };

        var panInfo = _CreateVersionStack(latestVersions);
        MyCard.StackInstall(ref panInfo, _StackInstall);

        cardInfo.Children.Add(panInfo);
        PanMinecraft.Children.Insert(0, cardInfo);
    }

    private void _AddCategoryCards(
        Dictionary<McVersionCategory, List<JsonObject>> dict,
        IEnumerable<McVersionCategory> categoryOrder)
    {
        foreach (var category in categoryOrder)
        {
            var versions = dict[category];
            if (versions.Count == 0)
                continue;

            var card = new MyCard
            {
                Title = $"{McVersionClassifier.GetCategoryDisplayName(category)} ({versions.Count})",
                Margin = new Thickness(0d, 0d, 0d, 15d)
            };

            var stack = _CreateVersionStack(versions);

            card.Children.Add(stack);
            card.SwapControl = stack;

            // 不能使用 AddressOf，这导致了 #535，原因完全不明，疑似是编译器 Bug
            card.InstallMethod = _StackInstall;
            card.IsSwapped = true;

            PanMinecraft.Children.Add(card);
        }
    }

    private static StackPanel _CreateVersionStack(IEnumerable<JsonObject> versions)
    {
        return new StackPanel
        {
            Margin = new Thickness(20d, MyCard.SwapedHeight, 18d, 0d),
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new TranslateTransform(0d, 0d),
            Tag = versions
        };
    }

    private static void _StackInstall(StackPanel stack)
    {
        foreach (var item in (IEnumerable)stack.Tag)
            stack.Children.Add(
                ModDownloadLib.McDownloadListItem(
                    (JsonObject)item,
                    (sender, e) => ModMain.frmDownloadInstall.MinecraftSelected((MyListItem)sender, e),
                    false
                )
            );
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
        if (_selectedLoaderType is ModLoaderType.NeoForge or ModLoaderType.LabyMod or ModLoaderType.Cleanroom)
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", _selectedLoaderType);
        if (LoadOptiFine is null || LoadOptiFine.State.LoadingState == MyLoading.MyLoadingState.Run)
            return Lang.Text("Download.Install.State.Loading");
        if (LoadOptiFine.State.LoadingState == MyLoading.MyLoadingState.Error)
            return $"{Lang.Text("Download.Install.State.GetVersionListFailed")}{((ModLoader.LoaderBase)LoadOptiFine.State).Error.Message}";
        // 检查 Forge 1.13 - 1.14.3：全部不兼容
        if (_selectedLoaderType == ModLoaderType.Forge && McVersionComparer.CompareVersion(_vanillaName, "1.13") >= 0 &&
            McVersionComparer.CompareVersion("1.14.3", _vanillaName) >= 0) return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", _selectedLoaderType);
        // 检查 Fabric 1.20.5+: 全部不兼容
        if (selectedFabric is not null && McVersionComparer.CompareVersion(_vanillaName, "1.20.4") > 0)
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", _selectedLoaderType);
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

        return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", _selectedLoaderType);
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
                return McVersionComparer.CompareVersionGe(left.DisplayName, right.DisplayName);
            });
            // 可视化
            PanOptiFine.Children.Clear();
            foreach (var Version in versions)
                PanOptiFine.Children.Add(
                    ModDownloadLib.OptiFineDownloadListItem(Version, (a, b) => this.OptiFine_Selected((dynamic)a, b),
                        false));
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "可视化 OptiFine 安装版本列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Download.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void OptiFine_Selected(MyListItem sender, EventArgs e)
    {
        selectedOptiFine = (ModDownload.DlOptiFineListEntry)(dynamic)sender.Tag;
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
        if (_selectedLoaderType is ModLoaderType.NeoForge or ModLoaderType.LegacyFabric or ModLoaderType.LabyMod or ModLoaderType.Cleanroom)
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", _selectedLoaderType);
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
                userSummary: Lang.Text("Download.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void LiteLoader_Selected(MyListItem sender, EventArgs e)
    {
        selectedLiteLoader = (ModDownload.DlLiteLoaderListEntry)(dynamic)sender.Tag;
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
        
        if (_selectedLoaderType is not null && _selectedLoaderType != ModLoaderType.Forge)
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", _selectedLoaderType);
        
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
            if (selectedNeoForge is not null || selectedFabric is not null)
                return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", _selectedLoaderType);
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
                userSummary: Lang.Text("Download.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void Forge_Selected(MyListItem sender, EventArgs e)
    {
        SelectModLoader(ModLoaderType.Forge);
        selectedForge = (ModDownload.DlForgeVersionEntry)(dynamic)sender.Tag;
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
        _selectedLoaderType = null;
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
        if (_selectedLoaderType is not null && _selectedLoaderType != ModLoaderType.NeoForge)
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", _selectedLoaderType);
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
                userSummary: Lang.Text("Download.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void NeoForge_Selected(MyListItem sender, EventArgs e)
    {
        SelectModLoader(ModLoaderType.NeoForge);
        selectedNeoForge = (ModDownload.DlNeoForgeListEntry)(dynamic)sender.Tag;
        CardNeoForge.IsSwapped = true;
        OptiFine_Loaded();
        ReloadSelected();
    }

    private void NeoForge_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedNeoForge = null;
        _selectedLoaderType = null;
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
    private string? LoadCleanroomGetError()
    {
        if (!_vanillaName.StartsWith("1."))
            return Lang.Text("Download.Install.State.NoAvailableVersion");
        if (selectedOptiFine is not null)
            return Lang.Text("Download.Install.Compat.IncompatibleWithOptiFine");
        if (_selectedLoaderType is not null && _selectedLoaderType != ModLoaderType.Cleanroom)
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", _selectedLoaderType);
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
                userSummary: Lang.Text("Download.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void Cleanroom_Selected(MyListItem sender, EventArgs e)
    {
        SelectModLoader(ModLoaderType.Cleanroom);
        selectedCleanroom = (ModDownload.DlCleanroomListEntry)(dynamic)sender.Tag;
        CardCleanroom.IsSwapped = true;
        OptiFine_Loaded();
        ReloadSelected();
    }

    private void Cleanroom_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedCleanroom = null;
        _selectedLoaderType = null;
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
                if (_selectedLoaderType is not null && _selectedLoaderType != ModLoaderType.Fabric)
                    return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", _selectedLoaderType);
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
                userSummary: Lang.Text("Download.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    public void Fabric_Selected(MyListItem sender, EventArgs e)
    {
        SelectModLoader(ModLoaderType.Fabric);
        ModBase.Log(((dynamic)sender.Tag).ToString());
        selectedFabric = ((dynamic)sender.Tag)["version"].ToString();
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
        _selectedLoaderType = null;
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
            var targetName = _vanillaName.Replace("∞", "infinite").Replace("Combat Test 7c", "1.16_combat-3").ToLower();
            return fabricApi.RawGameVersions.Any(f => f == targetName);
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
                    ModDownloadLib.FabricApiDownloadListItem(version, (a, b) => this.FabricApi_Selected((dynamic)a, b)));
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
                userSummary: Lang.Text("Download.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void FabricApi_Selected(MyListItem sender, EventArgs e)
    {
        selectedFabricApi = (ModComp.CompFile)(dynamic)sender.Tag;
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
            return $"{Lang.Text("Download.Install.State.GetVersionListFailed")}{((ModLoader.LoaderBase)LoadLegacyFabric.State).Error.Message}";
        foreach (JsonObject Version in ModDownload.dlLegacyFabricListLoader.output.Value["game"].AsArray())
            if ((Version["version"].ToString() ?? "") == (_vanillaName ?? ""))
            {
                if (selectedLiteLoader is not null)
                    return Lang.Text("Download.Install.Compat.IncompatibleWithLiteLoader");
                if (_selectedLoaderType is not null && _selectedLoaderType != ModLoaderType.LegacyFabric)
                    return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", _selectedLoaderType);
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
                userSummary: Lang.Text("Download.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    public void LegacyFabric_Selected(MyListItem sender, EventArgs e)
    {
        SelectModLoader(ModLoaderType.LegacyFabric);
        selectedLegacyFabric = ((dynamic)sender.Tag)["version"].ToString();
        LegacyFabricApi_Loaded();
        CardLegacyFabric.IsSwapped = true;
        ReloadSelected();
    }

    private void LegacyFabric_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedLegacyFabric = null;
        selectedLegacyFabricApi = null;
        autoSelectedLegacyFabricApi = false;
        _selectedLoaderType = null;
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
            return $"{Lang.Text("Download.Install.State.GetVersionListFailed")}{((ModLoader.LoaderBase)LoadLegacyFabricApi.State).Error.Message}";
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
                userSummary: Lang.Text("Download.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void LegacyFabricApi_Selected(MyListItem sender, EventArgs e)
    {
        selectedLegacyFabricApi = (ModComp.CompFile)(dynamic)sender.Tag;
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
                userSummary: Lang.Text("Download.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    private void OptiFabric_Selected(MyListItem sender, EventArgs e)
    {
        selectedOptiFabric = (ModComp.CompFile)(dynamic)sender.Tag;
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
            return $"{Lang.Text("Download.Install.State.GetVersionListFailed")}{((ModLoader.LoaderBase)LoadLabyMod.State).Error.Message}";
        // 检查 Loader
        if (GetLoaderError(LoadLabyMod) is not null)
            return GetLoaderError(LoadLabyMod);
        if (selectedOptiFine is not null)
            return Lang.Text("Download.Install.Compat.IncompatibleWithOptiFine");
        if (_selectedLoaderType is not null && _selectedLoaderType != ModLoaderType.LabyMod)
            return Lang.Text("Download.Install.Compat.IncompatibleWithLoader", _selectedLoaderType);
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
                userSummary: Lang.Text("Download.Install.Error.OperationFailed"));
        }
    }

    // 选择与清除
    public void LabyMod_Selected(MyListItem sender, EventArgs e)
    {
        SelectModLoader(ModLoaderType.LabyMod);
        selectedLabyModChannel = ((dynamic)sender.Tag)["channel"].ToString();
        selectedLabyModCommitRef = ((dynamic)sender.Tag)["commitReference"].ToString();
        selectedLabyModBaseVersion = ((dynamic)sender.Tag)["version"].ToString();
        selectedLabyModVersion =
            selectedLabyModBaseVersion + " " + (selectedLabyModChannel == "snapshot" ? Lang.Text("Download.Version.Type.Snapshot") : Lang.Text("Download.Version.Type.Stable"));
        CardLabyMod.IsSwapped = true;
        ReloadSelected();
    }

    private void LabyMod_Clear(object sender, MouseButtonEventArgs e)
    {
        selectedLabyModCommitRef = null;
        selectedLabyModVersion = null;
        selectedLabyModBaseVersion = null;
        selectedLabyModChannel = null;
        
        if (_selectedLoaderType == ModLoaderType.LabyMod)
        {
            _selectedLoaderType = null;
        }    
        
        selectedAPIName = null;
        CardLabyMod.IsSwapped = true;
        e.Handled = true;
        ReloadSelected();
    }

    #endregion

    #region 安装

    private void TextSelectName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && BtnStart.IsEnabled)
            BtnStart_Click();
    }

    private void BtnStart_Click()
    {
        // 确认版本隔离
        if (_selectedLoaderType is not null &&
            (Config.Launch.IndieSolutionV2 == 0 ||
             Config.Launch.IndieSolutionV2 == 2))
            if (ModMain.MyMsgBox(
                    Lang.Text("Download.Install.InstanceIsolation.Warning.Message"),
                    Lang.Text("Download.Install.InstanceIsolation.Warning.Title"),
                    Lang.Text("Download.Install.InstanceIsolation.Warning.Cancel"),
                    Lang.Text("Download.Install.InstanceIsolation.Warning.Continue")
                ) == 1)
                return;

        // 提交安装申请
        var instanceName = TextSelectName.Text;
        var request = new ModDownloadLib.McInstallRequest
        {
            targetInstanceName = instanceName,
            targetInstanceFolder = $@"{ModFolder.mcFolderSelected}versions\{instanceName}\",
            minecraftJson = _vanillaData?["url"].ToString(),
            minecraftName = _vanillaName,
            optiFineEntry = selectedOptiFine,
            forgeEntry = selectedForge,
            neoForgeEntry = selectedNeoForge,
            cleanroomEntry = selectedCleanroom,
            fabricVersion = selectedFabric,
            fabricApi = selectedFabricApi,
            optiFabric = selectedOptiFabric,
            liteLoaderEntry = selectedLiteLoader,
            labyModChannel = selectedLabyModChannel,
            labyModCommitRef = selectedLabyModCommitRef,
            legacyFabricVersion = selectedLegacyFabric,
            legacyFabricApi = selectedLegacyFabricApi
        };
        if (!ModDownloadLib.McInstall(request))
            return;
        // 返回，这样在再次进入安装页面时这个实例就会显示文件夹已重复
        ExitSelectPage();
    }

    #endregion
}

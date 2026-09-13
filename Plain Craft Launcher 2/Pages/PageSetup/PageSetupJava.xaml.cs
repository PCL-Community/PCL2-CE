using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.Minecraft;
using PCL.Core.UI;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageSetupJava
{
    private bool isLoad = false;

    public ModLoader.LoaderTask<bool, List<JavaEntry>> loader;

    public PageSetupJava()
    {
        InitializeComponent();
        loader = new ModLoader.LoaderTask<bool, List<JavaEntry>>("JavaPageLoader", Load_GetJavaList);
        Loaded += PageSetupLaunch_Loaded;
    }

    private void PageSetupLaunch_Loaded(object sender, RoutedEventArgs e)
    {
        PageLoaderInit(PanLoad, CardLoad, PanMain, null, loader, _ => OnLoadFinished(), Load_Input);
    }

    private object Load_Input()
    {
        return false;
    }

    private void Load_GetJavaList(ModLoader.LoaderTask<bool, List<JavaEntry>> loader)
    {
        if (loader.input || loader.isForceRestarting)
        {
            JavaService.JavaManager.ScanJavaAsync(force: true).GetAwaiter().GetResult();
            JavaService.JavaManager.CheckAllAvailability();
        }
        loader.output = ModJava.Javas.GetSortedJavaList();
    }

    private void OnLoadFinished()
    {
        PanContent.Children.Clear();
        var itemAuto = new MyListItem
        {
            Type = MyListItem.CheckType.RadioBox,
            Title = Lang.Text("Setup.Java.AutoSelect.Title"),
            Info = Lang.Text("Setup.Java.AutoSelect.Info")
        };
        itemAuto.Check += (sender, e) => Config.Launch.SelectedJava = "";
        PanContent.Children.Add(itemAuto);
        var currentSetJava = Config.Launch.SelectedJava;
        foreach (var entry in ModJava.Javas.GetSortedJavaList())
        {
            var item = ItemBuild(entry);
            PanContent.Children.Add(item);
            if (entry.Installation.JavaExePath == currentSetJava)
                item.SetChecked(true, false, false);
        }

        if (string.IsNullOrEmpty(currentSetJava))
            itemAuto.SetChecked(true, false, false);
    }
    
    private MyListItem ItemBuild(JavaEntry j)
    {
        var item = new MyListItem();
        var versionTypeDesc = j.Installation.IsJre ? "JRE" : "JDK";
        var versionNameDesc = j.Installation.MajorVersion.ToString();
        item.Title = $"{versionTypeDesc} {versionNameDesc}";

        item.Info = j.Installation.JavaFolder;
        var displayTags = new List<string>();
        var displayBits = j.Installation.Is64Bit ? "64 Bit" : "32 Bit";
        displayTags.Add(displayBits);
        var displayBrand = j.Installation.Brand.ToString();
        displayTags.Add(displayBrand);
        item.Tags = displayTags;

        item.Type = MyListItem.CheckType.RadioBox;
        item.Check += (sender, e) =>
        {
            if (!j.Installation.IsStillAvailable)
            {
                HintService.Hint(Lang.Text("Setup.Java.Unavailable"));
                return;
            }

            if (j.IsEnabled)
                Config.Launch.SelectedJava = j.Installation.JavaExePath;
            else
            {
                HintService.Hint(Lang.Text("Setup.Java.EnableBeforeSelect"));
                e.handled = true;
            }
        };
        var btnOpenFolder = new MyIconButton();
        btnOpenFolder.SvgIcon = "lucide/folder-open";
        btnOpenFolder.ToolTip = Lang.Text("Common.Action.Open");
        btnOpenFolder.Click += (sender, e) =>
        {
            if (!j.Installation.IsStillAvailable)
            {
                HintService.Hint(Lang.Text("Setup.Java.Unavailable"));
                return;
            }

            ModBase.OpenExplorer(j.Installation.JavaFolder);
        };
        var btnInfo = new MyIconButton();
        btnInfo.SvgIcon = "lucide/info";
        btnInfo.ToolTip = Lang.Text("Setup.Java.Detail.ToolTip");
        btnInfo.Click += (sender, e) =>
        {
            if (!j.Installation.IsStillAvailable)
            {
                HintService.Hint(Lang.Text("Setup.Java.Unavailable"));
                return;
            }

            ModMain.MyMsgBox(
                Lang.Text("Setup.Java.Info.Format",
                    versionTypeDesc,
                    j.Installation.Version.ToString(),
                    j.Installation.Architecture.ToString(),
                    displayBits,
                    displayBrand,
                    j.Installation.JavaFolder),
                Lang.Text("Setup.Java.Info.Title"));
        };
        var btnEnableSwitch = new MyIconButton();
        
        item.Buttons = [btnOpenFolder, btnInfo, btnEnableSwitch];

        void UpdateEnableStyle(bool isCurEnable)
        {
            if (!j.Installation.IsStillAvailable)
            {
                HintService.Hint(Lang.Text("Setup.Java.Unavailable"));
                return;
            }

            if (isCurEnable)
            {
                item.LabTitle.TextDecorations = null;
                item.LabTitle.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
                btnEnableSwitch.SvgIcon = "lucide/circle-minus";
                btnEnableSwitch.ToolTip = Lang.Text("Setup.Java.Disable");
            }
            else
            {
                item.LabTitle.TextDecorations = TextDecorations.Strikethrough;
                item.LabTitle.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
                btnEnableSwitch.SvgIcon = "lucide/circle-check";
                btnEnableSwitch.ToolTip = Lang.Text("Setup.Java.Enable");
            }
        }
        
        btnEnableSwitch.Click += (_, _) =>
        {
            try
            {
                var target = ModJava.Javas.AddOrGet(j.Installation.JavaExePath);
                if (target is null)
                {
                    HintService.Hint(Lang.Text("Setup.Java.Unavailable"));
                    return;
                }

                if (target.IsEnabled && Config.Launch.SelectedJava == target.Installation.JavaExePath)
                {
                    HintService.Hint(Lang.Text("Setup.Java.DeselectBeforeDisable"));
                    return;
                }

                target.IsEnabled = !target.IsEnabled;
                UpdateEnableStyle(target.IsEnabled);
                ModJava.Javas.SaveConfig();
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Setup.Java.EnableFailed"),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Setup.Java.EnableFailed"));
            }
        };
        UpdateEnableStyle(j.IsEnabled);

        return item;
    }

    private void BtnAdd_Click(object sender, ModBase.RouteEventArgs e)
    {
        var ret = SystemDialogs.SelectFile(Lang.Text("Setup.Java.SelectFile.Filter"), Lang.Text("Setup.Java.SelectFile.Title"));
        if (string.IsNullOrEmpty(ret) || !File.Exists(ret))
            return;
        if (ModJava.Javas.Exist(ret))
            HintService.Hint(Lang.Text("Setup.Java.AlreadyExists"));
        else
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Run(() =>
                {
                    ModJava.Javas.AddOrGet(ret);
                    ModJava.Javas.SaveConfig();
                });
                if (ModJava.Javas.Exist(ret))
                {
                    HintService.Hint(Lang.Text("Setup.Java.Added"), HintType.Success);
                    loader.Start(true, true);
                }
                else
                {
                    HintService.Hint(Lang.Text("Setup.Java.AddFailed"), HintType.Error);
                }
            }));
    }
}

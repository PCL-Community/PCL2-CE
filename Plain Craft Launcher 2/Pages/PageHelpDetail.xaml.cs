using System.IO;
using System.Security;
using System.Windows;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageHelpDetail : IRefreshable
{
    internal sealed record HelpContent(string SourcePath, string Title, string Xaml);

    private HelpContent _content = null!;

    public string Title => _content.Title;

    internal PageHelpDetail(HelpContent content)
    {
        InitializeComponent();
        Loaded += (_, _) => PanBack.ScrollToHome();
        ApplyContent(content);
    }

    /// <summary>
    ///     从帮助 JSON 及同名 XAML 文件读取详情页数据。失败会抛出异常。
    /// </summary>
    internal static HelpContent LoadContent(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("未找到帮助 JSON 文件", jsonPath);

        var json = ModMain.ArgumentReplace(File.ReadAllText(jsonPath), SecurityElement.Escape);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Title", out var titleElement) ||
            titleElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(titleElement.GetString()))
            throw new ArgumentException("帮助 JSON 中未找到有效的 Title 项", nameof(jsonPath));

        var xamlPath = Path.ChangeExtension(jsonPath, ".xaml");
        if (!File.Exists(xamlPath))
            throw new FileNotFoundException("未找到帮助 JSON 对应的 XAML 文件", xamlPath);

        var xaml = File.ReadAllText(xamlPath);
        if (string.IsNullOrWhiteSpace(xaml))
            throw new InvalidDataException("帮助 XAML 文件为空");

        return new HelpContent(jsonPath, titleElement.GetString()!, xaml);
    }

    private void ApplyContent(HelpContent content)
    {
        // 修改时应同时修改 PageLaunchRight.LoadContent。
        var xaml = ModMain.ArgumentReplace(content.Xaml);
        while (xaml.Contains("xmlns"))
            xaml = xaml.RegexReplace("xmlns[^\"']*(\"|')[^\"']*(\"|')", "").Replace("xmlns", "");
        xaml =
            $"<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:sys=\"clr-namespace:System;assembly=System.Runtime\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"clr-namespace:PCL;assembly=Plain Craft Launcher 2\">{xaml}</StackPanel>";

        var element = (UIElement)ModBase.GetObjectFromXML(xaml, out var sanitizeResult);
        foreach (var unsupported in sanitizeResult.UnsupportedTypesFound)
            HintService.Hint(Lang.Text("Event.Sanitize.UnsupportedTypeHint", unsupported), HintType.Error);
        foreach (var unknown in sanitizeResult.UnrecognizedTypes)
            HintService.Hint(Lang.Text("Event.Sanitize.UnknownTypeHint", unknown), HintType.Error);

        _content = content;
        PanCustom.Children.Clear();
        PanCustom.Children.Add(element);
        if (ModMain.frmMain?.pageCurrent.page == FormMain.PageType.HelpDetail)
            ModMain.frmMain.PageNameRefresh();
    }

    public void Refresh()
    {
        try
        {
            ApplyContent(LoadContent(_content.SourcePath));
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "刷新帮助详情页失败",
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Event.Error.ExecutionFailed", EventType.OpenHelp, _content.SourcePath));
        }
    }
}

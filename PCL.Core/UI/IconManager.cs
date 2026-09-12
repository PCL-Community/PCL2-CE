using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xaml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WpfXamlReader = System.Windows.Markup.XamlReader;

namespace PCL.Core.UI;

// 图标管理器（处理集合和选择逻辑）
public class IconManager : INotifyPropertyChanged {
    private readonly Dictionary<string, IconModel> _iconIndex = new();

    public IconModel? SelectedIcon
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(SelectedIcon));
        }
    }

    public bool SetSelectedIconByName(string name) {
        if (_iconIndex.TryGetValue(name, out var icon)) {
            SelectedIcon = icon;
            return true;
        }
        return false;
    }

    public bool AddIconFromXaml(string name, string xamlString) {
        if (string.IsNullOrWhiteSpace(name) || _iconIndex.ContainsKey(name)) return false; // 避免重复

        if (TryLoadIconFromXaml(xamlString, out var content)) {
            var model = new IconModel(name, content);
            _iconIndex[name] = model;
            return true;
        }
        return false;
    }

    // 可选：添加移除方法
    public void RemoveIconByName(string name) {
        _iconIndex.Remove(name);
    }
    
    // 从 XAML 字符串加载图标
    public static bool TryLoadIconFromXaml(string xamlString, out UIElement? icon) {
        icon = null;
        if (string.IsNullOrWhiteSpace(xamlString)) return false;
        
        // 确保在UI线程执行
        if (!Application.Current.Dispatcher.CheckAccess()) {
            return false;
        }
        
        try {
            ValidateSafeLooseXaml(xamlString);
            icon = (UIElement)WpfXamlReader.Parse(xamlString);
            return true;
        }
        catch (Exception) {
            return false;
        }
    }
    
    // 从 XAML 字符串加载图标
    public static bool LoadIconFromXaml(string xamlString, out UIElement? icon) {
        icon = null;
        if (string.IsNullOrWhiteSpace(xamlString)) {
            throw new ArgumentNullException(nameof(xamlString), "XAML 字符串不能为空或空白。");
        }
        
        // 确保在UI线程执行
        if (!Application.Current.Dispatcher.CheckAccess()) {
            throw new InvalidOperationException("XAML 解析需要在 UI 线程执行。");
        }
        
        ValidateSafeLooseXaml(xamlString);
        icon = (UIElement)WpfXamlReader.Parse(xamlString);
        return true;
    }

    private static readonly Type[] DisallowedLooseXamlTypes =
    [
        typeof(WebBrowser),
        typeof(Frame),
        typeof(MediaElement),
        typeof(ObjectDataProvider),
        typeof(XmlDataProvider),
        typeof(WpfXamlReader),
        typeof(Window),
        typeof(Process),
        typeof(ProcessStartInfo)
    ];

    private static readonly string[] DisallowedLooseXamlMembers =
    [
        "Code",
        "FactoryMethod",
        "Static"
    ];

    private static readonly string[] DisallowedLooseXamlAssemblies =
    [
        "Microsoft.Xaml.Behaviors"
    ];

    private static void ValidateSafeLooseXaml(string xamlString) {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xamlString));
        using var reader = new XamlXmlReader(stream);

        while (reader.Read()) {
            if (reader.Type?.UnderlyingType is { } nodeType) {
                ValidateLooseXamlType(nodeType);
            }

            if (reader.Member is { } member) {
                if (DisallowedLooseXamlMembers.Contains(member.Name)) {
                    throw new UnauthorizedAccessException($"不允许使用 {member.Name} 成员。");
                }

                if (member.DeclaringType?.UnderlyingType is { } declaringType) {
                    ValidateLooseXamlType(declaringType);
                }
            }

            if (reader.Value is string value) {
                ValidateLooseXamlValue(value);
            }
        }
    }

    private static void ValidateLooseXamlType(Type type) {
        if (DisallowedLooseXamlTypes.Any(disallowedType => disallowedType.IsAssignableFrom(type))) {
            throw new UnauthorizedAccessException($"不允许使用 {type.Name} 类型。");
        }

        var assemblyName = type.Assembly.GetName().Name;
        if (assemblyName is not null && DisallowedLooseXamlAssemblies.Any(disallowedAssembly => assemblyName.StartsWith(disallowedAssembly, StringComparison.Ordinal))) {
            throw new UnauthorizedAccessException($"不允许使用 {assemblyName} 程序集中的类型。");
        }
    }

    private static void ValidateLooseXamlValue(string value) {
        if (DisallowedLooseXamlTypes.Any(disallowedType => string.Equals(value, disallowedType.Name, StringComparison.Ordinal))) {
            throw new UnauthorizedAccessException($"不允许使用 {value} 值。");
        }

        if (DisallowedLooseXamlAssemblies.Any(disallowedAssembly => value.Contains(disallowedAssembly, StringComparison.Ordinal))) {
            throw new UnauthorizedAccessException($"不允许引用 {value}。");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
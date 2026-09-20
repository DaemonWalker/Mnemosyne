using CommunityToolkit.Mvvm.ComponentModel;
using Mnemosyne.Plugin.Abstractions;

namespace Mnemosyne.ViewModels;

/// <summary>
/// 设置页"插件"分区中一个插件的设置组：展示插件名与描述，包含若干设置项。
/// </summary>
public partial class PluginSettingsGroupViewModel : ObservableObject
{
    public PluginSettingsGroupViewModel(IMnemosynePlugin plugin, IReadOnlyList<PluginSettingItemViewModel> items)
    {
        PluginId = plugin.Id;
        DisplayName = plugin.DisplayName;
        Description = plugin.Description;
        Items = items;
    }

    /// <summary>插件 Id（设置存储键）。</summary>
    public string PluginId { get; }

    /// <summary>插件显示名（组标题）。</summary>
    public string DisplayName { get; }

    /// <summary>插件描述（组标题下辅助文本）。</summary>
    public string Description { get; }

    /// <summary>该插件的设置项列表。</summary>
    public IReadOnlyList<PluginSettingItemViewModel> Items { get; }
}

/// <summary>
/// 单个插件设置项的编辑状态：按 Descriptor.Type 暴露对应类型的可绑定值，
/// 界面按 Type 渲染控件（Bool→CheckBox，String/Path→TextBox，Int→数字框，Enum→ComboBox）。
/// </summary>
public partial class PluginSettingItemViewModel : ObservableObject
{
    public PluginSettingItemViewModel(PluginSettingDescriptor descriptor, object? currentValue)
    {
        Descriptor = descriptor;
        switch (descriptor.Type)
        {
            case PluginSettingType.Bool:
                _boolValue = currentValue is bool b && b;
                break;
            case PluginSettingType.Enum:
                _selectedEnumValue = currentValue as string ?? descriptor.EnumValues.FirstOrDefault();
                break;
            default:
                // String/Int/Path 都以文本编辑，Int 在 Value 取值时解析
                _stringValue = currentValue?.ToString() ?? string.Empty;
                break;
        }
    }

    public PluginSettingDescriptor Descriptor { get; }

    public string DisplayName => Descriptor.DisplayName;

    public string Description => Descriptor.Description;

    public PluginSettingType Type => Descriptor.Type;

    public IReadOnlyList<string> EnumValues => Descriptor.EnumValues;

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private string _stringValue = string.Empty;

    [ObservableProperty]
    private string? _selectedEnumValue;

    /// <summary>当前编辑值（按 Type 取对应属性；Int 由文本解析，非法输入回退 0；Enum/Path 以 string 存储）。</summary>
    public object? Value => Type switch
    {
        PluginSettingType.Bool => BoolValue,
        PluginSettingType.Int => int.TryParse(StringValue, out int i) ? i : 0,
        PluginSettingType.Enum => SelectedEnumValue ?? string.Empty,
        _ => StringValue,
    };
}

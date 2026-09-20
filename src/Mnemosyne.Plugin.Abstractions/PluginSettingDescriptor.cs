namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 插件设置项声明。插件只描述 schema（键、类型、默认值、展示文本），
/// 设置界面由主程序统一渲染，用户改动经 <see cref="IPluginContext.GetSetting"/> 读取。
/// 展示文本的本地化由插件自行负责。
/// </summary>
public sealed class PluginSettingDescriptor
{
    /// <summary>设置键（插件内唯一，作为存储键）。</summary>
    public string Key { get; }

    /// <summary>设置项显示名。</summary>
    public string DisplayName { get; }

    /// <summary>设置项说明（界面辅助文本）。</summary>
    public string Description { get; }

    /// <summary>设置项类型。</summary>
    public PluginSettingType Type { get; }

    /// <summary>默认值；用户未改动时 <see cref="IPluginContext.GetSetting"/> 返回此值。</summary>
    public object? DefaultValue { get; }

    /// <summary>枚举候选值列表；仅 <see cref="PluginSettingType.Enum"/> 时非空，其余类型为空数组。</summary>
    public IReadOnlyList<string> EnumValues { get; }

    public PluginSettingDescriptor(
        string key,
        string displayName,
        string description,
        PluginSettingType type,
        object? defaultValue,
        IReadOnlyList<string>? enumValues = null)
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
        Type = type;
        DefaultValue = defaultValue;
        EnumValues = enumValues ?? Array.Empty<string>();
    }
}

namespace Mnemosyne.Plugin.Abstractions;

/// <summary>插件设置项类型，决定主程序设置页渲染的控件与值的 .NET 类型。</summary>
public enum PluginSettingType
{
    /// <summary>布尔开关（CheckBox），值为 bool。</summary>
    Bool,

    /// <summary>单行文本（TextBox），值为 string。</summary>
    String,

    /// <summary>整数（数字输入框），值为 int。</summary>
    Int,

    /// <summary>枚举下拉框（ComboBox），值为 string，候选由 <see cref="PluginSettingDescriptor.EnumValues"/> 给出。</summary>
    Enum,

    /// <summary>文件/目录路径（TextBox + 浏览按钮），值为 string。</summary>
    Path,
}

namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 插件贡献的一个主题。<see cref="XamlPath"/> 指向随插件 dll 一起分发的 WPF 资源字典文件
/// （相对插件 dll 所在目录），键集合应与主程序内置 Dark.xaml 全量一致——
/// 主程序加载插件主题时会先压入 Dark 字典兜底，缺键回退深色主题色；
/// 但 Brush.* 在字典内部以 StaticResource 解析，需自带 Brush 定义才能改变画刷颜色，
/// 建议以 Dark.xaml 全文为模板改色。
/// </summary>
public sealed class PluginThemeDefinition
{
    /// <param name="name">主题稳定标识（写入 settings.json，一经发布不得更改），如 "Solarized Dark"。</param>
    /// <param name="displayName">设置页展示名。</param>
    /// <param name="xamlPath">资源字典文件路径（相对插件 dll 所在目录）。</param>
    public PluginThemeDefinition(string name, string displayName, string xamlPath)
    {
        Name = name;
        DisplayName = displayName;
        XamlPath = xamlPath;
    }

    /// <summary>主题稳定标识（持久化到用户设置）。与内置 "Dark"/"Light" 或其他插件冲突时被忽略。</summary>
    public string Name { get; }

    /// <summary>设置页展示名。</summary>
    public string DisplayName { get; }

    /// <summary>资源字典文件路径（相对插件 dll 所在目录）。</summary>
    public string XamlPath { get; }
}

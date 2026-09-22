using Mnemosyne.Plugin.Abstractions;

namespace Mnemosyne.Themes.Solarized;

/// <summary>
/// Solarized Dark 主题插件。资源字典随 dll 分发（SolarizedDark.xaml），
/// 键集合与内置 Dark.xaml 全量一致；主程序加载时以 Dark 垫底兜底缺键。
/// </summary>
public sealed class SolarizedTheme : IMnemosynePlugin, IThemeContribution
{
    public string Id => "mnemosyne.themes.solarized";

    public string DisplayName => "Solarized Themes";

    public string Version => "1.0.0";

    public string Description => "Solarized Dark 主题（Ethan Schoonover 调色板）。";

    public IReadOnlyList<PluginSettingDescriptor> Settings => [];

    public void Initialize(IPluginContext context)
    {
    }

    public IReadOnlyList<PluginThemeDefinition> Themes { get; } =
        [new PluginThemeDefinition("Solarized Dark", "Solarized Dark", "SolarizedDark.xaml")];
}

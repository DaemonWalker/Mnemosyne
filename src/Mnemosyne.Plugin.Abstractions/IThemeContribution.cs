namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 主题贡献能力契约。插件类需同时实现 <see cref="IMnemosynePlugin"/>（提供身份与设置），
/// 主程序扫描后把 <see cref="Themes"/> 登记进主题服务：设置页主题下拉随之列出，
/// 切换时以 file URI 加载 <see cref="PluginThemeDefinition.XamlPath"/> 指向的资源字典，
/// UI（DynamicResource）与编辑器/终端配色随即热切换。
/// </summary>
public interface IThemeContribution
{
    /// <summary>本插件贡献的主题列表。</summary>
    IReadOnlyList<PluginThemeDefinition> Themes { get; }
}

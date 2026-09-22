namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 语言贡献能力契约。插件类需同时实现 <see cref="IMnemosynePlugin"/>（提供身份与设置），
/// 主程序扫描后把 <see cref="Languages"/> 登记进语言注册表：打开文件按文件名/扩展名匹配，
/// 语言选择器与状态栏随之列出。扩展名或精确文件名与已注册语言冲突时，该语言条目被忽略
/// 并记入插件日志（不连累插件的其他能力）。
/// </summary>
public interface ILanguageContribution
{
    /// <summary>本插件贡献的语言定义列表。</summary>
    IReadOnlyList<PluginLanguageDefinition> Languages { get; }
}

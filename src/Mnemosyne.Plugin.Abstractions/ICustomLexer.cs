namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 自定义词法器能力契约，用于 Lexilla 没有内置 Lexer 的语言。插件类需同时实现
/// <see cref="IMnemosynePlugin"/>；语言定义（<see cref="PluginLanguageDefinition.LexerName"/>）
/// 经 <see cref="Name"/> 引用本词法器后，主程序对该语言使用 container lexer，
/// 在 StyleNeeded 回调里调 <see cref="Tokenize"/> 分词并逐段上色。
/// 词法器名冲突时后到者被忽略并记入插件日志。
/// </summary>
public interface ICustomLexer
{
    /// <summary>词法器名（不区分大小写），如 "dataweave"。</summary>
    string Name { get; }

    /// <summary>样式表：<see cref="LexerStyleSpan.Style"/> 即本列表下标。颜色由语义映射到主题。</summary>
    IReadOnlyList<LexerStyle> Styles { get; }

    /// <summary>
    /// 对文本分词，产出顺序衔接的上色区间（长度之和应等于输入长度）。
    /// 实现必须是纯函数：不修改外部状态、不产生副作用。在 UI 线程的 StyleNeeded 回调中
    /// 被调用，输入为待着色区间（通常从行首到编辑点），应保持轻量。
    /// </summary>
    /// <param name="text">待分词文本。</param>
    IReadOnlyList<LexerStyleSpan> Tokenize(string text);
}

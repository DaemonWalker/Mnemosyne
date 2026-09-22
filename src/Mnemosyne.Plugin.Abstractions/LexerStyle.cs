namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 自定义词法器的一种样式：颜色由 <see cref="Semantic"/> 映射到主题色，<see cref="Bold"/> 单独声明。
/// <see cref="ICustomLexer.Tokenize"/> 产出的 <see cref="LexerStyleSpan.Style"/> 即
/// <see cref="ICustomLexer.Styles"/> 列表中本类的下标。
/// </summary>
public sealed class LexerStyle
{
    public LexerStyle(LexerStyleSemantic semantic, bool bold = false)
    {
        Semantic = semantic;
        Bold = bold;
    }

    /// <summary>语义类别（决定主题取色）。</summary>
    public LexerStyleSemantic Semantic { get; }

    /// <summary>是否加粗。</summary>
    public bool Bold { get; }
}

namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 自定义词法器分词产出的一段上色区间：连续 <paramref name="Length"/> 个字符应用同一样式。
/// 普通密封类而非 record struct（netstandard2.0 限制，见 notes.md §6）。
/// </summary>
public sealed class LexerStyleSpan
{
    public LexerStyleSpan(int length, int style)
    {
        Length = length;
        Style = style;
    }

    /// <summary>区间长度（字符数，&gt; 0）。</summary>
    public int Length { get; }

    /// <summary>样式下标（<see cref="ICustomLexer.Styles"/> 列表下标）。</summary>
    public int Style { get; }
}

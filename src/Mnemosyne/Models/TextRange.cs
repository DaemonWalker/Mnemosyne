namespace Mnemosyne.Models;

/// <summary>一段文本区间。页内搜索中用于 Scintilla 的 UTF-8 字节偏移区间。</summary>
public sealed record TextRange(int Start, int Length);

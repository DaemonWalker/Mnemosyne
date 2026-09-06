namespace Mnemosyne.Models;

/// <summary>大文件分块读取的一块文本；BytesRead 为截至本块已累计读取的字节数（进度计算用）。</summary>
public sealed record ReadChunk(string Text, long BytesRead);

namespace Mnemosyne.Models;

/// <summary>双击搜索结果后的跳转目标：文件路径 + 行号（1 起始）+ 行内字符区间。</summary>
public sealed record SearchResultLocation(string FullPath, int Line, int StartInLine, int Length);

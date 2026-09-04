namespace Mnemosyne.Models;

/// <summary>
/// 文件夹搜索中的一条匹配。Start/Length 是匹配在 LineText（不含行尾符）内的字符索引，
/// 跳转时按行首位置 + Start 换算成文档字符索引。
/// </summary>
public sealed record FileSearchMatch(int LineNumber, int Start, int Length, string LineText);

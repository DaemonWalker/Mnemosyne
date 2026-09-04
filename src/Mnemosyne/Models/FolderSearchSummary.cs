namespace Mnemosyne.Models;

/// <summary>
/// 文件夹搜索完成后的汇总。Truncated 表示总结果数达到上限、扫描提前停止；
/// Skipped 是无法读取的文件/目录数量（权限、占用等，不中断搜索）。
/// </summary>
public sealed record FolderSearchSummary(bool Truncated, int Skipped);

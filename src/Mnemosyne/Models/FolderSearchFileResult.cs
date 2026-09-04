namespace Mnemosyne.Models;

/// <summary>一个文件的分组结果。Truncated 表示该文件匹配数达到单文件上限被截断。</summary>
public sealed class FolderSearchFileResult
{
    public required string FullPath { get; init; }

    /// <summary>相对搜索根目录的路径（正斜杠分隔），供 UI 展示</summary>
    public required string RelativePath { get; init; }

    public required IReadOnlyList<FileSearchMatch> Matches { get; init; }

    public bool Truncated { get; init; }
}

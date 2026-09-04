namespace Mnemosyne.Models;

/// <summary>一次文件夹内搜索的参数快照（扫描在后台线程执行，先整体快照避免读取到半更新状态）。</summary>
public sealed record FolderSearchOptions
{
    public required string Query { get; init; }

    public bool MatchCase { get; init; }

    public bool WholeWord { get; init; }

    public bool UseRegex { get; init; }

    /// <summary>包含规则原文（VSCode 风格 glob，逗号分隔；空表示全部包含）</summary>
    public string IncludePatterns { get; init; } = "";

    /// <summary>排除规则原文（VSCode 风格 glob，逗号分隔）</summary>
    public string ExcludePatterns { get; init; } = "";
}

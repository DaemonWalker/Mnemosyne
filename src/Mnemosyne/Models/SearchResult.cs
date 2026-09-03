namespace Mnemosyne.Models;

/// <summary>一次页内搜索的结果。InvalidPattern 表示正则表达式非法（按无结果处理并提示）。</summary>
public sealed class SearchResult
{
    public static readonly SearchResult Empty = new() { Matches = [] };

    public static readonly SearchResult Invalid = new() { Matches = [], InvalidPattern = true };

    public required IReadOnlyList<SearchMatch> Matches { get; init; }

    /// <summary>匹配数达到 SearchService.MaxMatches 上限被截断</summary>
    public bool Truncated { get; init; }

    public bool InvalidPattern { get; init; }
}

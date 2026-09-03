using System.Text.RegularExpressions;
using Mnemosyne.Models;

namespace Mnemosyne.Services;

/// <summary>
/// 页内搜索逻辑（architecture.md 中 SearchService 的页内部分，文件夹扫描属 Step 6）。
/// 三种选项统一走 .NET Regex：字面量模式对关键字做 Regex.Escape，保证正则特殊字符按字面处理；
/// 全字匹配不用 \b，而是自行判定边界——词字符限定为 ASCII 字母/数字/下划线（与 Scintilla 默认一致），
/// 中文字符不是词字符，因此中文词的前后总是边界（中文没有词边界，这样语义才正确）。
/// 匹配结果为 .NET 字符索引，可直接用于 Scintilla5.NET 的 position API（其 position 单位是字符而非 UTF-8 字节）。
/// </summary>
public static class SearchService
{
    /// <summary>匹配数上限：超出后截断，避免大文档上高亮/导航数据量失控</summary>
    public const int MaxMatches = 50000;

    public static SearchResult FindMatches(string text, string query, bool matchCase, bool wholeWord, bool useRegex)
    {
        if (string.IsNullOrEmpty(query)) return SearchResult.Empty;

        Regex regex;
        try
        {
            regex = BuildRegex(query, matchCase, useRegex);
        }
        catch (ArgumentException)
        {
            return SearchResult.Invalid;
        }

        var matches = new List<SearchMatch>();
        bool truncated = false;
        foreach (Match match in regex.Matches(text))
        {
            if (match.Length == 0) continue;
            if (wholeWord && !IsWordBoundary(text, match.Index, match.Length)) continue;
            if (matches.Count >= MaxMatches)
            {
                truncated = true;
                break;
            }
            matches.Add(new SearchMatch(match.Index, match.Length, match.Value));
        }
        return new SearchResult { Matches = matches, Truncated = truncated };
    }

    /// <summary>
    /// 展开替换文本：正则模式下支持 $1 等分组引用（在匹配子串上重跑正则取分组）；
    /// 字面量模式替换文本原样使用（不解释 $）。重跑失败时回退为原文。
    /// </summary>
    public static string ExpandReplacement(string matchedText, string query, string replacement, bool matchCase, bool useRegex)
    {
        if (!useRegex) return replacement;
        try
        {
            Match match = BuildRegex(query, matchCase, useRegex: true).Match(matchedText);
            if (match.Success && match.Index == 0 && match.Length == matchedText.Length)
            {
                return match.Result(replacement);
            }
        }
        catch (ArgumentException)
        {
        }
        return matchedText;
    }

    // Multiline：让 ^/$ 按行边界匹配（编辑器通用预期）
    private static Regex BuildRegex(string query, bool matchCase, bool useRegex)
    {
        string pattern = useRegex ? query : Regex.Escape(query);
        RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Multiline;
        if (!matchCase) options |= RegexOptions.IgnoreCase;
        return new Regex(pattern, options);
    }

    private static bool IsWordBoundary(string text, int start, int length)
    {
        bool before = start == 0 || !IsWordChar(text[start - 1]);
        int end = start + length;
        bool after = end >= text.Length || !IsWordChar(text[end]);
        return before && after;
    }

    private static bool IsWordChar(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';
}

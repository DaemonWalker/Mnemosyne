using System.IO;
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

    /// <summary>文件夹搜索：单文件结果数上限（防极端文件撑爆结果树）</summary>
    public const int MaxFileMatches = 100;

    /// <summary>文件夹搜索：总结果数上限（达到后停止扫描并标记截断）</summary>
    public const int MaxTotalMatches = 10000;

    /// <summary>默认排除的目录名（不区分大小写），无需用户配置</summary>
    private static readonly HashSet<string> DefaultExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules",
    };

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

    /// <summary>构建页内/文件夹搜索共用的正则；非法模式返回 null 而非抛异常</summary>
    public static Regex? TryBuildRegex(string query, bool matchCase, bool useRegex)
    {
        try
        {
            return BuildRegex(query, matchCase, useRegex);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>全字匹配边界判定：词字符限定 ASCII 字母/数字/下划线，中文字符前后总是边界</summary>
    public static bool IsWordBoundary(string text, int start, int length)
    {
        bool before = start == 0 || !IsWordChar(text[start - 1]);
        int end = start + length;
        bool after = end >= text.Length || !IsWordChar(text[end]);
        return before && after;
    }

    /// <summary>span 版本的全字边界判定（文件夹扫描按行切片使用）</summary>
    public static bool IsWordBoundary(ReadOnlySpan<char> text, int start, int length)
    {
        bool before = start == 0 || !IsWordChar(text[start - 1]);
        int end = start + length;
        bool after = end >= text.Length || !IsWordChar(text[end]);
        return before && after;
    }

    /// <summary>
    /// 文件夹扫描：后台递归遍历 rootPath，glob 过滤包含/排除，跳过二进制与默认排除目录，
    /// 有匹配的文件按批次经 progress 增量推送（限频避免刷屏）。返回汇总；取消抛 OperationCanceledException。
    /// </summary>
    public static async Task<FolderSearchSummary> SearchFolderAsync(
        string rootPath,
        FolderSearchOptions options,
        FileService fileService,
        IProgress<IReadOnlyList<FolderSearchFileResult>> progress,
        CancellationToken cancellationToken)
    {
        Regex regex = TryBuildRegex(options.Query, options.MatchCase, options.UseRegex)
            ?? throw new ArgumentException("Invalid search pattern", nameof(options));
        GlobMatcher? includes = GlobMatcher.Parse(options.IncludePatterns);
        GlobMatcher? excludes = GlobMatcher.Parse(options.ExcludePatterns);

        int totalMatches = 0;
        int skipped = 0;
        bool truncated = false;
        var batch = new List<FolderSearchFileResult>();
        var throttle = System.Diagnostics.Stopwatch.StartNew();

        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0 && !truncated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            string[] subDirectories;
            string[] files;
            try
            {
                subDirectories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                continue;
            }

            foreach (string subDirectory in subDirectories)
            {
                if (DefaultExcludedDirectories.Contains(Path.GetFileName(subDirectory))) continue;
                try
                {
                    // 跳过联接点/符号链接，防目录循环
                    if ((File.GetAttributes(subDirectory) & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                    continue;
                }
                if (excludes?.Matches(ToRelative(rootPath, subDirectory), isDirectory: true) == true) continue;
                pending.Push(subDirectory);
            }

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = ToRelative(rootPath, file);
                if (includes is not null && !includes.Matches(relative, isDirectory: false)) continue;
                if (excludes?.Matches(relative, isDirectory: false) == true) continue;

                FolderSearchFileResult? result = null;
                try
                {
                    result = await SearchFileAsync(file, relative, regex, options.WholeWord, fileService,
                        MaxTotalMatches - totalMatches, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    skipped++;
                }
                if (result is null || result.Matches.Count == 0) continue;

                totalMatches += result.Matches.Count;
                truncated = totalMatches >= MaxTotalMatches;
                batch.Add(result);
                if (batch.Count >= 20 || throttle.ElapsedMilliseconds >= 100)
                {
                    progress.Report(batch);
                    batch = [];
                    throttle.Restart();
                }
                if (truncated) break;
            }
        }
        if (batch.Count > 0) progress.Report(batch);
        return new FolderSearchSummary(truncated, skipped);
    }

    /// <summary>搜索单个文件；二进制文件返回 null。per-file 与全局（remainingBudget）上限都会截断。</summary>
    private static async Task<FolderSearchFileResult?> SearchFileAsync(
        string path, string relativePath, Regex regex, bool wholeWord, FileService fileService,
        int remainingBudget, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (LooksBinary(bytes)) return null;
        string text = fileService.Decode(bytes).Text;

        int cap = Math.Min(MaxFileMatches, Math.Max(remainingBudget, 0));
        var matches = new List<FileSearchMatch>();
        bool fileTruncated = false;
        int lineNumber = 1;
        int lineStart = 0;
        while (lineStart <= text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int newline = text.IndexOf('\n', lineStart);
            int lineEnd = newline < 0 ? text.Length : newline;
            int contentEnd = lineEnd > lineStart && text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            // EnumerateMatches 直接在原文 span 上扫（不分配行子串），有匹配时才物化行文本
            ReadOnlySpan<char> lineSpan = text.AsSpan(lineStart, contentEnd - lineStart);
            string? line = null;
            foreach (ValueMatch m in regex.EnumerateMatches(lineSpan))
            {
                if (m.Length == 0) continue;
                if (wholeWord && !IsWordBoundary(lineSpan, m.Index, m.Length)) continue;
                if (matches.Count >= cap)
                {
                    fileTruncated = matches.Count >= MaxFileMatches;
                    break;
                }
                line ??= lineSpan.ToString();
                matches.Add(new FileSearchMatch(lineNumber, m.Index, m.Length, line));
            }
            if (fileTruncated || matches.Count >= cap) break;
            if (newline < 0) break;
            lineStart = newline + 1;
            lineNumber++;
        }

        if (matches.Count == 0) return null;
        return new FolderSearchFileResult
        {
            FullPath = path,
            RelativePath = relativePath,
            Matches = matches,
            Truncated = fileTruncated,
        };
    }

    /// <summary>二进制嗅探：头部含 NUL 字节判二进制；带 BOM 的 UTF-16/32 文本天然含 NUL，先按 BOM 豁免</summary>
    private static bool LooksBinary(byte[] bytes)
    {
        bool hasBom = bytes.Length >= 2
            && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)
                || (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF));
        if (hasBom) return false;
        int head = Math.Min(bytes.Length, 8000);
        for (int i = 0; i < head; i++)
        {
            if (bytes[i] == 0) return true;
        }
        return false;
    }

    private static string ToRelative(string rootPath, string path) =>
        Path.GetRelativePath(rootPath, path).Replace(Path.DirectorySeparatorChar, '/');

    // Multiline：让 ^/$ 按行边界匹配（编辑器通用预期）
    private static Regex BuildRegex(string query, bool matchCase, bool useRegex)
    {
        string pattern = useRegex ? query : Regex.Escape(query);
        RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Multiline;
        if (!matchCase) options |= RegexOptions.IgnoreCase;
        return new Regex(pattern, options);
    }

    private static bool IsWordChar(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';
}

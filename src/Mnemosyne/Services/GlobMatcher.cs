using System.Text;
using System.Text.RegularExpressions;

namespace Mnemosyne.Services;

/// <summary>
/// VSCode 风格 glob 匹配（逗号分隔多个模式，作用于相对搜索根目录、正斜杠分隔的路径）：
/// * 匹配段内任意字符，? 匹配段内单字符，** 跨目录层级。
/// 不含 / 的模式按任意层级的文件名匹配（如 *.cs）；含 / 的模式从根锚定（如 src/**）。
/// 以 /** 结尾的模式同时为目录生成裁剪规则（如 **/bin/** 可整体跳过 bin 目录）。
/// 匹配不区分大小写（Windows 文件系统语义）。
/// </summary>
public sealed class GlobMatcher
{
    private readonly List<Regex> _patterns = [];
    private readonly List<Regex> _directoryPatterns = [];

    private GlobMatcher()
    {
    }

    /// <summary>解析逗号分隔的模式串；空输入或全部被剔除时返回 null（表示无约束）</summary>
    public static GlobMatcher? Parse(string? patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns)) return null;
        var matcher = new GlobMatcher();
        foreach (string raw in patterns.Split(','))
        {
            string pattern = raw.Trim().Replace('\\', '/').TrimStart('/');
            if (pattern.Length == 0 || pattern == "**") continue;
            matcher._patterns.Add(new Regex(ToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            if (pattern.EndsWith("/**", StringComparison.Ordinal) && pattern.Length > 3)
            {
                matcher._directoryPatterns.Add(new Regex(ToRegex(pattern[..^3]), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }
        }
        return matcher._patterns.Count == 0 ? null : matcher;
    }

    /// <summary>判断相对路径是否命中任一模式；目录额外尝试 /** 裁剪规则</summary>
    public bool Matches(string relativePath, bool isDirectory)
    {
        foreach (Regex pattern in _patterns)
        {
            if (pattern.IsMatch(relativePath)) return true;
        }
        if (isDirectory)
        {
            foreach (Regex pattern in _directoryPatterns)
            {
                if (pattern.IsMatch(relativePath)) return true;
            }
        }
        return false;
    }

    private static string ToRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        if (!pattern.Contains('/'))
        {
            // 无 / 模式：任意层级文件名（VSCode 语义）
            builder.Append("(?:.*/)?");
        }
        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            if (c == '*')
            {
                int start = i;
                while (i < pattern.Length && pattern[i] == '*') i++;
                if (i - start >= 2)
                {
                    // "**/" 匹配零级或多级目录；结尾的 "**" 匹配任意剩余路径
                    if (i < pattern.Length && pattern[i] == '/')
                    {
                        builder.Append("(?:[^/]+/)*");
                        i++;
                    }
                    else
                    {
                        builder.Append(".*");
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                builder.Append("[^/]");
                i++;
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
                i++;
            }
        }
        builder.Append('$');
        return builder.ToString();
    }
}

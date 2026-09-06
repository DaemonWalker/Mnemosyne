namespace Mnemosyne.Services;

/// <summary>
/// 按文件内容统计行首缩进，猜测缩进风格（Tab/空格 + 宽度）。
/// 缩进行数不足时不做猜测（返回 null），调用方回退到设置项默认值。
/// </summary>
public static class IndentDetector
{
    // 只统计缩进行数达到该值才下结论，避免只有少数行缩进的文件被误判
    private const int MinIndentedLines = 3;

    // 常见宽度候选，打平时按列表顺序优先（4 最常用）
    private static readonly int[] CandidateWidths = [4, 2, 8, 3];

    private const int MaxLinesToScan = 10000;

    /// <summary>检测成功返回 (UseTabs, Width)，样本不足返回 null。Tab 文件宽度不可知，用 defaultWidth。</summary>
    public static (bool UseTabs, int Width)? Detect(string text, int defaultWidth)
    {
        int tabLines = 0;
        var spaceCounts = new List<int>();

        int scanned = 0;
        int lineStart = 0;
        while (lineStart < text.Length && scanned < MaxLinesToScan)
        {
            int lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = text.Length;
            scanned++;

            char first = text[lineStart];
            if (first == '\t')
            {
                tabLines++;
            }
            else if (first == ' ')
            {
                int spaces = 0;
                while (lineStart + spaces < lineEnd && text[lineStart + spaces] == ' ') spaces++;
                if (spaces > 0) spaceCounts.Add(spaces);
            }

            lineStart = lineEnd + 1;
        }

        if (tabLines + spaceCounts.Count < MinIndentedLines) return null;
        if (tabLines > spaceCounts.Count) return (true, defaultWidth);
        if (spaceCounts.Count == 0) return null;

        // 每个候选宽度统计能整除它的缩进行数，得分最高者胜出（打平取 CandidateWidths 靠前项）
        int bestWidth = CandidateWidths[0];
        int bestScore = -1;
        foreach (int width in CandidateWidths)
        {
            int score = spaceCounts.Count(n => n % width == 0);
            if (score > bestScore)
            {
                bestScore = score;
                bestWidth = width;
            }
        }
        return (false, bestWidth);
    }
}

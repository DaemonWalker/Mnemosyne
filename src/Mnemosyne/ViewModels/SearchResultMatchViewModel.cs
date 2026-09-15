using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemosyne.Models;

namespace Mnemosyne.ViewModels;

/// <summary>结果树中的一条匹配行。显示文本去掉行首空白（不越过匹配起点）并截断超长行；
/// 匹配落在截断窗口之外时窗口向右滑动，保证匹配始终可见。</summary>
public partial class SearchResultMatchViewModel : ObservableObject
{
    private const int MaxDisplayChars = 300;

    private readonly Action<SearchResultLocation> _open;

    public SearchResultMatchViewModel(string fullPath, FileSearchMatch match, Action<SearchResultLocation> open)
    {
        _open = open;
        Location = new SearchResultLocation(fullPath, match.LineNumber, match.Start, match.Length);
        LineNumberDisplay = match.LineNumber.ToString();

        string line = match.LineText;
        int trim = 0;
        while (trim < match.Start && trim < line.Length && char.IsWhiteSpace(line[trim])) trim++;

        // 钳制匹配范围，防御搜索后文件被修改导致的越界索引
        int matchStart = Math.Clamp(match.Start, trim, line.Length);
        int matchEndFull = Math.Clamp(match.Start + match.Length, matchStart, line.Length);

        // 匹配落在窗口右侧之外时向右滑动窗口，保证匹配完整可见
        int windowStart = Math.Min(Math.Max(trim, matchEndFull - MaxDisplayChars), matchStart);
        int windowEnd = Math.Min(line.Length, windowStart + MaxDisplayChars);
        int matchEnd = Math.Min(matchEndFull, windowEnd);

        PrefixText = line[windowStart..matchStart];
        MatchText = line[matchStart..matchEnd];
        SuffixText = line[matchEnd..windowEnd];
    }

    public SearchResultLocation Location { get; }

    public string LineNumberDisplay { get; }

    /// <summary>供 UIA/辅助功能读取的整行纯文本（Prefix+Match+Suffix）</summary>
    public string DisplayText => PrefixText + MatchText + SuffixText;

    public string PrefixText { get; }

    public string MatchText { get; }

    public string SuffixText { get; }

    [RelayCommand]
    private void Open() => _open(Location);
}

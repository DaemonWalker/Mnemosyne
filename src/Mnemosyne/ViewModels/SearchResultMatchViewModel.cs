using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemosyne.Models;

namespace Mnemosyne.ViewModels;

/// <summary>结果树中的一条匹配行。显示文本去掉行首空白（不越过匹配起点）并截断超长行。</summary>
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
        int displayEnd = Math.Min(line.Length, trim + MaxDisplayChars);
        int matchEnd = Math.Min(match.Start + match.Length, displayEnd);
        PrefixText = line[trim..match.Start];
        MatchText = line[match.Start..matchEnd];
        SuffixText = line[matchEnd..displayEnd];
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

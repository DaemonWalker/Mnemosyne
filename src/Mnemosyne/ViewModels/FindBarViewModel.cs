using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemosyne.Models;
using Mnemosyne.Services;

namespace Mnemosyne.ViewModels;

/// <summary>
/// 编辑器顶部浮层搜索/替换条。窗口级单例，跟随活动文档工作：切换 Tab 时清除旧文档高亮、
/// 在新文档上重搜（关键字与选项跨 Tab 保留，与 VSCode 一致）。
/// 匹配计算放后台线程（带版本号丢弃过期结果），高亮与选中回 UI 线程应用。
/// </summary>
public partial class FindBarViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly DispatcherTimer _debounce;

    private DocumentViewModel? _document;
    private List<SearchMatch> _matches = [];
    private int _currentIndex = -1;
    private bool _truncated;
    private bool _patternInvalid;
    private int _searchVersion;

    public FindBarViewModel(LocalizationService localization)
    {
        _localization = localization;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RunSearchAsync();
        };
        _localization.LanguageChanged += (_, _) => UpdateCountDisplay();
    }

    /// <summary>关闭搜索条后由 View 注入：把焦点还给编辑器</summary>
    public Action? FocusEditorRequested { get; set; }

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _replaceText = "";

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _wholeWord;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private bool _isReplaceVisible;

    [ObservableProperty]
    private string _countDisplay = "";

    /// <summary>计数区显示为错误色（无结果或正则非法）</summary>
    [ObservableProperty]
    private bool _isCountError;

    partial void OnSearchTextChanged(string value) => RunSearchSoon(immediate: false);

    partial void OnMatchCaseChanged(bool value) => RunSearchSoon(immediate: true);

    partial void OnWholeWordChanged(bool value) => RunSearchSoon(immediate: true);

    partial void OnUseRegexChanged(bool value) => RunSearchSoon(immediate: true);

    /// <summary>活动文档切换入口（MainWindowViewModel 在 ActiveDocument 变化时调用）</summary>
    public void AttachDocument(DocumentViewModel? document)
    {
        if (ReferenceEquals(_document, document)) return;
        if (_document is not null)
        {
            _document.ContentChanged -= OnDocumentContentChanged;
            _document.Editor.ClearSearchHighlights();
        }
        _document = document;
        ResetMatches();
        if (document is not null)
        {
            document.ContentChanged += OnDocumentContentChanged;
            if (IsVisible) RunSearchSoon(immediate: true);
        }
        UpdateCountDisplay();
    }

    /// <summary>Ctrl+F（replace=false）/Ctrl+H（replace=true）打开；有单行选中文本时带入搜索框</summary>
    public void Open(bool replace)
    {
        IsVisible = true;
        IsReplaceVisible = replace;
        if (_document is not null)
        {
            string selected = _document.Editor.SelectedText;
            if (!string.IsNullOrEmpty(selected) && !selected.Contains('\n'))
            {
                SearchText = selected;
            }
        }
        RunSearchSoon(immediate: true);
    }

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
        _document?.Editor.ClearSearchHighlights();
        ResetMatches();
        UpdateCountDisplay();
        FocusEditorRequested?.Invoke();
    }

    [RelayCommand]
    private void FindNext() => Navigate(1);

    [RelayCommand]
    private void FindPrevious() => Navigate(-1);

    [RelayCommand]
    private async Task ReplaceCurrentAsync()
    {
        DocumentViewModel? document = _document;
        if (document is null || document.Editor.IsReadOnly || _matches.Count == 0) return;

        // 当前选择恰落在当前匹配上才替换它，否则先定位到 caret 之后的第一个匹配
        int index = -1;
        TextRange selection = document.Editor.SelectionRange;
        if (_currentIndex >= 0 && _currentIndex < _matches.Count
            && selection.Start == _matches[_currentIndex].Start
            && selection.Length == _matches[_currentIndex].Length)
        {
            index = _currentIndex;
        }
        else
        {
            index = FirstMatchAtOrAfter(document.Editor.CaretPosition);
            if (index < 0) index = 0;
        }

        SearchMatch target = _matches[index];
        string replacement = SearchService.ExpandReplacement(
            target.Value, SearchText, ReplaceText, MatchCase, UseRegex);
        document.Editor.ReplaceRange(target.Start, target.Length, replacement);
        // ReplaceTarget 后 caret 落在新文本末尾，重搜后"当前匹配"自然指向其后一个匹配
        await RunSearchAsync();
        ApplyHighlights(selectCurrent: true);
        UpdateCountDisplay();
    }

    [RelayCommand]
    private async Task ReplaceAllAsync()
    {
        DocumentViewModel? document = _document;
        if (document is null || document.Editor.IsReadOnly || string.IsNullOrEmpty(SearchText)) return;

        // 以最新文本重新搜索，避免防抖窗口内的过期匹配
        string text = document.Editor.Text;
        string query = SearchText;
        string replacementText = ReplaceText;
        bool matchCase = MatchCase, wholeWord = WholeWord, useRegex = UseRegex;
        SearchResult result = await Task.Run(() => SearchService.FindMatches(text, query, matchCase, wholeWord, useRegex));
        if (!ReferenceEquals(document, _document) || result.InvalidPattern || result.Matches.Count == 0) return;

        // 逆序替换保证前面的字符偏移始终有效；包在一个撤销动作里，一次 Ctrl+Z 全部撤销
        document.Editor.BeginUndoAction();
        try
        {
            for (int i = result.Matches.Count - 1; i >= 0; i--)
            {
                SearchMatch match = result.Matches[i];
                string replacement = SearchService.ExpandReplacement(match.Value, query, replacementText, matchCase, useRegex);
                document.Editor.ReplaceRange(match.Start, match.Length, replacement);
            }
        }
        finally
        {
            document.Editor.EndUndoAction();
        }
        await RunSearchAsync();
        UpdateCountDisplay();
    }

    private void OnDocumentContentChanged(object? sender, EventArgs e)
    {
        if (IsVisible) RunSearchSoon(immediate: false);
    }

    private void RunSearchSoon(bool immediate)
    {
        if (!IsVisible) return;
        if (immediate)
        {
            _debounce.Stop();
            _ = RunSearchAsync();
        }
        else
        {
            _debounce.Stop();
            _debounce.Start();
        }
    }

    private async Task RunSearchAsync()
    {
        int version = ++_searchVersion;
        DocumentViewModel? document = _document;
        if (document is null || !IsVisible || string.IsNullOrEmpty(SearchText))
        {
            document?.Editor.ClearSearchHighlights();
            ResetMatches();
            UpdateCountDisplay();
            return;
        }

        string text = document.Editor.Text;
        string query = SearchText;
        bool matchCase = MatchCase, wholeWord = WholeWord, useRegex = UseRegex;
        SearchResult result = await Task.Run(() => SearchService.FindMatches(text, query, matchCase, wholeWord, useRegex));
        // 过期结果（期间关键字/文档又变了）直接丢弃
        if (version != _searchVersion || !ReferenceEquals(document, _document)) return;

        _matches = [.. result.Matches];
        _truncated = result.Truncated;
        _patternInvalid = result.InvalidPattern;

        if (result.InvalidPattern || _matches.Count == 0)
        {
            _currentIndex = -1;
            document.Editor.ClearSearchHighlights();
        }
        else
        {
            // 当前匹配：有选择时优先取覆盖选择的匹配（如刚导航选中的那个），否则取 caret 之后第一个，再回绕
            TextRange selection = document.Editor.SelectionRange;
            int anchor = selection.Length > 0 ? selection.Start : document.Editor.CaretPosition;
            _currentIndex = FirstMatchAtOrAfter(anchor);
            if (_currentIndex < 0) _currentIndex = 0;
            ApplyHighlights(selectCurrent: false);
        }
        UpdateCountDisplay();
    }

    private void Navigate(int delta)
    {
        DocumentViewModel? document = _document;
        if (document is null || _matches.Count == 0) return;

        int index;
        TextRange selection = document.Editor.SelectionRange;
        bool onCurrent = _currentIndex >= 0 && _currentIndex < _matches.Count
            && selection.Start == _matches[_currentIndex].Start
            && selection.Length == _matches[_currentIndex].Length;
        if (onCurrent)
        {
            index = (_currentIndex + delta + _matches.Count) % _matches.Count;
        }
        else if (delta > 0)
        {
            index = FirstMatchAtOrAfter(document.Editor.CaretPosition);
            if (index < 0) index = 0;
        }
        else
        {
            index = LastMatchEndingBefore(document.Editor.CaretPosition);
            if (index < 0) index = _matches.Count - 1;
        }

        _currentIndex = index;
        ApplyHighlights(selectCurrent: true);
        UpdateCountDisplay();
    }

    // _matches 按 Start 升序且不重叠（Regex.Matches 保证），二分定位
    private int FirstMatchAtOrAfter(int position)
    {
        int lo = 0, hi = _matches.Count - 1, result = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_matches[mid].Start >= position)
            {
                result = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }
        return result;
    }

    private int LastMatchEndingBefore(int position)
    {
        int lo = 0, hi = _matches.Count - 1, result = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            SearchMatch match = _matches[mid];
            if (match.Start + match.Length <= position)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return result;
    }

    private void ApplyHighlights(bool selectCurrent)
    {
        DocumentViewModel? document = _document;
        if (document is null) return;
        List<TextRange> ranges = _matches.Select(m => new TextRange(m.Start, m.Length)).ToList();
        document.Editor.SetSearchHighlights(ranges, _currentIndex);
        if (selectCurrent && _currentIndex >= 0 && _currentIndex < _matches.Count)
        {
            SearchMatch current = _matches[_currentIndex];
            document.Editor.SelectRange(current.Start, current.Length);
        }
    }

    private void ResetMatches()
    {
        _searchVersion++;
        _matches = [];
        _currentIndex = -1;
        _truncated = false;
        _patternInvalid = false;
        IsCountError = false;
    }

    private void UpdateCountDisplay()
    {
        if (!IsVisible || string.IsNullOrEmpty(SearchText))
        {
            CountDisplay = "";
            IsCountError = false;
            return;
        }
        if (_patternInvalid)
        {
            CountDisplay = _localization.GetString("Loc.Find.InvalidRegex");
            IsCountError = true;
            return;
        }
        if (_matches.Count == 0)
        {
            CountDisplay = _localization.GetString("Loc.Find.NoResults");
            IsCountError = true;
            return;
        }
        IsCountError = false;
        CountDisplay = $"{_currentIndex + 1}/{_matches.Count}{(_truncated ? "+" : "")}";
    }
}

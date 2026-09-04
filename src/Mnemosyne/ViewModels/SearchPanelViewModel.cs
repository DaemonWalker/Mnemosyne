using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemosyne.Models;
using Mnemosyne.Services;

namespace Mnemosyne.ViewModels;

/// <summary>
/// 侧边栏文件夹搜索面板。关键字/选项/包含/排除变化经防抖自动重搜（Enter 立即），
/// 新搜索取消旧搜索（CancellationToken），结果按批次增量进入可展开的结果树。
/// 面板无独立状态栏资源，错误与状态都收敛到 StatusText 与 ShowError 钩子。
/// </summary>
public partial class SearchPanelViewModel : ObservableObject
{
    private const int DebounceMilliseconds = 400;

    private readonly FileService _fileService;
    private readonly LocalizationService _localization;
    private readonly Func<string?> _folderRootProvider;
    private readonly DispatcherTimer _debounce;

    private CancellationTokenSource? _cancellation;
    private int _searchVersion;
    private int _totalMatches;
    private int _totalFiles;
    private int _skipped;
    private bool _truncated;
    private bool _cancelled;
    private bool _patternInvalid;

    public SearchPanelViewModel(FileService fileService, LocalizationService localization, Func<string?> folderRootProvider)
    {
        _fileService = fileService;
        _localization = localization;
        _folderRootProvider = folderRootProvider;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMilliseconds) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RunSearchAsync();
        };
        _localization.LanguageChanged += (_, _) => UpdateStatusText();
        ResultFiles.CollectionChanged += OnResultFilesChanged;
    }

    /// <summary>结果树的根集合（文件分组节点）</summary>
    public ObservableCollection<SearchResultFileViewModel> ResultFiles { get; } = [];

    /// <summary>双击结果请求跳转（MainWindowViewModel 注入）</summary>
    public Action<SearchResultLocation>? OpenMatchRequested { get; set; }

    public Action<string, string>? ShowError { get; set; }

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _wholeWord;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private string _includePattern = "";

    [ObservableProperty]
    private string _excludePattern = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelSearchCommand))]
    private bool _isSearching;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _hasSearched;

    public bool HasFolder => _folderRootProvider() is not null;

    public bool HasResults => ResultFiles.Count > 0;

    public bool ShowNoFolderHint => !HasFolder;

    public bool ShowEmptyHint => HasFolder && (!HasSearched || string.IsNullOrEmpty(SearchText));

    public bool ShowNoResultsHint => HasFolder && HasSearched && !IsSearching
        && !string.IsNullOrEmpty(SearchText) && ResultFiles.Count == 0 && !_patternInvalid;

    partial void OnSearchTextChanged(string value) => RunSearchSoon(immediate: false);

    partial void OnMatchCaseChanged(bool value) => RunSearchSoon(immediate: true);

    partial void OnWholeWordChanged(bool value) => RunSearchSoon(immediate: true);

    partial void OnUseRegexChanged(bool value) => RunSearchSoon(immediate: true);

    partial void OnIncludePatternChanged(string value) => RunSearchSoon(immediate: false);

    partial void OnExcludePatternChanged(string value) => RunSearchSoon(immediate: false);

    /// <summary>文件夹打开/关闭时由 MainWindowViewModel 调用：刷新提示态，文件夹没了就取消搜索并清空</summary>
    public void RefreshFolderState()
    {
        if (!HasFolder)
        {
            _cancellation?.Cancel();
            ResultFiles.Clear();
            HasSearched = false;
            IsSearching = false;
            UpdateStatusText();
        }
        UpdateStateFlags();
    }

    /// <summary>Enter 键：跳过防抖立即搜索</summary>
    [RelayCommand]
    private void SearchNow()
    {
        _debounce.Stop();
        _ = RunSearchAsync();
    }

    [RelayCommand(CanExecute = nameof(IsSearching))]
    private void CancelSearch() => _cancellation?.Cancel();

    private void OnResultFilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateStateFlags();

    private void RunSearchSoon(bool immediate)
    {
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
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        ResultFiles.Clear();
        _totalMatches = 0;
        _totalFiles = 0;
        _skipped = 0;
        _truncated = false;
        _cancelled = false;
        _patternInvalid = false;

        string? root = _folderRootProvider();
        string query = SearchText;
        if (root is null || string.IsNullOrEmpty(query))
        {
            HasSearched = false;
            IsSearching = false;
            UpdateStatusText();
            UpdateStateFlags();
            return;
        }
        if (SearchService.TryBuildRegex(query, MatchCase, UseRegex) is null)
        {
            HasSearched = true;
            _patternInvalid = true;
            IsSearching = false;
            UpdateStatusText();
            UpdateStateFlags();
            return;
        }

        var options = new FolderSearchOptions
        {
            Query = query,
            MatchCase = MatchCase,
            WholeWord = WholeWord,
            UseRegex = UseRegex,
            IncludePatterns = IncludePattern,
            ExcludePatterns = ExcludePattern,
        };
        // 过期批次（属于前一次搜索）直接丢弃
        var progress = new Progress<IReadOnlyList<FolderSearchFileResult>>(batch =>
        {
            if (version == _searchVersion) OnResultsBatch(batch);
        });

        HasSearched = true;
        IsSearching = true;
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        UpdateStatusText();
        UpdateStateFlags();

        try
        {
            FolderSearchSummary summary = await Task.Run(
                () => SearchService.SearchFolderAsync(root, options, _fileService, progress, token), token);
            if (version != _searchVersion) return;
            _truncated = summary.Truncated;
            _skipped = summary.Skipped;
        }
        catch (OperationCanceledException)
        {
            // 仅当属于当前搜索时才标记取消，避免覆盖新搜索已重置的状态
            if (version == _searchVersion) _cancelled = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError?.Invoke(
                string.Format(_localization.GetString("Loc.Error.SearchFolder.Message"), root, ex.Message),
                _localization.GetString("Loc.Error.Title"));
        }
        finally
        {
            if (version == _searchVersion)
            {
                IsSearching = false;
                UpdateStatusText();
                UpdateStateFlags();
            }
        }
    }

    private void OnResultsBatch(IReadOnlyList<FolderSearchFileResult> batch)
    {
        foreach (FolderSearchFileResult file in batch)
        {
            ResultFiles.Add(new SearchResultFileViewModel(file, location => OpenMatchRequested?.Invoke(location)));
            _totalMatches += file.Matches.Count;
            _totalFiles++;
        }
        UpdateStatusText();
    }

    private void UpdateStateFlags()
    {
        OnPropertyChanged(nameof(HasFolder));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ShowNoFolderHint));
        OnPropertyChanged(nameof(ShowEmptyHint));
        OnPropertyChanged(nameof(ShowNoResultsHint));
    }

    private void UpdateStatusText()
    {
        if (!HasFolder || !HasSearched)
        {
            StatusText = "";
            return;
        }
        if (_patternInvalid)
        {
            StatusText = _localization.GetString("Loc.Find.InvalidRegex");
            return;
        }
        string count = _truncated ? $"{_totalMatches}+" : _totalMatches.ToString();
        string stats = string.Format(_localization.GetString("Loc.Search.Status.Stats"), count, _totalFiles);
        if (IsSearching)
        {
            stats = _localization.GetString("Loc.Search.Status.Searching") + " " + stats;
        }
        else if (_cancelled)
        {
            stats += _localization.GetString("Loc.Search.Status.CancelledSuffix");
        }
        if (_skipped > 0)
        {
            stats += string.Format(_localization.GetString("Loc.Search.Status.SkippedSuffix"), _skipped);
        }
        StatusText = stats;
    }
}

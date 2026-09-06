using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemosyne.Models;
using Mnemosyne.Plugin.Abstractions;
using Mnemosyne.Services;

namespace Mnemosyne.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly FileService _fileService;
    private readonly LocalizationService _localization;
    private readonly ConfigService _configService;
    private readonly PluginService _pluginService;
    private readonly AppSettings _settings;

    private GridLength _lastSidebarWidth = new(260);

    public MainWindowViewModel(FileService fileService, LocalizationService localization, ConfigService configService, RecentFilesService recentFiles, PluginService pluginService)
    {
        _fileService = fileService;
        _localization = localization;
        _configService = configService;
        _pluginService = pluginService;
        _settings = configService.Settings;
        _wordWrap = _settings.WordWrap;
        _showWhitespace = _settings.ShowWhitespace;
        RecentFiles = recentFiles;
        FileTree = new FileTreeViewModel(localization);
        FileTree.OpenFileRequested = path => _ = OpenDocumentAsync(path);
        FindBar = new FindBarViewModel(localization);
        SearchPanel = new SearchPanelViewModel(fileService, localization, () => FileTree.RootNode?.FullPath);
        SearchPanel.OpenMatchRequested = location => _ = OpenSearchMatchAsync(location);
        SearchPanel.ShowError = (message, title) => ShowError?.Invoke(message, title);
        FileTree.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileTreeViewModel.RootNode)) SearchPanel.RefreshFolderState();
        };
        Documents.CollectionChanged += OnDocumentsChanged;
    }

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    public FileTreeViewModel FileTree { get; }

    public RecentFilesService RecentFiles { get; }

    /// <summary>页内搜索/替换浮层（窗口级，跟随活动文档）</summary>
    public FindBarViewModel FindBar { get; }

    /// <summary>侧边栏文件夹搜索面板</summary>
    public SearchPanelViewModel SearchPanel { get; }

    // 以下钩子由 View 注入，承载对话框等纯 UI 交互，业务流转保持在本类中
    public Func<IReadOnlyList<string>?>? OpenFilePicker { get; set; }

    public Func<string?>? OpenFolderPicker { get; set; }

    public Func<string, string?>? SaveFilePicker { get; set; }

    public Func<DocumentViewModel, SavePromptResult>? ConfirmUnsavedClose { get; set; }

    public Func<bool>? ConfirmEncodingReload { get; set; }

    /// <summary>大文件加载被取消后询问：返回 true 关闭该 Tab，false 保留已加载部分</summary>
    public Func<DocumentViewModel, bool>? ConfirmCancelledLoad { get; set; }

    /// <summary>截断文档（部分加载）保存到原路径前询问：返回 true 走另存为，false 取消保存</summary>
    public Func<DocumentViewModel, bool>? ConfirmPartialSave { get; set; }

    public Action<string, string>? ShowError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarVisible))]
    [NotifyPropertyChangedFor(nameof(IsFilePanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsSearchPanelVisible))]
    private ActivityPanel? _activePanel = Models.ActivityPanel.Files;

    // 与侧边栏列宽双向绑定：拖动分隔条改这里，收起/展开时由 ViewModel 置 0 或恢复
    [ObservableProperty]
    private GridLength _sidebarWidth = new(260);

    // 视图菜单开关：立即应用到所有已打开文档并写回设置持久化
    [ObservableProperty]
    private bool _wordWrap;

    [ObservableProperty]
    private bool _showWhitespace;

    // 大文件加载进度（状态栏右侧区域）；同一时刻只允许一个大文件在加载（门闸串行化）
    [ObservableProperty]
    private bool _loadProgressVisible;

    [ObservableProperty]
    private int _loadProgressPercent;

    [ObservableProperty]
    private string _loadProgressText = string.Empty;

    private DocumentViewModel? _loadingDocument;
    private readonly SemaphoreSlim _largeLoadGate = new(1, 1);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveDocument))]
    [NotifyCanExecuteChangedFor(nameof(SaveActiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveActiveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseActiveTabCommand))]
    private DocumentViewModel? _activeDocument;

    public bool IsSidebarVisible => ActivePanel is not null;

    public bool IsFilePanelVisible => ActivePanel == Models.ActivityPanel.Files;

    public bool IsSearchPanelVisible => ActivePanel == Models.ActivityPanel.Search;

    public bool HasOpenDocuments => Documents.Count > 0;

    public bool HasActiveDocument => ActiveDocument is not null;

    public bool ShowEmptyState => Documents.Count == 0;

    partial void OnActivePanelChanged(ActivityPanel? value)
    {
        SidebarWidth = value is null ? new GridLength(0) : _lastSidebarWidth;
    }

    partial void OnActiveDocumentChanged(DocumentViewModel? value)
    {
        FindBar.AttachDocument(value);
    }

    partial void OnSidebarWidthChanged(GridLength value)
    {
        if (value.Value > 0) _lastSidebarWidth = value;
    }

    partial void OnWordWrapChanged(bool value)
    {
        foreach (DocumentViewModel doc in Documents) doc.Editor.SetWordWrap(value);
        _settings.WordWrap = value;
        SaveSettings();
    }

    partial void OnShowWhitespaceChanged(bool value)
    {
        foreach (DocumentViewModel doc in Documents) doc.Editor.SetViewWhitespace(value);
        _settings.ShowWhitespace = value;
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            _configService.Save();
        }
        catch (Exception ex)
        {
            ShowError?.Invoke(
                string.Format(_localization.GetString("Loc.Error.SaveSettings.Message"), ex.Message),
                _localization.GetString("Loc.Error.Title"));
        }
    }

    [RelayCommand]
    private void ToggleActivity(ActivityPanel panel)
    {
        ActivePanel = ActivePanel == panel ? null : panel;
    }

    [RelayCommand]
    private void ShowSearchPanel()
    {
        ActivePanel = Models.ActivityPanel.Search;
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        IReadOnlyList<string>? paths = OpenFilePicker?.Invoke();
        if (paths is not null) await OpenPathsAsync(paths);
    }

    public async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                OpenFolder(path);
            }
            else
            {
                await OpenDocumentAsync(path);
            }
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        string? path = OpenFolderPicker?.Invoke();
        if (path is not null) OpenFolder(path);
    }

    /// <summary>打开文件夹到侧边栏文件树并记录最近列表</summary>
    public void OpenFolder(string path)
    {
        FileTree.OpenFolder(path);
        if (FileTree.HasFolder)
        {
            RecentFiles.RecordFolder(Path.GetFullPath(path));
            ActivePanel = Models.ActivityPanel.Files;
        }
    }

    [RelayCommand]
    private async Task OpenRecentFileAsync(string? path)
    {
        if (path is not null) await OpenDocumentAsync(path);
    }

    [RelayCommand]
    private void OpenRecentFolder(string? path)
    {
        if (path is not null) OpenFolder(path);
    }

    /// <summary>打开文件到新 Tab；同路径已打开时聚焦既有 Tab。返回打开的文档，失败返回 null。</summary>
    public async Task<DocumentViewModel?> OpenDocumentAsync(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return null;
        }

        DocumentViewModel? existing = Documents.FirstOrDefault(d =>
            string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActiveDocument = existing;
            return existing;
        }

        // 目录由 OpenPathsAsync 路由到文件树，这里兜底忽略
        if (Directory.Exists(fullPath)) return null;
        if (!File.Exists(fullPath))
        {
            ReportError("Loc.Error.OpenFile.Message", fullPath, _localization.GetString("Loc.Dialog.Confirm.Title"));
            return null;
        }

        var document = new DocumentViewModel(_fileService, _localization, _settings);

        // 8.1 阈值判断：超过设置阈值进入大文件模式（边读边显示 + 进度条 + 可取消）
        long fileSize = new FileInfo(fullPath).Length;
        if (fileSize > (long)_settings.LargeFileThresholdMB * 1024 * 1024)
        {
            Documents.Add(document);
            ActiveDocument = document;
            RecentFiles.RecordFile(fullPath);
            _ = LoadLargeDocumentAsync(document, fullPath, forcedEncoding: null);
            return document;
        }

        try
        {
            await document.LoadFromFileAsync(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ReportError("Loc.Error.OpenFile.Message", fullPath, ex.Message);
            return null;
        }

        Documents.Add(document);
        ActiveDocument = document;
        RecentFiles.RecordFile(fullPath);
        return document;
    }

    /// <summary>大文件加载驱动：进度上报状态栏；取消后询问关闭 Tab 或保留已加载部分；并发大文件经门闸串行</summary>
    private async Task LoadLargeDocumentAsync(DocumentViewModel document, string path, Encoding? forcedEncoding)
    {
        long fileSize = new FileInfo(path).Length;
        await _largeLoadGate.WaitAsync();
        // 对话框（取消询问/错误提示）是模态的，必须先结束进度显示并释放门闸再弹，故延迟到 finally 之后处理
        bool cancelled = false;
        string? ioError = null;
        try
        {
            // 等待门闸期间 Tab 可能已被关闭
            if (!Documents.Contains(document)) return;
            _loadingDocument = document;
            LoadProgressPercent = 0;
            LoadProgressText = string.Format(_localization.GetString("Loc.Status.LoadingFile"), Path.GetFileName(path));
            LoadProgressVisible = true;

            var progress = new Progress<int>(p =>
            {
                LoadProgressPercent = p;
                LoadProgressText = string.Format(
                    _localization.GetString("Loc.Status.LoadingFileProgress"), Path.GetFileName(path), p);
            });
            await document.LoadLargeFileAsync(path, fileSize, forcedEncoding, progress);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ioError = ex.Message;
        }
        finally
        {
            _loadingDocument = null;
            LoadProgressVisible = false;
            _largeLoadGate.Release();
        }

        if (cancelled)
        {
            // Tab 已在加载期间被关闭时不再询问
            if (Documents.Contains(document))
            {
                if (ConfirmCancelledLoad?.Invoke(document) ?? true)
                {
                    RemoveDocument(document);
                }
                else
                {
                    document.KeepPartialLoad();
                }
            }
        }
        if (ioError is not null)
        {
            if (Documents.Contains(document)) RemoveDocument(document);
            ReportError("Loc.Error.OpenFile.Message", path, ioError);
        }
    }

    [RelayCommand]
    private void CancelLoad()
    {
        _loadingDocument?.CancelLoad();
    }

    /// <summary>从 Tab 集合移除文档（不触发脏确认；调用方负责先行确认）</summary>
    private void RemoveDocument(DocumentViewModel document)
    {
        int index = Documents.IndexOf(document);
        if (index < 0) return;
        Documents.Remove(document);
        if (ReferenceEquals(ActiveDocument, document) && Documents.Count > 0)
        {
            ActiveDocument = Documents[Math.Min(index, Documents.Count - 1)];
        }
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private Task<bool> SaveActiveAsync()
    {
        return SaveDocumentAsync(ActiveDocument!, forcePicker: false);
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private Task<bool> SaveActiveAsAsync()
    {
        return SaveDocumentAsync(ActiveDocument!, forcePicker: true);
    }

    /// <summary>保存文档；无路径或强制另存时弹保存对话框。返回是否真的保存成功。</summary>
    public async Task<bool> SaveDocumentAsync(DocumentViewModel document, bool forcePicker)
    {
        string? path = forcePicker ? null : document.FilePath;

        // 截断文档（大文件加载被取消后只保留了部分内容）直接保存会用不完整内容覆盖原文件，
        // 必须先询问：另存为新文件或取消
        if (document.IsPartialLoad && !forcePicker && path is not null)
        {
            if (ConfirmPartialSave?.Invoke(document) != true) return false;
            path = null;
        }

        if (path is null)
        {
            path = SaveFilePicker?.Invoke(document.Title);
            if (path is null) return false;
        }

        try
        {
            await document.SaveAsync(path);
            document.IsPartialLoad = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ReportError("Loc.Error.SaveFile.Message", path, ex.Message);
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private Task<bool> CloseActiveTabAsync()
    {
        return CloseDocumentAsync(ActiveDocument!);
    }

    /// <summary>Tab 头部关闭按钮入口（参数为目标文档）</summary>
    [RelayCommand]
    private Task<bool> CloseDocument(DocumentViewModel document)
    {
        return CloseDocumentAsync(document);
    }

    /// <summary>关闭文档 Tab；脏文档先询问保存。返回是否已关闭（用户取消返回 false）。</summary>
    public async Task<bool> CloseDocumentAsync(DocumentViewModel document)
    {
        // 加载中的大文件直接取消并关闭（用户已明确要求关闭，不再询问保留部分）
        if (document.IsLoading)
        {
            document.CancelLoad();
            RemoveDocument(document);
            return true;
        }

        if (document.IsDirty && ConfirmUnsavedClose is not null)
        {
            switch (ConfirmUnsavedClose(document))
            {
                case SavePromptResult.Save:
                    if (!await SaveDocumentAsync(document, forcePicker: false)) return false;
                    break;
                case SavePromptResult.Cancel:
                    return false;
                case SavePromptResult.DontSave:
                    break;
            }
        }

        RemoveDocument(document);
        return true;
    }

    /// <summary>Tab 拖拽排序：把文档移动到目标索引</summary>
    public void MoveDocument(DocumentViewModel document, int newIndex)
    {
        int oldIndex = Documents.IndexOf(document);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Documents.Count || oldIndex == newIndex) return;
        Documents.Move(oldIndex, newIndex);
    }

    /// <summary>关闭除指定文档外的所有 Tab；脏文档确认被取消时中止</summary>
    public async Task CloseOthersAsync(DocumentViewModel document)
    {
        foreach (DocumentViewModel doc in Documents.Where(d => !ReferenceEquals(d, document)).ToList())
        {
            if (!await CloseDocumentAsync(doc)) break;
        }
    }

    /// <summary>关闭指定文档右侧的所有 Tab；脏文档确认被取消时中止</summary>
    public async Task CloseToRightAsync(DocumentViewModel document)
    {
        int index = Documents.IndexOf(document);
        if (index < 0) return;
        foreach (DocumentViewModel doc in Documents.Skip(index + 1).ToList())
        {
            if (!await CloseDocumentAsync(doc)) break;
        }
    }

    /// <summary>切换活动文档编码：脏文档先确认，确认后按新编码从磁盘重载</summary>
    public async Task SwitchEncodingAsync(Encoding encoding)
    {
        DocumentViewModel? document = ActiveDocument;
        if (document is null || document.FilePath is null) return;
        if (EncodingCatalog.SameAs(document.CurrentEncoding, encoding)) return;
        if (document.IsLoading) return;
        if (document.IsDirty && ConfirmEncodingReload?.Invoke() != true) return;

        // 大文件按新编码重载同样走分块加载，避免一次性读入卡 UI
        if (new FileInfo(document.FilePath).Length > (long)_settings.LargeFileThresholdMB * 1024 * 1024)
        {
            _ = LoadLargeDocumentAsync(document, document.FilePath, encoding);
            return;
        }

        try
        {
            await document.ReloadWithEncodingAsync(encoding);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ReportError("Loc.Error.OpenFile.Message", document.FilePath, ex.Message);
        }
    }

    /// <summary>双击搜索结果：打开文件 Tab（已在编辑的聚焦既有 Tab）、跳行并选中匹配文本</summary>
    private async Task OpenSearchMatchAsync(SearchResultLocation location)
    {
        DocumentViewModel? document = await OpenDocumentAsync(location.FullPath);
        if (document is null) return;
        document.GoToMatch(location.Line, location.StartInLine, location.Length);
        document.Editor.FocusEditor();
    }

    /// <summary>当前活动文档是否有可用的格式化插件（供菜单/右键菜单启用态查询，实时计算）</summary>
    public bool CanFormatActiveDocument =>
        ActiveDocument is { IsLoading: false } document &&
        !document.Editor.IsReadOnly &&
        _pluginService.FindFormatter(document.Language.FormatterId) is not null;

    /// <summary>
    /// 格式化活动文档：先经插件 Format 成功后再用单个撤销动作替换全文；
    /// 失败（插件抛异常）只弹本地化错误提示，原文不受影响。
    /// </summary>
    public async Task FormatActiveDocumentAsync()
    {
        DocumentViewModel? document = ActiveDocument;
        if (document is null || document.IsLoading || document.Editor.IsReadOnly) return;
        ICodeFormatter? formatter = _pluginService.FindFormatter(document.Language.FormatterId);
        if (formatter is null) return;

        string input = document.Editor.Text;
        var options = new FormatterOptions
        {
            UseTabs = document.IndentUseTabs,
            IndentWidth = document.IndentWidth,
        };

        string formatted;
        try
        {
            // 大文档格式化可能超过 50ms，放后台线程；异常（含 FormatterException）在此统一兜底
            formatted = await Task.Run(() => formatter.Format(input, options));
        }
        catch (Exception ex)
        {
            ShowError?.Invoke(
                string.Format(_localization.GetString("Loc.Error.Format.Message"), formatter.DisplayName, ex.Message),
                _localization.GetString("Loc.Error.Title"));
            return;
        }

        document.ReplaceAllText(formatted);
    }

    private void ReportError(string messageKey, string path, string detail)
    {
        ShowError?.Invoke(
            string.Format(_localization.GetString(messageKey), path, detail),
            _localization.GetString("Loc.Error.Title"));
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasOpenDocuments));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}

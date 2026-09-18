using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
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
    private readonly ThemeService _themeService;
    private readonly PluginService _pluginService;
    private readonly MarkdownRenderService _markdownRenderer;
    private readonly SessionService _sessionService;
    private readonly AppSettings _settings;

    private GridLength _lastSidebarWidth = new(260);
    private bool _restoringSession;
    private DispatcherTimer? _sessionDebounce;
    private readonly HashSet<DocumentViewModel> _externalPrompting = [];
    // 正在打开中的路径（异步加载期间去重），防止同一文件从文件夹/查找等入口并发打开出重复 Tab
    private readonly Dictionary<string, Task<DocumentViewModel?>> _openingDocuments = new(StringComparer.OrdinalIgnoreCase);
    // 监听活动文档的 DisplayTitle 变化（另存为/重命名/切换文件夹后同步窗口标题）
    private DocumentViewModel? _titleSubscribed;

    public MainWindowViewModel(FileService fileService, LocalizationService localization, ConfigService configService, ThemeService themeService, RecentFilesService recentFiles, PluginService pluginService, MarkdownRenderService markdownRenderer, SessionService sessionService)
    {
        _fileService = fileService;
        _localization = localization;
        _configService = configService;
        _themeService = themeService;
        _pluginService = pluginService;
        _markdownRenderer = markdownRenderer;
        _sessionService = sessionService;
        _settings = configService.Settings;
        _wordWrap = _settings.WordWrap;
        _showWhitespace = _settings.ShowWhitespace;
        RecentFiles = recentFiles;
        FileTree = new FileTreeViewModel(localization, _settings);
        FileTree.OpenFileRequested = path => _ = OpenDocumentAsync(path);
        FindBar = new FindBarViewModel(localization);
        SearchPanel = new SearchPanelViewModel(fileService, localization, () => FileTree.RootNode?.FullPath);
        SearchPanel.OpenMatchRequested = location => _ = OpenSearchMatchAsync(location);
        SearchPanel.ShowError = (message, title) => ShowError?.Invoke(message, title);
        FileTree.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileTreeViewModel.RootNode))
            {
                SearchPanel.RefreshFolderState();
                UpdateDocumentDisplayPaths();
                SaveSession();
                UpdateWindowTitle();
            }
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

    /// <summary>干净文档被外部修改后询问是否重新加载：true 重新加载，false 保留当前内容</summary>
    public Func<DocumentViewModel, bool>? ConfirmExternalReload { get; set; }

    /// <summary>脏文档与外部修改冲突时询问：true 重新加载（丢弃未保存修改），false 保留</summary>
    public Func<DocumentViewModel, bool>? ConfirmExternalConflict { get; set; }

    /// <summary>磁盘文件被外部删除（且文档不脏）时告知用户</summary>
    public Action<DocumentViewModel>? NotifyExternalDeleted { get; set; }

    public Action<string, string>? ShowError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarVisible))]
    [NotifyPropertyChangedFor(nameof(IsFilePanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsSearchPanelVisible))]
    private ActivityPanel? _activePanel = Models.ActivityPanel.Files;

    // 与侧边栏列宽双向绑定：拖动分隔条改这里，收起/展开时由 ViewModel 置 0 或恢复
    [ObservableProperty]
    private GridLength _sidebarWidth = new(260);

    // 终端面板：首次展开时懒创建（不进冷启动路径）；VSCode 式显隐——隐藏仅收起视图，会话保留
    [ObservableProperty]
    private bool _isTerminalVisible;

    // 面板行高由 MainWindow code-behind 命令式设置（规避绑定 DataBind 优先级竞态），这里只记忆上次高度
    private GridLength _lastTerminalHeight = new(240);

    private TerminalViewModel? _terminal;

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
    [NotifyPropertyChangedFor(nameof(CanSaveActive))]
    [NotifyPropertyChangedFor(nameof(CanPreviewActiveDocument))]
    [NotifyCanExecuteChangedFor(nameof(SaveActiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveActiveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseActiveTabCommand))]
    private DocumentViewModel? _activeDocument;

    // 窗口标题：活动文档相对路径 > 打开的文件夹名 > 仅应用名
    [ObservableProperty]
    private string _windowTitle = "Mnemosyne";

    public bool IsSidebarVisible => ActivePanel is not null;

    public bool IsFilePanelVisible => ActivePanel == Models.ActivityPanel.Files;

    public bool IsSearchPanelVisible => ActivePanel == Models.ActivityPanel.Search;

    public bool HasOpenDocuments => Documents.Count > 0;

    public bool HasActiveDocument => ActiveDocument is not null;

    /// <summary>预览 Tab 无内容可保存，保存/另存命令对其禁用</summary>
    public bool CanSaveActive => ActiveDocument is not null and not MarkdownPreviewViewModel;

    /// <summary>当前活动文档可打开 Markdown 预览（非预览 Tab、语言为 Markdown、不在大文件加载中）</summary>
    public bool CanPreviewActiveDocument =>
        ActiveDocument is { IsLoading: false } document &&
        document is not MarkdownPreviewViewModel &&
        IsMarkdown(document);

    private static bool IsMarkdown(DocumentViewModel document) =>
        string.Equals(document.Language.LexerName, "markdown", StringComparison.OrdinalIgnoreCase);

    /// <summary>为活动 Markdown 文档打开预览 Tab；已有对应预览时聚焦既有 Tab</summary>
    public void OpenMarkdownPreview()
    {
        if (!CanPreviewActiveDocument) return;
        DocumentViewModel source = ActiveDocument!;
        MarkdownPreviewViewModel? existing = Documents.OfType<MarkdownPreviewViewModel>()
            .FirstOrDefault(p => ReferenceEquals(p.Source, source));
        if (existing is not null)
        {
            ActiveDocument = existing;
            return;
        }

        var preview = new MarkdownPreviewViewModel(
            source, _markdownRenderer, _fileService, _localization, _settings, _sessionService,
            path => _ = OpenDocumentAsync(path),
            (message, title) => ShowError?.Invoke(message, title));
        Documents.Add(preview);
        ActiveDocument = preview;
    }

    public bool ShowEmptyState => Documents.Count == 0;

    partial void OnActivePanelChanged(ActivityPanel? value)
    {
        SidebarWidth = value is null ? new GridLength(0) : _lastSidebarWidth;
    }

    partial void OnActiveDocumentChanged(DocumentViewModel? value)
    {
        if (_titleSubscribed is not null) _titleSubscribed.PropertyChanged -= OnActiveDocumentPropertyChanged;
        _titleSubscribed = value;
        if (value is not null) value.PropertyChanged += OnActiveDocumentPropertyChanged;
        // 预览 Tab 没有可编辑内容，页内搜索条不挂到它
        FindBar.AttachDocument(value is MarkdownPreviewViewModel ? null : value);
        SaveSession();
        UpdateWindowTitle();
    }

    private void OnActiveDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.DisplayTitle)) UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        if (ActiveDocument is not null)
        {
            WindowTitle = "Mnemosyne - " + ActiveDocument.DisplayTitle;
        }
        else if (FileTree.RootNode is { } root)
        {
            WindowTitle = "Mnemosyne - " + Path.GetFileName(root.FullPath.TrimEnd(Path.DirectorySeparatorChar));
        }
        else
        {
            WindowTitle = "Mnemosyne";
        }
    }

    // ===== 文档显示路径（Tab 标签/窗口标题用） =====

    /// <summary>按当前工作区根目录重算所有文档的显示路径（打开/切换文件夹时调用）</summary>
    private void UpdateDocumentDisplayPaths()
    {
        foreach (DocumentViewModel doc in Documents)
        {
            doc.RelativePath = ComputeDisplayPath(doc.FilePath);
        }
        UpdateDuplicateTitleFlags();
    }

    /// <summary>文件在工作区内时返回相对路径，否则返回完整路径；无路径文档返回 null</summary>
    private string? ComputeDisplayPath(string? filePath)
    {
        if (filePath is null) return null;
        string? root = FileTree.RootNode?.FullPath;
        if (root is null) return filePath;
        string relative = Path.GetRelativePath(root, filePath);
        bool outsideRoot = relative is ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        return outsideRoot ? filePath : relative;
    }

    partial void OnSidebarWidthChanged(GridLength value)
    {
        if (value.Value > 0) _lastSidebarWidth = value;
    }

    // ===== 终端面板 =====

    /// <summary>终端面板 ViewModel，首次展开时才创建</summary>
    public TerminalViewModel? Terminal => _terminal;

    public void ToggleTerminal()
    {
        if (!IsTerminalVisible && _terminal is null)
        {
            _terminal = new TerminalViewModel(_localization, _settings, GetTerminalWorkingDirectory);
            OnPropertyChanged(nameof(Terminal));
        }
        IsTerminalVisible = !IsTerminalVisible;
    }

    /// <summary>展开时应用的行高；上次高度被拖得过扁（放不下工具条+一行终端）时回到默认，避免面板卡死在不可用高度</summary>
    public GridLength GetTerminalHeightToRestore()
    {
        if (_lastTerminalHeight.Value < 120) _lastTerminalHeight = new GridLength(240);
        return _lastTerminalHeight;
    }

    /// <summary>分隔条拖动完成后记忆行高（供下次展开恢复）</summary>
    public void RememberTerminalHeight(GridLength height)
    {
        if (height.Value > 0) _lastTerminalHeight = height;
    }

    // 工作目录：已打开文件夹 > 活动文档目录 > exe 目录
    private string GetTerminalWorkingDirectory() =>
        FileTree.RootNode?.FullPath
        ?? (ActiveDocument?.FilePath is { } path ? Path.GetDirectoryName(path) : null)
        ?? AppContext.BaseDirectory;

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

    // ===== 设置批量应用（设置窗口"保存设定"时调用一次，只落盘一次 settings.json） =====

    /// <summary>UI 字体/字号应用钩子，由 View 注入（作用于窗口根，沿可视树继承）</summary>
    public Action? ApplyUiFontSettings { get; set; }

    /// <summary>一次性应用设置窗口的全部修改。WordWrap/ShowWhitespace 由视图菜单直改，不在此列</summary>
    public void ApplyAllSettings(AppSettings settings)
    {
        _settings.FontFamily = settings.FontFamily;
        _settings.FontSize = settings.FontSize;
        _settings.Theme = settings.Theme;
        _settings.Language = settings.Language;
        _settings.IndentUseTabs = settings.IndentUseTabs;
        _settings.IndentWidth = settings.IndentWidth;
        _settings.LargeFileThresholdMB = settings.LargeFileThresholdMB;
        _settings.HideDotFiles = settings.HideDotFiles;
        _settings.HideHiddenFiles = settings.HideHiddenFiles;
        _settings.UiFontFamily = settings.UiFontFamily;
        _settings.UiFontSize = settings.UiFontSize;
        _settings.AllowMultipleInstances = settings.AllowMultipleInstances;
        _settings.ShellFileContextMenu = settings.ShellFileContextMenu;
        _settings.ShellFolderContextMenu = settings.ShellFolderContextMenu;
        _settings.TerminalShell = settings.TerminalShell;

        foreach (DocumentViewModel doc in Documents)
        {
            doc.Editor.ApplyFont(settings.FontFamily, settings.FontSize);
            doc.SetIndentation(settings.IndentUseTabs, settings.IndentWidth);
            if (doc is MarkdownPreviewViewModel preview) preview.RefreshFonts();
        }
        _themeService.ApplyTheme(settings.Theme);
        _localization.SetLanguage(settings.Language);
        ApplyUiFontSettings?.Invoke();
        FileTree.RefreshVisible();

        // 右键菜单文本按当前界面语言写入注册表；失败不阻断其余设置落盘
        try
        {
            ShellIntegrationService.Apply(
                settings.ShellFileContextMenu,
                settings.ShellFolderContextMenu,
                _localization.GetString("Loc.Shell.OpenFile"),
                _localization.GetString("Loc.Shell.OpenFolder"));
        }
        catch (Exception ex)
        {
            ShowError?.Invoke(
                string.Format(_localization.GetString("Loc.Error.ShellIntegration.Message"), ex.Message),
                _localization.GetString("Loc.Error.Title"));
        }

        SaveSettings();
    }

    // ===== 新建文档 =====

    [RelayCommand]
    private void NewFile() => NewDocument();

    /// <summary>新建一个从未保存过的空白文档 Tab</summary>
    public DocumentViewModel NewDocument()
    {
        var document = new DocumentViewModel(_fileService, _localization, _settings, _sessionService);
        Documents.Add(document);
        ActiveDocument = document;
        return document;
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
                await OpenFolderAsync(path);
            }
            else
            {
                await OpenDocumentAsync(path);
            }
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        string? path = OpenFolderPicker?.Invoke();
        if (path is not null) await OpenFolderAsync(path);
    }

    /// <summary>
    /// 打开文件夹到侧边栏文件树并记录最近列表。切换到不同文件夹时：旧文件夹下的 Tab 静默关闭
    /// （未保存修改写入热退出暂存，不弹保存确认），随后还原新文件夹下留有暂存的未保存修改。
    /// </summary>
    public async Task OpenFolderAsync(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return;
        }

        string? oldRoot = FileTree.RootNode?.FullPath;
        FileTree.OpenFolder(path);
        // 打开失败（目录不存在等）时文件树保持原状，旧文件夹的 Tab 也不动
        if (!string.Equals(FileTree.RootNode?.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)) return;

        RecentFiles.RecordFolder(fullPath);
        ActivePanel = Models.ActivityPanel.Files;

        if (oldRoot is not null && !string.Equals(oldRoot, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            CloseFolderDocuments(oldRoot);
        }
        await RestoreFolderStashAsync(fullPath);
    }

    /// <summary>静默关闭指定文件夹下的所有文档 Tab：脏文档先写热退出暂存（preserveStash 保留），不弹保存确认</summary>
    private void CloseFolderDocuments(string folderRoot)
    {
        string rootPrefix = folderRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (DocumentViewModel document in Documents
                     .Where(d => d is not MarkdownPreviewViewModel &&
                                 !d.IsLoading &&
                                 d.FilePath is not null &&
                                 d.FilePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            document.FlushStash();
            RemoveDocument(document, preserveStash: true);
        }
    }

    /// <summary>还原 FilePath 落在指定文件夹下的热退出暂存为脏 Tab；磁盘文件已删除的暂存清除</summary>
    private async Task RestoreFolderStashAsync(string folderRoot)
    {
        string rootPrefix = folderRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string key in _sessionService.ListStashKeys())
        {
            if (_sessionService.ReadStash(key) is not { Metadata.FilePath: { } filePath } stash) continue;
            if (!filePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            // 已打开的同路径 Tab 其暂存由会话恢复/编辑防抖负责，跳过避免重复还原或误清
            if (Documents.Any(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase))) continue;
            if (!File.Exists(filePath))
            {
                _sessionService.ClearStash(key);
                continue;
            }
            DocumentViewModel? document = await OpenDocumentAsync(filePath);
            // 大文件异步加载未完成时不覆盖内容，暂存留待下次打开该文件夹再还原
            if (document is null || document.IsLoading) continue;
            if (stash.Content != document.Editor.Text)
            {
                document.RestoreStashContent(stash.Content, 0);
            }
            else
            {
                _sessionService.ClearStash(key);
            }
        }
    }

    [RelayCommand]
    private async Task OpenRecentFileAsync(string? path)
    {
        if (path is not null) await OpenDocumentAsync(path);
    }

    [RelayCommand]
    private async Task OpenRecentFolderAsync(string? path)
    {
        if (path is not null) await OpenFolderAsync(path);
    }

    /// <summary>打开文件到新 Tab；同路径已打开或正在打开时聚焦既有 Tab。返回打开的文档，失败返回 null。</summary>
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

        // 同路径正在异步加载（大文件或启动并发），等待同一个 Task 而不是再开一个 Tab
        if (_openingDocuments.TryGetValue(fullPath, out Task<DocumentViewModel?>? pending))
        {
            DocumentViewModel? pendingDoc = await pending;
            if (pendingDoc is not null && Documents.Contains(pendingDoc)) ActiveDocument = pendingDoc;
            return pendingDoc;
        }

        Task<DocumentViewModel?> task = OpenDocumentCoreAsync(fullPath);
        _openingDocuments[fullPath] = task;
        try
        {
            return await task;
        }
        finally
        {
            _openingDocuments.Remove(fullPath);
        }
    }

    private async Task<DocumentViewModel?> OpenDocumentCoreAsync(string fullPath)
    {
        // 目录由 OpenPathsAsync 路由到文件树，这里兜底忽略
        if (Directory.Exists(fullPath)) return null;
        if (!File.Exists(fullPath))
        {
            ReportError("Loc.Error.OpenFile.Message", fullPath, _localization.GetString("Loc.Dialog.Confirm.Title"));
            return null;
        }

        // 在去重早退之前记录：已打开的文件（含会话恢复的 Tab）再次打开时也要刷新历史
        RecentFiles.RecordFile(fullPath);

        DocumentViewModel? existing = Documents.FirstOrDefault(d =>
            string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActiveDocument = existing;
            return existing;
        }

        var document = new DocumentViewModel(_fileService, _localization, _settings, _sessionService);

        // 8.1 阈值判断：超过设置阈值进入大文件模式（边读边显示 + 进度条 + 可取消）
        long fileSize = new FileInfo(fullPath).Length;
        if (fileSize > (long)_settings.LargeFileThresholdMB * 1024 * 1024)
        {
            Documents.Add(document);
            ActiveDocument = document;
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

    /// <summary>从 Tab 集合移除文档（不触发脏确认；调用方负责先行确认）。源 Markdown 文档被移除时联动移除其预览 Tab。preserveStash 为 true 时保留热退出暂存（切换文件夹场景，再次打开时还原）。</summary>
    private void RemoveDocument(DocumentViewModel document, bool preserveStash = false)
    {
        int index = Documents.IndexOf(document);
        if (index < 0) return;
        foreach (MarkdownPreviewViewModel preview in Documents.OfType<MarkdownPreviewViewModel>()
                     .Where(p => ReferenceEquals(p.Source, document)).ToList())
        {
            preview.Detach();
            preview.DisposeResources();
            Documents.Remove(preview);
        }
        // 关闭即放弃：热退出暂存一并清除（含"不保存"关闭脏 Tab 的场景）
        if (!preserveStash) _sessionService.ClearStash(document.HotExitKey);
        document.DisposeResources();
        Documents.Remove(document);
        if (ActiveDocument is not null && Documents.Contains(ActiveDocument)) return;
        ActiveDocument = Documents.Count > 0 ? Documents[Math.Min(index, Documents.Count - 1)] : null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveActive))]
    private Task<bool> SaveActiveAsync()
    {
        return SaveDocumentAsync(ActiveDocument!, forcePicker: false);
    }

    [RelayCommand(CanExecute = nameof(CanSaveActive))]
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
        if (e.OldItems is not null)
        {
            foreach (DocumentViewModel doc in e.OldItems) UnhookDocument(doc);
        }
        if (e.NewItems is not null)
        {
            foreach (DocumentViewModel doc in e.NewItems) HookDocument(doc);
        }
        OnPropertyChanged(nameof(HasOpenDocuments));
        OnPropertyChanged(nameof(ShowEmptyState));
        UpdateDuplicateTitleFlags();
        SaveSession();
    }

    /// <summary>存在文件名相同但显示路径不同的 Tab 时，这些 Tab 的标签改用路径显示以区分</summary>
    private void UpdateDuplicateTitleFlags()
    {
        foreach (IGrouping<string, DocumentViewModel> group in Documents.GroupBy(d => d.Title, StringComparer.OrdinalIgnoreCase))
        {
            bool duplicate = group.Select(d => d.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            foreach (DocumentViewModel doc in group) doc.ShowPathInTitle = duplicate;
        }
    }

    private void HookDocument(DocumentViewModel document)
    {
        document.Editor.CaretPositionChanged += OnDocumentCaretMoved;
        document.ExternalChangeDetected += OnExternalChangeDetected;
        document.PropertyChanged += OnDocumentPropertyChanged;
        document.RelativePath = ComputeDisplayPath(document.FilePath);
    }

    private void UnhookDocument(DocumentViewModel document)
    {
        document.Editor.CaretPositionChanged -= OnDocumentCaretMoved;
        document.ExternalChangeDetected -= OnExternalChangeDetected;
        document.PropertyChanged -= OnDocumentPropertyChanged;
    }

    private void OnDocumentCaretMoved(object? sender, EventArgs e) => ScheduleSessionSave();

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 另存为/首次保存后路径与文件名变化，会话里的 Tab 记录、显示路径与重名标记需更新
        if (e.PropertyName is nameof(DocumentViewModel.FilePath) or nameof(DocumentViewModel.Title) && sender is DocumentViewModel document)
        {
            document.RelativePath = ComputeDisplayPath(document.FilePath);
            UpdateDuplicateTitleFlags();
            SaveSession();
        }
    }

    // ===== 会话恢复（cache/session.json） =====

    /// <summary>立即保存会话（Tab 增删/移动/活动切换/文件夹变化时调用）</summary>
    private void SaveSession()
    {
        if (_restoringSession) return;
        _sessionService.SaveSession(BuildSessionState());
    }

    /// <summary>光标移动等高频变化走 1 秒防抖保存，避免频繁落盘</summary>
    private void ScheduleSessionSave()
    {
        if (_restoringSession) return;
        if (_sessionDebounce is null)
        {
            _sessionDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sessionDebounce.Tick += (_, _) =>
            {
                _sessionDebounce.Stop();
                SaveSession();
            };
        }
        _sessionDebounce.Stop();
        _sessionDebounce.Start();
    }

    private SessionState BuildSessionState()
    {
        // Markdown 预览 Tab 不进会话（启动时不恢复预览，用户可重新打开）
        List<DocumentViewModel> tabs = Documents.Where(d => d is not MarkdownPreviewViewModel).ToList();
        return new SessionState
        {
            Tabs = tabs.Select(d => new SessionTab
            {
                FilePath = d.FilePath,
                HotExitKey = d.FilePath is null ? d.HotExitKey : null,
                Title = d.FilePath is null ? d.Title : null,
                CaretPosition = d.Editor.CaretPosition,
            }).ToList(),
            ActiveTabIndex = ActiveDocument is not null ? tabs.IndexOf(ActiveDocument) : -1,
            OpenFolder = FileTree.RootNode?.FullPath,
        };
    }

    /// <summary>启动时恢复上次会话（窗口显示后异步执行，不拖慢冷启动）</summary>
    public async Task RestoreSessionAsync()
    {
        SessionState? state = _sessionService.LoadSession();
        if (state is null || (state.Tabs.Count == 0 && state.OpenFolder is null)) return;

        _restoringSession = true;
        var restoredKeys = new HashSet<string>(StringComparer.Ordinal);
        var restoredDocs = new List<DocumentViewModel>();
        try
        {
            foreach (SessionTab tab in state.Tabs)
            {
                try
                {
                    DocumentViewModel? document = await RestoreTabAsync(tab, restoredKeys);
                    if (document is not null) restoredDocs.Add(document);
                }
                catch (Exception)
                {
                    // 单个 Tab 恢复失败不影响其余 Tab
                }
            }
            if (state.OpenFolder is not null && Directory.Exists(state.OpenFolder))
            {
                await OpenFolderAsync(state.OpenFolder);
            }
            if (state.ActiveTabIndex >= 0 && state.ActiveTabIndex < restoredDocs.Count)
            {
                ActiveDocument = restoredDocs[state.ActiveTabIndex];
            }
            // 清理孤儿暂存（Tab 被关闭时 ClearStash 失败的残留）；
            // 带路径的暂存保留——可能是上次切换文件夹时留下的未保存修改，再次打开该文件夹时还原
            foreach (string key in _sessionService.ListStashKeys())
            {
                if (restoredKeys.Contains(key)) continue;
                if (_sessionService.ReadStash(key) is { Metadata.FilePath: not null }) continue;
                _sessionService.ClearStash(key);
            }
        }
        finally
        {
            _restoringSession = false;
            SaveSession();
        }
    }

    private async Task<DocumentViewModel?> RestoreTabAsync(SessionTab tab, HashSet<string> restoredKeys)
    {
        if (tab.FilePath is not null)
        {
            if (!File.Exists(tab.FilePath)) return null;
            DocumentViewModel? document = await OpenDocumentAsync(tab.FilePath);
            if (document is null) return null;
            string key = SessionService.KeyForPath(tab.FilePath);
            restoredKeys.Add(key);
            // 有热退出暂存且内容与磁盘不一致时恢复未保存修改（恢复为脏文档）
            if (_sessionService.ReadStash(key) is { } stash)
            {
                if (stash.Content != document.Editor.Text)
                {
                    document.RestoreStashContent(stash.Content, tab.CaretPosition);
                }
                else
                {
                    _sessionService.ClearStash(key);
                }
            }
            // 大文件异步加载未完成时不恢复光标（位置可能超出已加载范围）
            if (!document.IsDirty && !document.IsLoading) document.RestoreCaret(tab.CaretPosition);
            return document;
        }

        if (tab.HotExitKey is null || _sessionService.ReadStash(tab.HotExitKey) is not { } untitledStash) return null;
        restoredKeys.Add(tab.HotExitKey);
        DocumentViewModel untitled = NewDocument();
        // 沿用原暂存键，防抖写回时仍落到同一暂存文件
        untitled.HotExitKey = tab.HotExitKey;
        if (!string.IsNullOrEmpty(untitledStash.Metadata.Title)) untitled.Title = untitledStash.Metadata.Title;
        untitled.RestoreStashContent(untitledStash.Content, tab.CaretPosition);
        return untitled;
    }

    /// <summary>窗口关闭：热退出兜底（同步暂存全部脏文档）+ 会话落盘；不提示保存（需求 4.9）</summary>
    public void OnWindowClosing()
    {
        _sessionDebounce?.Stop();
        // 窗口关闭即终止 shell 会话，避免遗留孤儿进程
        _terminal?.CloseSession();
        foreach (DocumentViewModel doc in Documents) doc.FlushStash();
        SaveSession();
    }

    // ===== 外部修改检测 =====

    private void OnExternalChangeDetected(object? sender, EventArgs e)
    {
        if (sender is DocumentViewModel document) _ = HandleExternalChangeAsync(document);
    }

    private async Task HandleExternalChangeAsync(DocumentViewModel document)
    {
        // 同一文档同一时间只弹一个提示；加载中的文档不处理（加载完成后状态自然刷新）
        if (document.IsLoading || !_externalPrompting.Add(document)) return;
        try
        {
            string? path = document.FilePath;
            if (path is null || !Documents.Contains(document)) return;

            if (!File.Exists(path))
            {
                // 外部删除：脏文档继续编辑不打扰；干净文档告知一次，内容保留
                document.ResetExternalTimestamp();
                if (!document.IsDirty) NotifyExternalDeleted?.Invoke(document);
                return;
            }

            bool reload = document.IsDirty
                ? ConfirmExternalConflict?.Invoke(document) == true
                : ConfirmExternalReload?.Invoke(document) == true;
            if (!reload)
            {
                // 保留当前内容：记住当前 mtime，下一次外部变化再提示
                document.RefreshExternalTimestamp();
                return;
            }

            // 重新加载：脏文档的未保存修改丢弃，热退出暂存随内容替换自动清除
            document.RefreshExternalTimestamp();
            if (new FileInfo(path).Length > (long)_settings.LargeFileThresholdMB * 1024 * 1024)
            {
                _ = LoadLargeDocumentAsync(document, path, document.CurrentEncoding);
                return;
            }
            try
            {
                await document.ReloadWithEncodingAsync(document.CurrentEncoding);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                ReportError("Loc.Error.OpenFile.Message", path, ex.Message);
            }
        }
        finally
        {
            _externalPrompting.Remove(document);
        }
    }
}

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemosyne.Models;
using Mnemosyne.Services;

namespace Mnemosyne.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly FileService _fileService;
    private readonly LocalizationService _localization;
    private readonly AppSettings _settings;

    private GridLength _lastSidebarWidth = new(260);

    public MainWindowViewModel(FileService fileService, LocalizationService localization, AppSettings settings, RecentFilesService recentFiles)
    {
        _fileService = fileService;
        _localization = localization;
        _settings = settings;
        RecentFiles = recentFiles;
        FileTree = new FileTreeViewModel(localization);
        FileTree.OpenFileRequested = path => _ = OpenDocumentAsync(path);
        _indentDisplay = settings.IndentUseTabs
            ? string.Format(localization.GetString("Loc.Status.TabSize"), settings.IndentWidth)
            : string.Format(localization.GetString("Loc.Status.Spaces"), settings.IndentWidth);
        Documents.CollectionChanged += OnDocumentsChanged;
    }

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    public FileTreeViewModel FileTree { get; }

    public RecentFilesService RecentFiles { get; }

    // 以下钩子由 View 注入，承载对话框等纯 UI 交互，业务流转保持在本类中
    public Func<IReadOnlyList<string>?>? OpenFilePicker { get; set; }

    public Func<string?>? OpenFolderPicker { get; set; }

    public Func<string, string?>? SaveFilePicker { get; set; }

    public Func<DocumentViewModel, SavePromptResult>? ConfirmUnsavedClose { get; set; }

    public Func<bool>? ConfirmEncodingReload { get; set; }

    public Action<string, string>? ShowError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarVisible))]
    [NotifyPropertyChangedFor(nameof(IsFilePanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsSearchPanelVisible))]
    private ActivityPanel? _activePanel = Models.ActivityPanel.Files;

    // 与侧边栏列宽双向绑定：拖动分隔条改这里，收起/展开时由 ViewModel 置 0 或恢复
    [ObservableProperty]
    private GridLength _sidebarWidth = new(260);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveDocument))]
    [NotifyCanExecuteChangedFor(nameof(SaveActiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveActiveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseActiveTabCommand))]
    private DocumentViewModel? _activeDocument;

    [ObservableProperty]
    private string _indentDisplay;

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

    partial void OnSidebarWidthChanged(GridLength value)
    {
        if (value.Value > 0) _lastSidebarWidth = value;
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
        if (path is null)
        {
            path = SaveFilePicker?.Invoke(document.Title);
            if (path is null) return false;
        }

        try
        {
            await document.SaveAsync(path);
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

        int index = Documents.IndexOf(document);
        Documents.Remove(document);
        if (ReferenceEquals(ActiveDocument, document) && Documents.Count > 0)
        {
            ActiveDocument = Documents[Math.Min(index, Documents.Count - 1)];
        }
        return true;
    }

    /// <summary>切换活动文档编码：脏文档先确认，确认后按新编码从磁盘重载</summary>
    public async Task SwitchEncodingAsync(Encoding encoding)
    {
        DocumentViewModel? document = ActiveDocument;
        if (document is null || document.FilePath is null) return;
        if (EncodingCatalog.SameAs(document.CurrentEncoding, encoding)) return;
        if (document.IsDirty && ConfirmEncodingReload?.Invoke() != true) return;

        try
        {
            await document.ReloadWithEncodingAsync(encoding);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ReportError("Loc.Error.OpenFile.Message", document.FilePath, ex.Message);
        }
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

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemosyne.Services;

namespace Mnemosyne.ViewModels;

/// <summary>
/// 侧边栏文件树。负责：打开/关闭文件夹、目录懒加载、FileSystemWatcher 防抖刷新、
/// 右键菜单磁盘操作（新建/重命名/删除/在资源管理器中打开）。
/// FileSystemWatcher 事件在线程池线程触发，先按目录去重聚合，经防抖定时器后用 Dispatcher 回 UI 刷新。
/// </summary>
public partial class FileTreeViewModel : ObservableObject, IDisposable
{
    private const int DebounceMilliseconds = 300;

    private readonly LocalizationService _localization;
    private readonly object _pendingLock = new();
    private readonly HashSet<string> _pendingDirectories = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;
    private bool _isCommittingEdit;

    public FileTreeViewModel(LocalizationService localization)
    {
        _localization = localization;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder))]
    private FileTreeNodeViewModel? _rootNode;

    /// <summary>TreeView 的 ItemsSource（0 或 1 个根节点）</summary>
    public ObservableCollection<FileTreeNodeViewModel> RootNodes { get; } = [];

    public bool HasFolder => RootNode is not null;

    // 对话框/错误提示钩子由 View 注入
    public Func<FileTreeNodeViewModel, bool>? ConfirmDelete { get; set; }

    public Action<string, string>? ShowError { get; set; }

    /// <summary>单击文件节点时请求打开到 Tab（参数为完整路径）</summary>
    public Action<string>? OpenFileRequested { get; set; }

    public void OpenFolder(string path)
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
        if (!Directory.Exists(fullPath))
        {
            ReportError("Loc.Error.OpenFolder.Message", fullPath, _localization.GetString("Loc.Error.OpenFolder.NotExist"));
            return;
        }

        CloseFolder();

        var root = FileTreeNodeViewModel.CreateDirectory(fullPath, isRoot: true);
        HookNode(root);
        RootNode = root;
        RootNodes.Clear();
        RootNodes.Add(root);
        root.IsExpanded = true;

        try
        {
            _watcher = new FileSystemWatcher(fullPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _watcher.Created += (_, e) => EnqueueChange(e.FullPath);
            _watcher.Deleted += (_, e) => EnqueueChange(e.FullPath);
            _watcher.Changed += (_, e) => EnqueueChange(e.FullPath);
            _watcher.Renamed += (_, e) =>
            {
                EnqueueChange(e.FullPath);
                EnqueueChange(e.OldFullPath);
            };
            _watcher.Error += (_, _) => ScheduleFullRefresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 监听失败不致命：树仍可手动展开浏览，只是不自动刷新
            ReportError("Loc.Error.WatchFolder.Message", fullPath, ex.Message);
        }
    }

    [RelayCommand]
    private void CloseFolder()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        lock (_pendingLock) _pendingDirectories.Clear();
        RootNode = null;
        RootNodes.Clear();
    }

    /// <summary>单击文件节点：打开到 Tab（目录展开/收起交给 TreeView 默认交互）</summary>
    public void ActivateNode(FileTreeNodeViewModel node)
    {
        if (node.IsDummy || node.IsPlaceholder || node.IsDirectory) return;
        OpenFileRequested?.Invoke(node.FullPath);
    }

    [RelayCommand]
    private void BeginCreateFile(FileTreeNodeViewModel? node) => BeginCreate(node, isDirectory: false);

    [RelayCommand]
    private void BeginCreateFolder(FileTreeNodeViewModel? node) => BeginCreate(node, isDirectory: true);

    private void BeginCreate(FileTreeNodeViewModel? node, bool isDirectory)
    {
        FileTreeNodeViewModel? parent = TargetDirectory(node);
        if (parent is null) return;
        EnsureChildrenLoaded(parent);
        parent.IsExpanded = true;

        var placeholder = FileTreeNodeViewModel.CreatePlaceholder(parent.FullPath, isDirectory);
        parent.Children.Insert(0, placeholder);
        placeholder.IsEditing = true;
    }

    [RelayCommand]
    private void BeginRename(FileTreeNodeViewModel? node)
    {
        if (node is null || node.IsRoot || node.IsDummy || node.IsPlaceholder) return;
        node.EditText = node.Name;
        node.IsEditing = true;
    }

    /// <summary>内联编辑提交（Enter 或失焦）。新建占位节点在此真正落盘。</summary>
    public void CommitEdit(FileTreeNodeViewModel node)
    {
        // 校验失败弹模态错误框会触发编辑框 LostFocus 重入本方法，需防递归弹窗
        if (!node.IsEditing || _isCommittingEdit) return;
        _isCommittingEdit = true;
        try
        {
            CommitEditCore(node);
        }
        finally
        {
            _isCommittingEdit = false;
        }
    }

    private void CommitEditCore(FileTreeNodeViewModel node)
    {
        string newName = node.EditText.Trim();

        if (node.IsPlaceholder)
        {
            if (newName.Length == 0)
            {
                CancelEdit(node);
                return;
            }
            if (!ValidateNewName(node.FullPath, newName)) return;
            string target = Path.Combine(node.FullPath, newName);
            try
            {
                if (node.IsDirectory) Directory.CreateDirectory(target);
                else File.WriteAllBytes(target, []);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                ReportError("Loc.Error.CreateEntry.Message", target, ex.Message);
                CancelEdit(node);
                return;
            }
            node.IsEditing = false;
            RefreshParentOf(node);
            return;
        }

        if (string.Equals(newName, node.Name, StringComparison.Ordinal) || newName.Length == 0)
        {
            node.IsEditing = false;
            return;
        }
        string? parentDir = Path.GetDirectoryName(node.FullPath);
        if (parentDir is null || !ValidateNewName(parentDir, newName)) return;
        string targetPath = Path.Combine(parentDir, newName);
        try
        {
            if (node.IsDirectory) Directory.Move(node.FullPath, targetPath);
            else File.Move(node.FullPath, targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ReportError("Loc.Error.RenameEntry.Message", node.FullPath, ex.Message);
            node.IsEditing = false;
            RefreshParentOf(node);
            return;
        }
        node.IsEditing = false;
        RefreshParentOf(node);
    }

    /// <summary>内联编辑取消（Esc）。占位节点直接移除。</summary>
    public void CancelEdit(FileTreeNodeViewModel node)
    {
        node.IsEditing = false;
        if (node.IsPlaceholder) RefreshParentOf(node);
    }

    [RelayCommand]
    private void Delete(FileTreeNodeViewModel? node)
    {
        if (node is null || node.IsRoot || node.IsDummy || node.IsPlaceholder) return;
        if (ConfirmDelete?.Invoke(node) != true) return;

        FileTreeNodeViewModel? parent = FindNode(Path.GetDirectoryName(node.FullPath));
        try
        {
            if (node.IsDirectory) Directory.Delete(node.FullPath, recursive: true);
            else File.Delete(node.FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ReportError("Loc.Error.DeleteEntry.Message", node.FullPath, ex.Message);
        }
        if (parent is not null) RefreshNode(parent);
    }

    [RelayCommand]
    private void RevealInExplorer(FileTreeNodeViewModel? node)
    {
        if (node is null || node.IsDummy || node.IsPlaceholder) return;
        try
        {
            Process.Start("explorer.exe", $"/select,\"{node.FullPath}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            ReportError("Loc.Error.Reveal.Message", node.FullPath, ex.Message);
        }
    }

    public void Dispose() => CloseFolder();

    // 选中文件节点时"新建"落在其父目录，目录节点与空白处落在目录自身/根
    private FileTreeNodeViewModel? TargetDirectory(FileTreeNodeViewModel? node)
    {
        if (node is null || node.IsDummy) return RootNode;
        if (node.IsPlaceholder) return FindNode(node.FullPath);
        if (node.IsDirectory) return node;
        return FindNode(Path.GetDirectoryName(node.FullPath));
    }

    private bool ValidateNewName(string parentDir, string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            ReportError("Loc.Error.InvalidName.Message", name, _localization.GetString("Loc.Error.InvalidName.Detail"));
            return false;
        }
        string target = Path.Combine(parentDir, name);
        if (File.Exists(target) || Directory.Exists(target))
        {
            ReportError("Loc.Error.EntryExists.Message", target, _localization.GetString("Loc.Error.EntryExists.Detail"));
            return false;
        }
        return true;
    }

    private void HookNode(FileTreeNodeViewModel node)
    {
        if (node.IsDirectory) node.ChildrenRequested += OnChildrenRequested;
    }

    private void OnChildrenRequested(FileTreeNodeViewModel node) => LoadChildren(node);

    private void EnsureChildrenLoaded(FileTreeNodeViewModel node)
    {
        if (node.HasDummyChild) LoadChildren(node);
    }

    private void LoadChildren(FileTreeNodeViewModel node)
    {
        node.ClearDummy();
        foreach (FileTreeNodeViewModel child in ReadChildren(node.FullPath))
        {
            HookNode(child);
            node.Children.Add(child);
        }
    }

    /// <summary>读取目录子项并排序：目录在前，同类按名称排序。IO 失败返回空并提示。</summary>
    private List<FileTreeNodeViewModel> ReadChildren(string directoryPath)
    {
        try
        {
            var children = new List<FileTreeNodeViewModel>();
            foreach (string dir in Directory.EnumerateDirectories(directoryPath))
            {
                children.Add(FileTreeNodeViewModel.CreateDirectory(dir));
            }
            foreach (string file in Directory.EnumerateFiles(directoryPath))
            {
                children.Add(FileTreeNodeViewModel.CreateFile(file));
            }
            children.Sort(static (a, b) => a.IsDirectory == b.IsDirectory
                ? string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                : a.IsDirectory ? -1 : 1);
            return children;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ReportError("Loc.Error.ReadFolder.Message", directoryPath, ex.Message);
            return [];
        }
    }

    /// <summary>按磁盘现状合并刷新节点子项：保留仍存在节点的展开状态，新增缺失项，移除已删除项与占位项</summary>
    private void RefreshNode(FileTreeNodeViewModel node)
    {
        if (!node.IsDirectory || node.HasDummyChild) return;

        List<FileTreeNodeViewModel> fresh = ReadChildren(node.FullPath);
        var freshByPath = fresh.ToDictionary(c => c.FullPath, StringComparer.OrdinalIgnoreCase);

        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            FileTreeNodeViewModel child = node.Children[i];
            if (child.IsPlaceholder || !freshByPath.Remove(child.FullPath))
            {
                node.Children.RemoveAt(i);
            }
        }
        foreach (FileTreeNodeViewModel child in freshByPath.Values)
        {
            HookNode(child);
            node.Children.Add(child);
        }

        var sorted = node.Children
            .OrderByDescending(c => c.IsDirectory)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = node.Children.IndexOf(sorted[i]);
            if (current != i) node.Children.Move(current, i);
        }
    }

    private void RefreshParentOf(FileTreeNodeViewModel node)
    {
        FileTreeNodeViewModel? parent = FindNode(Path.GetDirectoryName(node.FullPath));
        if (parent is not null) RefreshNode(parent);
    }

    /// <summary>按完整路径在已加载的树中查找节点（未展开的目录查不到，属正常）</summary>
    private FileTreeNodeViewModel? FindNode(string? fullPath)
    {
        if (fullPath is null || RootNode is null) return null;
        return FindNodeRecursive(RootNode, NormalizePath(fullPath));
    }

    private static FileTreeNodeViewModel? FindNodeRecursive(FileTreeNodeViewModel node, string fullPath)
    {
        if (string.Equals(NormalizePath(node.FullPath), fullPath, StringComparison.OrdinalIgnoreCase)) return node;
        foreach (FileTreeNodeViewModel child in node.Children)
        {
            if (child.IsDirectory && FindNodeRecursive(child, fullPath) is { } found) return found;
        }
        return null;
    }

    private static string NormalizePath(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // ---- FileSystemWatcher 防抖刷新 ----

    private void EnqueueChange(string changedPath)
    {
        string? directory = Path.GetDirectoryName(changedPath);
        if (directory is null) return;
        lock (_pendingLock)
        {
            _pendingDirectories.Add(directory);
            _debounceTimer ??= new System.Threading.Timer(OnDebounceElapsed);
            _debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void ScheduleFullRefresh()
    {
        if (RootNode is null) return;
        lock (_pendingLock)
        {
            _pendingDirectories.Add(RootNode.FullPath);
            _debounceTimer ??= new System.Threading.Timer(OnDebounceElapsed);
            _debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        List<string> directories;
        lock (_pendingLock)
        {
            directories = [.. _pendingDirectories];
            _pendingDirectories.Clear();
        }
        Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(() => ProcessPendingChanges(directories));
    }

    private void ProcessPendingChanges(List<string> directories)
    {
        FileTreeNodeViewModel? root = RootNode;
        if (root is null) return;

        if (!Directory.Exists(root.FullPath))
        {
            CloseFolder();
            return;
        }

        foreach (string directory in directories)
        {
            FileTreeNodeViewModel? node = FindNode(directory);
            if (node is not null) RefreshNode(node);
        }
    }

    private void ReportError(string messageKey, string path, string detail)
    {
        ShowError?.Invoke(
            string.Format(_localization.GetString(messageKey), path, detail),
            _localization.GetString("Loc.Error.Title"));
    }
}

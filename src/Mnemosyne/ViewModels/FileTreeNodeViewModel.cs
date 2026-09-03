using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mnemosyne.ViewModels;

/// <summary>
/// 文件树节点。目录子项懒加载：初始放一个哨兵子节点（IsDummy），首次展开时由
/// <see cref="ChildrenRequested"/> 通知 FileTreeViewModel 读取真实子项。
/// </summary>
public partial class FileTreeNodeViewModel : ObservableObject
{
    private FileTreeNodeViewModel(string name, string fullPath, bool isDirectory)
    {
        _name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
    }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public bool IsRoot { get; private init; }

    /// <summary>新建占位节点：提交内联编辑时才真正落盘</summary>
    public bool IsPlaceholder { get; init; }

    public bool IsDummy { get; private init; }

    public ObservableCollection<FileTreeNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editText = "";

    /// <summary>展开时通知宿主加载子项（仅当含哨兵节点时触发）</summary>
    public event Action<FileTreeNodeViewModel>? ChildrenRequested;

    public static FileTreeNodeViewModel CreateDirectory(string path, bool isRoot = false)
    {
        var node = new FileTreeNodeViewModel(DirectoryName(path), path, isDirectory: true) { IsRoot = isRoot };
        node.Children.Add(CreateDummy());
        return node;
    }

    public static FileTreeNodeViewModel CreateFile(string path) =>
        new(Path.GetFileName(path), path, isDirectory: false);

    public static FileTreeNodeViewModel CreatePlaceholder(string parentPath, bool isDirectory) =>
        new("", parentPath, isDirectory) { IsPlaceholder = true };

    private static FileTreeNodeViewModel CreateDummy() =>
        new("", "", isDirectory: false) { IsDummy = true };

    public bool HasDummyChild => Children.Count == 1 && Children[0].IsDummy;

    public void ClearDummy() => Children.Clear();

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && HasDummyChild) ChildrenRequested?.Invoke(this);
    }

    private static string DirectoryName(string path)
    {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : trimmed;
    }
}

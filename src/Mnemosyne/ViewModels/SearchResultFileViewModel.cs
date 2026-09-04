using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemosyne.Models;

namespace Mnemosyne.ViewModels;

/// <summary>结果树中的文件分组节点（可展开，子项为该文件的匹配行）。</summary>
public partial class SearchResultFileViewModel : ObservableObject
{
    public SearchResultFileViewModel(FolderSearchFileResult result, Action<SearchResultLocation> openMatch)
    {
        FullPath = result.FullPath;
        FileName = Path.GetFileName(result.FullPath);
        string? directory = Path.GetDirectoryName(result.RelativePath);
        DirectoryDisplay = string.IsNullOrEmpty(directory) ? "" : directory.Replace('/', Path.DirectorySeparatorChar);
        CountDisplay = result.Truncated ? $"{result.Matches.Count}+" : result.Matches.Count.ToString();
        ToolTipText = result.FullPath;
        foreach (FileSearchMatch match in result.Matches)
        {
            Matches.Add(new SearchResultMatchViewModel(result.FullPath, match, openMatch));
        }
    }

    public string FullPath { get; }

    public string FileName { get; }

    /// <summary>相对根目录的所在目录（根下文件为空串）</summary>
    public string DirectoryDisplay { get; }

    public string CountDisplay { get; }

    public string ToolTipText { get; }

    public ObservableCollection<SearchResultMatchViewModel> Matches { get; } = [];

    [ObservableProperty]
    private bool _isExpanded = true;
}

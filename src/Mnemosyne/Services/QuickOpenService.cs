using System.IO;
using Mnemosyne.Models;

namespace Mnemosyne.Services;

/// <summary>
/// 快速打开（Ctrl+P）的文件夹文件数据源：按需递归扫描整个文件夹并缓存结果供本次会话复用。
/// 缓存按规范化根路径为键——文件夹变更后再次按下 Ctrl+P 时键不同，自然触发重扫（缓存即失效）。
/// 排除规则与文件夹搜索/文件树一致：默认排除 .git/bin/obj/node_modules，跳过 ReparsePoint 防目录循环，
/// 按设置过滤 . 开头项与 Windows 隐藏属性项。
/// </summary>
public static class QuickOpenService
{
    // 与 SearchService 的默认排除目录保持一致
    private static readonly HashSet<string> DefaultExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules",
    };

    private static readonly object CacheLock = new();
    private static string? _cachedRoot;
    private static IReadOnlyList<QuickOpenFile>? _cachedFiles;

    /// <summary>取文件夹下全部文件（命中缓存直接返回，否则后台扫描并缓存）；取消抛 OperationCanceledException</summary>
    public static Task<IReadOnlyList<QuickOpenFile>> GetFilesAsync(string rootPath, AppSettings settings, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(rootPath);
        lock (CacheLock)
        {
            if (_cachedFiles is not null && string.Equals(_cachedRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(_cachedFiles);
            }
        }
        return Task.Run(() =>
        {
            IReadOnlyList<QuickOpenFile> files = Scan(root, settings, cancellationToken);
            lock (CacheLock)
            {
                _cachedRoot = root;
                _cachedFiles = files;
            }
            return files;
        }, cancellationToken);
    }

    private static IReadOnlyList<QuickOpenFile> Scan(string root, AppSettings settings, CancellationToken cancellationToken)
    {
        var files = new List<QuickOpenFile>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            string[] subDirectories;
            string[] directoryFiles;
            try
            {
                subDirectories = Directory.GetDirectories(directory);
                directoryFiles = Directory.GetFiles(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string subDirectory in subDirectories)
            {
                if (DefaultExcludedDirectories.Contains(Path.GetFileName(subDirectory))) continue;
                if (IsExcluded(subDirectory, settings)) continue;
                pending.Push(subDirectory);
            }

            foreach (string file in directoryFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsExcluded(file, settings)) continue;
                string relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                files.Add(new QuickOpenFile(file, relative));
            }
        }
        files.Sort(static (a, b) => string.Compare(a.DisplayPath, b.DisplayPath, StringComparison.OrdinalIgnoreCase));
        return files;
    }

    // 可见性规则与 FileTreeViewModel.IsHidden 一致；属性读取失败的项按排除处理
    private static bool IsExcluded(string path, AppSettings settings)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
            if (settings.HideDotFiles && Path.GetFileName(path).StartsWith('.')) return true;
            if (settings.HideHiddenFiles && (attributes & FileAttributes.Hidden) != 0) return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return true;
        }
        return false;
    }
}

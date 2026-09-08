using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Mnemosyne.Models;

namespace Mnemosyne.Services;

/// <summary>
/// 最近打开的文件/文件夹列表（便携模式 config/recent.json，各最多 20 条）。
/// 读写失败不致命：记录丢失不影响主流程。
/// </summary>
public class RecentFilesService
{
    private const int MaxEntries = 20;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _storePath;

    public RecentFilesService()
    {
        string configDir = Path.Combine(AppContext.BaseDirectory, "config");
        Directory.CreateDirectory(configDir);
        _storePath = Path.Combine(configDir, "recent.json");
        Load();
    }

    public ObservableCollection<RecentEntry> RecentFiles { get; } = [];

    public ObservableCollection<RecentEntry> RecentFolders { get; } = [];

    public void RecordFile(string path) => Record(RecentFiles, path);

    public void RecordFolder(string path) => Record(RecentFolders, path);

    private void Record(ObservableCollection<RecentEntry> list, string path)
    {
        RecentEntry? existing = list.FirstOrDefault(e =>
            string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) list.Remove(existing);
        list.Insert(0, RecentEntry.FromPath(path));
        while (list.Count > MaxEntries) list.RemoveAt(list.Count - 1);
        Save();
    }

    private void Load()
    {
        RecentStore? store = ReadStore();
        if (store is null) return;
        foreach (string path in store.Files.Take(MaxEntries)) RecentFiles.Add(RecentEntry.FromPath(path));
        foreach (string path in store.Folders.Take(MaxEntries)) RecentFolders.Add(RecentEntry.FromPath(path));
    }

    private RecentStore? ReadStore()
    {
        if (!File.Exists(_storePath)) return null;
        try
        {
            string json = File.ReadAllText(_storePath);
            return JsonSerializer.Deserialize<RecentStore>(json);
        }
        catch (Exception)
        {
            // recent.json 损坏时按空列表处理，不阻塞启动/落盘
            return null;
        }
    }

    private void Save()
    {
        try
        {
            // 多实例各自持有内存列表，写盘前合并磁盘上其他实例刚记录的条目，避免互相覆盖
            RecentStore? disk = ReadStore();
            var files = RecentFiles.Select(e => e.FullPath).ToList();
            var folders = RecentFolders.Select(e => e.FullPath).ToList();
            if (disk is not null)
            {
                MergeMissing(files, disk.Files);
                MergeMissing(folders, disk.Folders);
            }

            var store = new RecentStore
            {
                Files = files.Take(MaxEntries).ToList(),
                Folders = folders.Take(MaxEntries).ToList(),
            };
            File.WriteAllText(_storePath, JsonSerializer.Serialize(store, SerializerOptions));
        }
        catch (Exception)
        {
            // 落盘失败仅丢失最近列表记录，不打断用户操作
        }
    }

    private static void MergeMissing(List<string> merged, List<string> diskEntries)
    {
        foreach (string path in diskEntries)
        {
            if (!merged.Contains(path, StringComparer.OrdinalIgnoreCase)) merged.Add(path);
        }
    }

    private sealed class RecentStore
    {
        public List<string> Files { get; set; } = [];

        public List<string> Folders { get; set; } = [];
    }
}

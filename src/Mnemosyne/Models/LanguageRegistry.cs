using System.IO;

namespace Mnemosyne.Models;

/// <summary>
/// 语言注册表（architecture.md 约定集中放 Models）。内置只保留 Plain Text 兜底；
/// 全部具体语言由插件（ILanguageContribution）在插件扫描后经 RegisterLanguages 登记。
/// 读侧取无锁快照（注册整体替换引用），写侧持锁重建索引。
/// </summary>
public static class LanguageRegistry
{
    public static LanguageDefinition PlainText { get; } = new("Plain Text", "", ["txt", "log", "text"]);

    private static readonly Lock _gate = new();
    private static Snapshot _snapshot = Snapshot.Empty;

    /// <summary>已注册的全部语言（不含 Plain Text；插件扫描前为空）。</summary>
    public static IReadOnlyList<LanguageDefinition> All => _snapshot.All;

    /// <summary>追加登记一批语言；扩展名/精确文件名与已注册者冲突的条目被跳过（返回实际登记数）。</summary>
    public static int RegisterLanguages(IEnumerable<LanguageDefinition> languages)
    {
        lock (_gate)
        {
            Snapshot old = _snapshot;
            var all = new List<LanguageDefinition>(old.All);
            var byExtension = new Dictionary<string, LanguageDefinition>(old.ByExtension, StringComparer.OrdinalIgnoreCase);
            var byFileName = new Dictionary<string, LanguageDefinition>(old.ByFileName, StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (LanguageDefinition lang in languages)
            {
                all.Add(lang);
                added++;
                foreach (string ext in lang.Extensions)
                {
                    byExtension.TryAdd(ext, lang);
                }
                if (lang.FileNames is not null)
                {
                    foreach (string name in lang.FileNames)
                    {
                        byFileName.TryAdd(name, lang);
                    }
                }
            }
            _snapshot = new Snapshot(all, byExtension, byFileName);
            return added;
        }
    }

    /// <summary>按文件路径（文件名优先、其次扩展名）匹配语言；无匹配返回纯文本。</summary>
    public static LanguageDefinition GetForFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return PlainText;
        Snapshot snapshot = _snapshot;
        string fileName = Path.GetFileName(filePath);
        if (snapshot.ByFileName.TryGetValue(fileName, out LanguageDefinition? byName)) return byName;
        string ext = Path.GetExtension(fileName);
        if (ext.Length > 1 && snapshot.ByExtension.TryGetValue(ext[1..], out LanguageDefinition? byExt)) return byExt;
        return PlainText;
    }

    /// <summary>按 Lexilla 内部名称查找已注册语言；未注册返回 null（由调用方按原始名称建立条目）。</summary>
    public static LanguageDefinition? GetByLexerName(string lexerName)
    {
        if (string.IsNullOrEmpty(lexerName)) return PlainText;
        return _snapshot.All.FirstOrDefault(l => string.Equals(l.LexerName, lexerName, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Snapshot(
        IReadOnlyList<LanguageDefinition> all,
        IReadOnlyDictionary<string, LanguageDefinition> byExtension,
        IReadOnlyDictionary<string, LanguageDefinition> byFileName)
    {
        public static Snapshot Empty { get; } = new(
            [],
            new Dictionary<string, LanguageDefinition>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, LanguageDefinition>(StringComparer.OrdinalIgnoreCase));

        public IReadOnlyList<LanguageDefinition> All { get; } = all;
        public IReadOnlyDictionary<string, LanguageDefinition> ByExtension { get; } = byExtension;
        public IReadOnlyDictionary<string, LanguageDefinition> ByFileName { get; } = byFileName;
    }
}

using System.IO;

namespace Mnemosyne.Models;

/// <summary>最近打开列表中的一项（文件或文件夹）。</summary>
public class RecentEntry
{
    public string FullPath { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public static RecentEntry FromPath(string path) => new()
    {
        FullPath = path,
        DisplayName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name
            ? name
            : path,
    };
}

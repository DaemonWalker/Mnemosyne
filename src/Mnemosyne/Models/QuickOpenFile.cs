using System.IO;

namespace Mnemosyne.Models;

/// <summary>
/// 快速打开（Ctrl+P）列表中的一项。DisplayPath 为展示用第二行文本：
/// 文件夹模式下是相对根目录的路径（正斜杠），无文件夹模式（已打开 Tab/最近文件）下是完整路径。
/// </summary>
public sealed record QuickOpenFile(string FullPath, string DisplayPath)
{
    public string FileName => Path.GetFileName(FullPath);
}

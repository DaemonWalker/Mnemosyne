using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;

namespace Mnemosyne.Services;

/// <summary>从剪贴板读取 HTML 片段，处理 CF_HTML 头与字节偏移截取。</summary>
public static class HtmlFragmentReader
{
    public static string? TryGetHtmlFragment(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.Html)) return null;
        string? raw = data.GetData(DataFormats.Html) switch
        {
            string s => s,
            MemoryStream stream => Encoding.UTF8.GetString(stream.ToArray()),
            _ => null,
        };
        return string.IsNullOrEmpty(raw) ? null : ExtractFragment(raw);
    }

    // CF_HTML 的 StartFragment/EndFragment 是按 UTF-8 字节计的偏移，必须先编码再截取
    internal static string ExtractFragment(string raw)
    {
        int start = ReadOffset(raw, "StartFragment:");
        int end = ReadOffset(raw, "EndFragment:");
        if (start >= 0 && end > start)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(raw);
            start = Math.Min(start, bytes.Length);
            end = Math.Min(end, bytes.Length);
            if (end > start) return Encoding.UTF8.GetString(bytes, start, end - start);
        }
        return raw;
    }

    private static int ReadOffset(string raw, string name)
    {
        int index = raw.IndexOf(name, StringComparison.Ordinal);
        if (index < 0) return -1;
        index += name.Length;
        int end = index;
        while (end < raw.Length && char.IsDigit(raw[end])) end++;
        return end > index
            ? int.Parse(raw.AsSpan(index, end - index), CultureInfo.InvariantCulture)
            : -1;
    }
}

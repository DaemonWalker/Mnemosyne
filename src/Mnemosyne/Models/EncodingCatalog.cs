using System.Text;

namespace Mnemosyne.Models;

/// <summary>状态栏编码菜单的可选项与编码显示名。编码名是约定俗成的专有名词，不走 i18n。</summary>
public static class EncodingCatalog
{
    public static Encoding Utf8NoBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static Encoding Utf8Bom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static IReadOnlyList<Encoding> Options { get; } =
    [
        Utf8NoBom,
        Utf8Bom,
        Encoding.Unicode,
        Encoding.BigEndianUnicode,
        Encoding.GetEncoding(936),    // GBK
        Encoding.GetEncoding(54936),  // GB18030
        Encoding.GetEncoding(950),    // Big5
        Encoding.GetEncoding(932),    // Shift-JIS
        Encoding.GetEncoding(949),    // EUC-KR
        Encoding.GetEncoding(1252),   // Western European
    ];

    public static string DisplayName(Encoding encoding)
    {
        return encoding.CodePage switch
        {
            65001 => encoding.GetPreamble().Length > 0 ? "UTF-8 with BOM" : "UTF-8",
            1200 => "UTF-16 LE",
            1201 => "UTF-16 BE",
            12000 => "UTF-32 LE",
            12001 => "UTF-32 BE",
            936 => "GBK",
            54936 => "GB18030",
            950 => "Big5",
            932 => "Shift-JIS",
            949 => "EUC-KR",
            1252 => "Windows-1252",
            _ => encoding.WebName.ToUpperInvariant(),
        };
    }

    /// <summary>判断两个编码实例是否表示同一编码（CodePage + BOM 发射行为一致）</summary>
    public static bool SameAs(Encoding a, Encoding b)
    {
        return a.CodePage == b.CodePage && (a.GetPreamble().Length > 0) == (b.GetPreamble().Length > 0);
    }
}

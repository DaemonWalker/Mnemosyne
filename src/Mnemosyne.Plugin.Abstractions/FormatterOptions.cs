namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 格式化选项。主程序按当前文档的编辑器设置构造并传入。
/// </summary>
public sealed class FormatterOptions
{
    /// <summary>true 表示用制表符缩进，false 表示用空格缩进。</summary>
    public bool UseTabs { get; set; }

    /// <summary>
    /// 缩进宽度：空格模式下为每级缩进的空格数；制表符模式下为制表符的显示宽度
    /// （实现可自行决定是否在制表符模式下忽略此值）。
    /// </summary>
    public int IndentWidth { get; set; } = 4;

    /// <summary>生成指定层级的缩进字符串（制表符模式每层一个 '\t'，空格模式每层 IndentWidth 个空格）。</summary>
    public string GetIndent(int level)
    {
        if (level <= 0) return string.Empty;
        return UseTabs ? new string('\t', level) : new string(' ', Math.Max(1, IndentWidth) * level);
    }
}

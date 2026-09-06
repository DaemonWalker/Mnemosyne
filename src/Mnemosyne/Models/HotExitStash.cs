namespace Mnemosyne.Models;

/// <summary>热退出暂存的元数据（cache/hotexit/&lt;key&gt;.json），内容与暂存文本（&lt;key&gt;.txt）配套</summary>
public class HotExitStash
{
    /// <summary>文件路径；null 表示从未保存过的新建文档</summary>
    public string? FilePath { get; set; }

    /// <summary>Tab 标题（新建文档恢复时用）</summary>
    public string Title { get; set; } = string.Empty;
}

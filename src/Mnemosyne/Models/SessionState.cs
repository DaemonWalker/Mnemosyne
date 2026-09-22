namespace Mnemosyne.Models;

/// <summary>上次会话状态（cache/session.json）：打开的 Tab、活动 Tab、打开的文件夹、各文件夹的全局搜索条件</summary>
public class SessionState
{
    public List<SessionTab> Tabs { get; set; } = [];

    /// <summary>活动 Tab 在 Tabs 中的索引；-1 表示无</summary>
    public int ActiveTabIndex { get; set; } = -1;

    /// <summary>侧边栏打开的文件夹；null 表示未打开</summary>
    public string? OpenFolder { get; set; }

    /// <summary>各文件夹（规范化全路径为键）最近一次全局搜索的条件，再次打开该文件夹时还原</summary>
    public Dictionary<string, FolderSearchOptions> FolderSearches { get; set; } = [];
}

public class SessionTab
{
    /// <summary>文件路径；null 表示从未保存过的新建文档（内容在热退出暂存中）</summary>
    public string? FilePath { get; set; }

    /// <summary>新建文档的热退出暂存键（文件文档不需要，启动时按路径哈希计算）</summary>
    public string? HotExitKey { get; set; }

    /// <summary>新建文档的 Tab 标题</summary>
    public string? Title { get; set; }

    /// <summary>光标位置（字符索引）</summary>
    public int CaretPosition { get; set; }
}

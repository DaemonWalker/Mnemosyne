namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 插件贡献的语言定义：状态栏显示名、Lexer 名称、关联扩展名/文件名与关键字表。
/// <see cref="LexerName"/> 为 Lexilla 的内部名称（如 "cpp"、"python"）；
/// 若引用同插件 <see cref="ICustomLexer.Name"/>，主程序对该语言使用 container lexer
/// 并由该自定义词法器上色。<see cref="FormatterId"/> 为格式化插件匹配用的语言标识。
/// 与宿主模型镜像，但用普通密封类（netstandard2.0 无 record struct，见 notes.md §6）。
/// </summary>
public sealed class PluginLanguageDefinition
{
    /// <param name="displayName">状态栏与语言选择器中的显示名（如 "C#"、"DataWeave"）。</param>
    /// <param name="lexerName">Lexilla 内部名称或自定义词法器名；空串表示纯文本。</param>
    /// <param name="extensions">关联扩展名列表（不含点，如 "cs"、"dwl"）。</param>
    public PluginLanguageDefinition(
        string displayName,
        string lexerName,
        IReadOnlyList<string> extensions,
        string? keywords = null,
        string? secondaryKeywords = null,
        string? formatterId = null,
        bool supportsFolding = false,
        IReadOnlyList<string>? fileNames = null)
    {
        DisplayName = displayName;
        LexerName = lexerName;
        Extensions = extensions;
        Keywords = keywords;
        SecondaryKeywords = secondaryKeywords;
        FormatterId = formatterId;
        SupportsFolding = supportsFolding;
        FileNames = fileNames ?? [];
    }

    /// <summary>状态栏与语言选择器中的显示名。</summary>
    public string DisplayName { get; }

    /// <summary>Lexilla 内部名称或同插件 <see cref="ICustomLexer.Name"/>；空串表示纯文本。</summary>
    public string LexerName { get; }

    /// <summary>关联扩展名列表（不含点，不区分大小写）。</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>关键字表（设置 Lexer 时经 SCI_SETKEYWORDS 槽位 0 提供）。</summary>
    public string? Keywords { get; }

    /// <summary>第二关键字表（槽位 1，如内置类型名）。</summary>
    public string? SecondaryKeywords { get; }

    /// <summary>格式化插件匹配用的语言标识（如 "json"），无对应格式化器时为 null。</summary>
    public string? FormatterId { get; }

    /// <summary>其 Lexer 是否支持 fold 属性（启用折叠边栏）。</summary>
    public bool SupportsFolding { get; }

    /// <summary>精确文件名匹配（如 "CMakeLists.txt"、"Dockerfile"），优先于扩展名。</summary>
    public IReadOnlyList<string> FileNames { get; }
}

namespace Mnemosyne.Models;

/// <summary>
/// 一种语言的定义：状态栏显示名、Scintilla Lexer 名称（空串表示纯文本）、关联扩展名与关键字表。
/// FormatterId 为格式化插件匹配用的语言标识（如 json/xml/html），无对应格式化器时为 null。
/// SupportsFolding 表示其 Lexer 支持 fold 属性，启用折叠边栏。
/// </summary>
public sealed record LanguageDefinition(
    string DisplayName,
    string LexerName,
    IReadOnlyList<string> Extensions,
    string? Keywords = null,
    string? SecondaryKeywords = null,
    string? FormatterId = null,
    bool SupportsFolding = false);

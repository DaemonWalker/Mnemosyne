using Mnemosyne.Plugin.Abstractions;

namespace Mnemosyne.Services;

/// <summary>
/// 插件自定义词法器（ICustomLexer）注册表。PluginService 扫描后登记；
/// ScintillaHost 对命中本注册表的语言用 container lexer，在 StyleNeeded 回调里分词上色。
/// </summary>
public static class CustomLexerRegistry
{
    private static readonly Dictionary<string, ICustomLexer> _lexers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock _gate = new();

    /// <summary>登记词法器；名称（不区分大小写）与已登记者冲突时忽略后到者（PluginService 已先过滤并记日志）。</summary>
    public static void Register(ICustomLexer lexer)
    {
        lock (_gate)
        {
            _lexers.TryAdd(lexer.Name, lexer);
        }
    }

    /// <summary>按名称查找词法器（不区分大小写）。</summary>
    public static bool TryGet(string? name, out ICustomLexer lexer)
    {
        lock (_gate)
        {
            if (name is not null && _lexers.TryGetValue(name, out ICustomLexer? found))
            {
                lexer = found;
                return true;
            }
        }
        lexer = null!;
        return false;
    }

    /// <summary>语义类别 → 主题资源键（Color.Editor.* 后缀）。</summary>
    public static string ThemeKey(LexerStyleSemantic semantic) => semantic switch
    {
        LexerStyleSemantic.Default => "Foreground",
        LexerStyleSemantic.Comment => "Comment",
        LexerStyleSemantic.Keyword => "Keyword",
        LexerStyleSemantic.String => "String",
        LexerStyleSemantic.Number => "Number",
        LexerStyleSemantic.Type => "Type",
        LexerStyleSemantic.Function => "Function",
        LexerStyleSemantic.Preprocessor => "Preprocessor",
        LexerStyleSemantic.Error => "Error",
        _ => "Foreground",
    };
}

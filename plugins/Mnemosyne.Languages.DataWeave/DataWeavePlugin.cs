using Mnemosyne.Plugin.Abstractions;

namespace Mnemosyne.Languages.DataWeave;

/// <summary>
/// DataWeave（.dwl）语言插件。Lexilla 无内置 DataWeave Lexer，本插件经 ICustomLexer
/// 提供手写分词器，主程序对该语言设 container lexer，在 StyleNeeded 回调里分词上色。
/// 分词规则参照官方 TextMate 语法 mulesoft/data-weave-tmLanguage，另补该语法未覆盖的
/// 日期字面量 |yyyy-MM-dd| 与 $/$$ 变量引用。插值表达式 $(...) 内部不做完整分词，
/// 保持字符串底色，仅高亮 $(/) 定界符与 $name。
/// </summary>
public sealed class DataWeavePlugin : IMnemosynePlugin, ILanguageContribution, ICustomLexer
{
    public const string LexerName = "dataweave";

    public string Id => "mnemosyne.languages.dataweave";

    public string DisplayName => "DataWeave Language";

    public string Version => "1.0.0";

    public string Description => "DataWeave（.dwl）语言高亮：扩展名映射 + 手写分词器（container lexer 上色）。";

    public IReadOnlyList<PluginSettingDescriptor> Settings => [];

    public void Initialize(IPluginContext context)
    {
    }

    public IReadOnlyList<PluginLanguageDefinition> Languages { get; } =
        [new PluginLanguageDefinition("DataWeave", LexerName, ["dwl"])];

    public string Name => LexerName;

    /// <summary>样式表下标与内部 DwStyle 枚举值一一对应，配色与迁移前宿主内置映射逐项一致。</summary>
    public IReadOnlyList<LexerStyle> Styles { get; } =
    [
        new(LexerStyleSemantic.Default),                 // Default
        new(LexerStyleSemantic.Comment),                 // Comment
        new(LexerStyleSemantic.Keyword, bold: true),     // Keyword
        new(LexerStyleSemantic.Keyword, bold: true),     // Declaration
        new(LexerStyleSemantic.String),                  // String
        new(LexerStyleSemantic.Number),                  // Number
        new(LexerStyleSemantic.Keyword, bold: true),     // Constant
        new(LexerStyleSemantic.Default),                 // Operator（前景色）
        new(LexerStyleSemantic.Keyword, bold: true),     // Arrow
        new(LexerStyleSemantic.Function),                // Interpolation
        new(LexerStyleSemantic.Preprocessor, bold: true),// Directive
        new(LexerStyleSemantic.Type),                    // Variable
    ];

    public IReadOnlyList<LexerStyleSpan> Tokenize(string text) => DataWeaveTokenizer.Tokenize(text);

    /// <summary>分词产物的样式类别，序号即 <see cref="Styles"/> 下标。</summary>
    internal enum DwStyle
    {
        Default = 0,
        Comment = 1,
        Keyword = 2,
        Declaration = 3,
        String = 4,
        Number = 5,
        Constant = 6,
        Operator = 7,
        Arrow = 8,
        Interpolation = 9,
        Directive = 10,
        Variable = 11,
    }

    /// <summary>
    /// DataWeave 手写分词器（自主程序 Services/DataWeaveLexer 迁移，逻辑不变；
    /// 适配 netstandard2.0：区间取值用 Substring、产出合并用显式下标）。
    /// </summary>
    internal static class DataWeaveTokenizer
    {
        private static readonly HashSet<string> ControlKeywords = new(StringComparer.Ordinal)
        {
            "if", "else", "unless", "do", "using", "default", "match", "case", "update",
        };

        private static readonly HashSet<string> OperatorWords = new(StringComparer.Ordinal)
        {
            "and", "or", "not", "as", "is",
        };

        private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
        {
            "private", "internal",
        };

        private static readonly HashSet<string> Declarations = new(StringComparer.Ordinal)
        {
            "import", "fun", "ns", "input", "var", "type", "annotation", "output", "from",
        };

        private static readonly HashSet<string> Constants = new(StringComparer.Ordinal)
        {
            "true", "false", "null",
        };

        public static IReadOnlyList<LexerStyleSpan> Tokenize(string text)
        {
            var spans = new List<LexerStyleSpan>();
            int n = text.Length;
            int i = 0;
            // 除号与正则定界符同形：仅当前一个有效 token 不是值（标识符/数字/字符串/常量/右括号）时按正则扫描
            bool prevIsValue = false;

            void Emit(int length, DwStyle style, bool? isValue = null)
            {
                if (length <= 0) return;
                int styleIndex = (int)style;
                if (spans.Count > 0 && spans[spans.Count - 1].Style == styleIndex)
                {
                    spans[spans.Count - 1] = new LexerStyleSpan(spans[spans.Count - 1].Length + length, styleIndex);
                }
                else
                {
                    spans.Add(new LexerStyleSpan(length, styleIndex));
                }
                if (isValue is { } v)
                {
                    prevIsValue = v;
                }
            }

            while (i < n)
            {
                char c = text[i];

                if (char.IsWhiteSpace(c))
                {
                    int start = i;
                    while (i < n && char.IsWhiteSpace(text[i])) i++;
                    Emit(i - start, DwStyle.Default);
                    continue;
                }

                if (c == '/' && i + 1 < n && text[i + 1] == '/')
                {
                    int start = i;
                    while (i < n && text[i] != '\n') i++;
                    Emit(i - start, DwStyle.Comment, false);
                    continue;
                }

                if (c == '/' && i + 1 < n && text[i + 1] == '*')
                {
                    int start = i;
                    i += 2;
                    while (i < n && !(text[i] == '*' && i + 1 < n && text[i + 1] == '/')) i++;
                    if (i < n) i += 2;
                    Emit(i - start, DwStyle.Comment, false);
                    continue;
                }

                if (c is '"' or '\'' or '`')
                {
                    i = ScanString(text, i, c, Emit);
                    continue;
                }

                if (c == '-' && i + 1 < n && text[i + 1] == '>')
                {
                    Emit(2, DwStyle.Arrow, false);
                    i += 2;
                    continue;
                }

                if (c == '-')
                {
                    int run = 1;
                    while (i + run < n && text[i + run] == '-') run++;
                    if (run >= 3)
                    {
                        Emit(run, DwStyle.Arrow, false);
                        i += run;
                    }
                    else
                    {
                        Emit(1, DwStyle.Operator, false);
                        i++;
                    }
                    continue;
                }

                if (c == '<' && i + 1 < n && text[i + 1] == '~')
                {
                    Emit(2, DwStyle.Arrow, false);
                    i += 2;
                    continue;
                }

                if (c == '%')
                {
                    int start = i++;
                    while (i < n && IsIdentPart(text[i])) i++;
                    Emit(i - start, DwStyle.Directive, false);
                    continue;
                }

                if (c == '|')
                {
                    int end = i + 1;
                    while (end < n && text[end] != '|' && text[end] != '\n') end++;
                    if (end < n && text[end] == '|' && end > i + 1)
                    {
                        Emit(end - i + 1, DwStyle.Number, true);
                        i = end + 1;
                    }
                    else
                    {
                        Emit(1, DwStyle.Operator, false);
                        i++;
                    }
                    continue;
                }

                if (c == '$')
                {
                    int start = i++;
                    while (i < n && (IsIdentPart(text[i]) || text[i] == '$')) i++;
                    Emit(i - start, DwStyle.Variable, true);
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < n && char.IsDigit(text[i])) i++;
                    if (i + 1 < n && text[i] == '.' && char.IsDigit(text[i + 1]))
                    {
                        i++;
                        while (i < n && char.IsDigit(text[i])) i++;
                    }
                    if (i < n && text[i] is 'e' or 'E')
                    {
                        int e = i + 1;
                        if (e < n && text[e] is '+' or '-') e++;
                        if (e < n && char.IsDigit(text[e]))
                        {
                            i = e;
                            while (i < n && char.IsDigit(text[i])) i++;
                        }
                    }
                    Emit(i - start, DwStyle.Number, true);
                    continue;
                }

                if (IsIdentStart(c))
                {
                    int start = i;
                    while (i < n && IsIdentPart(text[i])) i++;
                    string word = text.Substring(start, i - start);
                    DwStyle style = ClassifyIdentifier(word);
                    Emit(i - start, style, style is DwStyle.Constant or DwStyle.Default);
                    if (word == "ns") i = ScanNamespaceTarget(text, i, Emit);
                    continue;
                }

                if (c == '/')
                {
                    if (prevIsValue)
                    {
                        Emit(1, DwStyle.Operator, false);
                        i++;
                        continue;
                    }
                    int end = i + 1;
                    while (end < n && text[end] != '\n')
                    {
                        if (text[end] == '\\') { end += 2; continue; }
                        if (text[end] == '/') break;
                        end++;
                    }
                    if (end < n && text[end] == '/')
                    {
                        Emit(end - i + 1, DwStyle.String, true);
                        i = end + 1;
                    }
                    else
                    {
                        Emit(1, DwStyle.Operator, false);
                        i++;
                    }
                    continue;
                }

                if (c == '?' && i + 2 < n && text[i + 1] == '?' && text[i + 2] == '?')
                {
                    Emit(3, DwStyle.Operator, false);
                    i += 3;
                    continue;
                }

                if (c == '.')
                {
                    if (i + 1 < n && text[i + 1] is '.' or '*' or '^' or '@')
                    {
                        Emit(2, DwStyle.Arrow, false);
                        i += 2;
                    }
                    else
                    {
                        Emit(1, DwStyle.Operator, false);
                        i++;
                    }
                    continue;
                }

                if (c is '=' or '!' or '<' or '>' or '~')
                {
                    if (i + 1 < n && text[i + 1] == '=')
                    {
                        Emit(2, DwStyle.Operator, false);
                        i += 2;
                    }
                    else
                    {
                        Emit(1, DwStyle.Operator, false);
                        i++;
                    }
                    continue;
                }

                if (c is '+' or '*' or ':')
                {
                    Emit(1, DwStyle.Operator, false);
                    i++;
                    continue;
                }

                if (c is ')' or ']')
                {
                    Emit(1, DwStyle.Default, true);
                    i++;
                    continue;
                }

                if (c is '(' or '[' or '{' or ',')
                {
                    Emit(1, DwStyle.Default, false);
                    i++;
                    continue;
                }

                Emit(1, DwStyle.Default);
                i++;
            }

            return spans;
        }

        private static DwStyle ClassifyIdentifier(string word)
        {
            if (ControlKeywords.Contains(word) || OperatorWords.Contains(word) || Modifiers.Contains(word))
            {
                return DwStyle.Keyword;
            }
            if (Declarations.Contains(word)) return DwStyle.Declaration;
            if (Constants.Contains(word)) return DwStyle.Constant;
            return DwStyle.Default;
        }

        /// ns 指令带别名与 URI 两个参数（tmLanguage 的 namespace 规则）：别名 → Variable，URI → String
        private static int ScanNamespaceTarget(string text, int i, Action<int, DwStyle, bool?> emit)
        {
            int n = text.Length;
            int start = i;
            while (i < n && text[i] is ' ' or '\t') i++;
            emit(i - start, DwStyle.Default, null);
            if (i >= n || !IsIdentStart(text[i])) return i;

            start = i;
            while (i < n && IsIdentPart(text[i])) i++;
            emit(i - start, DwStyle.Variable, false);

            start = i;
            while (i < n && text[i] is ' ' or '\t') i++;
            emit(i - start, DwStyle.Default, null);

            start = i;
            while (i < n && !char.IsWhiteSpace(text[i])) i++;
            emit(i - start, DwStyle.String, false);
            return i;
        }

        private static int ScanString(string text, int i, char quote, Action<int, DwStyle, bool?> emit)
        {
            int n = text.Length;
            int runStart = i;
            i++;
            while (i < n)
            {
                char c = text[i];
                if (c == '\\')
                {
                    i = Math.Min(i + 2, n);
                    continue;
                }
                if (c == quote)
                {
                    i++;
                    emit(i - runStart, DwStyle.String, true);
                    return i;
                }
                if (c == '$' && i + 1 < n)
                {
                    if (text[i + 1] == '(')
                    {
                        emit(i - runStart, DwStyle.String, null);
                        emit(2, DwStyle.Interpolation, false);
                        i += 2;
                        i = SkipInterpolation(text, i, emit);
                        runStart = i;
                        continue;
                    }
                    if (IsIdentStart(text[i + 1]))
                    {
                        emit(i - runStart, DwStyle.String, null);
                        int start = i++;
                        while (i < n && IsIdentPart(text[i])) i++;
                        emit(i - start, DwStyle.Interpolation, false);
                        runStart = i;
                        continue;
                    }
                }
                i++;
            }
            emit(i - runStart, DwStyle.String, true);
            return i;
        }

        /// 跳过 $( 到配对的 )：跟踪括号深度并跳过内嵌字符串，避免引号提前终结外层字符串
        private static int SkipInterpolation(string text, int i, Action<int, DwStyle, bool?> emit)
        {
            int n = text.Length;
            int exprStart = i;
            int depth = 1;
            while (i < n && depth > 0)
            {
                char c = text[i];
                if (c == '\\')
                {
                    i = Math.Min(i + 2, n);
                    continue;
                }
                if (c is '"' or '\'' or '`')
                {
                    i = SkipPlainString(text, i, c);
                    continue;
                }
                if (c == '(') depth++;
                else if (c == ')') depth--;
                i++;
            }
            int exprEnd = depth == 0 ? i - 1 : i;
            emit(exprEnd - exprStart, DwStyle.String, null);
            if (depth == 0) emit(1, DwStyle.Interpolation, false);
            return i;
        }

        private static int SkipPlainString(string text, int i, char quote)
        {
            int n = text.Length;
            i++;
            while (i < n)
            {
                if (text[i] == '\\')
                {
                    i = Math.Min(i + 2, n);
                    continue;
                }
                if (text[i] == quote)
                {
                    i++;
                    break;
                }
                i++;
            }
            return i;
        }

        private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

        private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}

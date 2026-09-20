using System.Text;
using Mnemosyne.Plugin.Abstractions;

namespace Mnemosyne.Formatters.Html;

/// <summary>
/// HTML 格式化器：自实现容错解析（不规则 HTML 不报错），按块级/行内元素规则重新缩进排版。
/// 任何意外都兜底返回原文（排版失败不应升级为用户可见错误）。
/// </summary>
public sealed class HtmlFormatter : IMnemosynePlugin, ICodeFormatter
{
    public string Id => "mnemosyne.formatters.html";

    public string DisplayName => "HTML";

    public string Version => "1.0.0";

    public string Description => "HTML 代码格式化";

    public IReadOnlyList<PluginSettingDescriptor> Settings => [];

    public void Initialize(IPluginContext context)
    {
    }

    public IReadOnlyList<string> LanguageIds => ["html"];

    // 空元素（无结束标签）
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta",
        "param", "source", "track", "wbr",
    };

    // 行内元素：与文本同行排版，不独占一行
    private static readonly HashSet<string> InlineElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "abbr", "b", "bdi", "bdo", "cite", "code", "data", "dfn", "em", "i", "kbd",
        "mark", "q", "rp", "rt", "ruby", "s", "samp", "small", "span", "strong", "sub",
        "sup", "time", "u", "var",
    };

    // 原样内容元素：script/style 内容逐行重排缩进，pre/textarea 内容逐字节保留
    private static readonly HashSet<string> RawTextElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "pre", "textarea",
    };

    public string Format(string input, FormatterOptions options)
    {
        try
        {
            return Layout(Tokenize(input), options);
        }
        catch (Exception)
        {
            // 容错约定：任何意外都不报错，原样返回
            return input;
        }
    }

    private enum TokenKind { Text, StartTag, EndTag, Comment, Declaration, RawContent }

    private readonly struct Token
    {
        public Token(TokenKind kind, string name, string content)
        {
            Kind = kind;
            Name = name;
            Content = content;
        }

        public TokenKind Kind { get; }

        public string Name { get; }

        public string Content { get; }
    }

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        int pos = 0;
        while (pos < input.Length)
        {
            int lt = input.IndexOf('<', pos);
            if (lt < 0)
            {
                AddText(tokens, input.Substring(pos));
                break;
            }
            if (lt > pos) AddText(tokens, input.Substring(pos, lt - pos));

            if (StartsWithAt(input, lt, "<!--"))
            {
                int end = input.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                end = end < 0 ? input.Length : end + 3;
                tokens.Add(new Token(TokenKind.Comment, "", input.Substring(lt, end - lt)));
                pos = end;
            }
            else if (lt + 1 < input.Length && (input[lt + 1] == '!' || input[lt + 1] == '?'))
            {
                // DOCTYPE / CDATA / 处理指令：原样独占一行
                int end = input.IndexOf('>', lt + 2);
                end = end < 0 ? input.Length : end + 1;
                tokens.Add(new Token(TokenKind.Declaration, "", input.Substring(lt, end - lt)));
                pos = end;
            }
            else if (lt + 1 < input.Length && input[lt + 1] == '/')
            {
                int end = input.IndexOf('>', lt + 2);
                end = end < 0 ? input.Length : end + 1;
                string raw = input.Substring(lt, end - lt);
                tokens.Add(new Token(TokenKind.EndTag, ExtractName(raw, 2), NormalizeTag(raw)));
                pos = end;
            }
            else if (lt + 1 < input.Length && char.IsLetter(input[lt + 1]))
            {
                int end = FindTagEnd(input, lt + 1);
                string raw = input.Substring(lt, end - lt);
                string name = ExtractName(raw, 1);
                tokens.Add(new Token(TokenKind.StartTag, name, NormalizeTag(raw)));
                pos = end;
                // 原样内容元素：直接扫到对应结束标签，中间内容不做任何解析
                if (RawTextElements.Contains(name) && !raw.TrimEnd().EndsWith("/>", StringComparison.Ordinal))
                {
                    string closing = "</" + name;
                    int closeStart = input.IndexOf(closing, pos, StringComparison.OrdinalIgnoreCase);
                    if (closeStart < 0)
                    {
                        tokens.Add(new Token(TokenKind.RawContent, name, input.Substring(pos)));
                        pos = input.Length;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.RawContent, name, input.Substring(pos, closeStart - pos)));
                        pos = closeStart;
                    }
                }
            }
            else
            {
                // 孤立的 '<' 当普通文本
                AddText(tokens, "<");
                pos = lt + 1;
            }
        }
        return tokens;
    }

    private static bool StartsWithAt(string input, int offset, string value) =>
        offset + value.Length <= input.Length &&
        string.Compare(input, offset, value, 0, value.Length, StringComparison.Ordinal) == 0;

    private static void AddText(List<Token> tokens, string text)
    {
        if (text.Length > 0) tokens.Add(new Token(TokenKind.Text, "", text));
    }

    // 找开始标签的收尾 '>'，跳过引号内的 '>'
    private static int FindTagEnd(string input, int from)
    {
        char quote = '\0';
        for (int i = from; i < input.Length; i++)
        {
            char c = input[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c == '"' || c == '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i + 1;
            }
        }
        return input.Length;
    }

    private static string ExtractName(string rawTag, int skip)
    {
        int i = skip;
        while (i < rawTag.Length && !char.IsWhiteSpace(rawTag[i]) && rawTag[i] != '>' && rawTag[i] != '/') i++;
        return rawTag.Substring(skip, i - skip).ToLowerInvariant();
    }

    // 标签内部空白折叠为单个空格（引号内的空白不动），如 <div   class="a"\n id="b"> → <div class="a" id="b">
    private static string NormalizeTag(string rawTag)
    {
        var builder = new StringBuilder(rawTag.Length);
        char quote = '\0';
        bool pendingSpace = false;
        foreach (char c in rawTag)
        {
            if (quote != '\0')
            {
                builder.Append(c);
                if (c == quote) quote = '\0';
            }
            else if (c == '"' || c == '\'')
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                pendingSpace = false;
                builder.Append(c);
                quote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
            }
            else
            {
                if (pendingSpace && builder.Length > 0 && builder[builder.Length - 1] != '<') builder.Append(' ');
                pendingSpace = false;
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    private static string Layout(List<Token> tokens, FormatterOptions options)
    {
        var output = new StringBuilder();
        var inline = new StringBuilder();
        int indent = 0;
        int lineIndent = 0;
        // 当前行缓冲以哪个块级开始标签开头（结束标签同名时合并成一行，如 <li>one</li>）
        string? lineStartTag = null;

        void FlushInline()
        {
            if (inline.Length == 0) return;
            output.Append(options.GetIndent(lineIndent)).Append(inline).Append('\n');
            inline.Clear();
            lineStartTag = null;
        }

        void WriteOwnLine(string text)
        {
            FlushInline();
            output.Append(options.GetIndent(indent)).Append(text).Append('\n');
        }

        void AppendInline(string fragment, bool spaceBefore)
        {
            if (inline.Length == 0)
            {
                lineIndent = indent;
            }
            else if (spaceBefore && inline[inline.Length - 1] != ' ' && inline[inline.Length - 1] != '>')
            {
                inline.Append(' ');
            }
            inline.Append(fragment);
        }

        foreach (Token token in tokens)
        {
            switch (token.Kind)
            {
                case TokenKind.Text:
                    string text = CollapseWhitespace(token.Content);
                    if (text.Length > 0) AppendInline(text, spaceBefore: true);
                    break;

                case TokenKind.Comment:
                case TokenKind.Declaration:
                    WriteOwnLine(token.Content.Trim());
                    break;

                case TokenKind.StartTag:
                    if (RawTextElements.Contains(token.Name))
                    {
                        // 原样内容元素：开始标签独占一行，且不增加缩进层级（结束标签按原层级输出）
                        WriteOwnLine(token.Content);
                    }
                    else if (VoidElements.Contains(token.Name))
                    {
                        if (token.Name == "hr")
                        {
                            WriteOwnLine(token.Content);
                        }
                        else
                        {
                            AppendInline(token.Content, spaceBefore: true);
                        }
                    }
                    else if (InlineElements.Contains(token.Name))
                    {
                        AppendInline(token.Content, spaceBefore: true);
                    }
                    else if (token.Content.TrimEnd().EndsWith("/>", StringComparison.Ordinal))
                    {
                        WriteOwnLine(token.Content);
                    }
                    else
                    {
                        FlushInline();
                        inline.Append(token.Content);
                        lineIndent = indent;
                        lineStartTag = token.Name;
                        indent++;
                    }
                    break;

                case TokenKind.RawContent:
                    FlushInline();
                    WriteRawContent(output, token, options, indent);
                    break;

                case TokenKind.EndTag:
                    if (InlineElements.Contains(token.Name))
                    {
                        AppendInline(token.Content, spaceBefore: false);
                    }
                    else if (RawTextElements.Contains(token.Name))
                    {
                        WriteOwnLine(token.Content);
                    }
                    else if (inline.Length > 0 && lineStartTag == token.Name)
                    {
                        // 行首就是匹配的开始标签：合并为一行（<p>Hello <b>x</b></p>）
                        inline.Append(token.Content);
                        if (indent > 0) indent--;
                        FlushInline();
                    }
                    else
                    {
                        // 行内有未闭合的行内容先输出；未匹配/多余的结束标签缩进钳位 0，不报错
                        FlushInline();
                        if (indent > 0) indent--;
                        WriteOwnLine(token.Content);
                    }
                    break;
            }
        }
        FlushInline();
        return output.ToString();
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
            }
            else
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                pendingSpace = false;
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    private static void WriteRawContent(StringBuilder output, Token token, FormatterOptions options, int indent)
    {
        string content = token.Content;
        if (string.IsNullOrWhiteSpace(content)) return;
        if (token.Name == "pre" || token.Name == "textarea")
        {
            // pre/textarea 的空白有语义，逐字节保留
            output.Append(content.Trim('\r', '\n'));
            if (content.Length == 0 || content[content.Length - 1] != '\n') output.Append('\n');
            return;
        }
        // script/style：去掉每行原有缩进后按当前层级重排
        string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            output.Append(options.GetIndent(indent + 1)).Append(trimmed).Append('\n');
        }
    }
}

using System.Net;
using System.Text;

namespace Mnemosyne.Services;

/// <summary>
/// HTML 片段 → Markdown 转换器：自实现容错迷你 DOM（不引第三方解析库），
/// 覆盖网页剪贴板常见结构（表格/标题/列表/引用/代码/链接/行内格式）。
/// 任何意外输入不得抛异常，兜底退化为去标签纯文本。
/// </summary>
public static class HtmlToMarkdownConverter
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta",
        "param", "source", "track", "wbr",
    };

    // 内容整体丢弃的元素
    private static readonly HashSet<string> SkipElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "title", "meta", "link", "template", "noscript",
        "svg", "iframe", "object", "applet", "select",
    };

    private static readonly HashSet<string> InlineElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "abbr", "b", "bdi", "bdo", "cite", "code", "data", "del", "dfn", "em",
        "font", "i", "ins", "kbd", "mark", "q", "s", "samp", "small", "span", "strike",
        "strong", "sub", "sup", "time", "u", "var", "wbr",
    };

    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "center", "dd", "details", "dialog",
        "div", "dl", "dt", "fieldset", "figcaption", "figure", "footer", "form",
        "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr", "li", "main",
        "nav", "ol", "p", "pre", "section", "summary", "table", "ul",
    };

    public static string Convert(string html)
    {
        try
        {
            string result = Renderer.Render(Parse(html));
            if (result.Trim().Length > 0) return result;
        }
        catch (Exception)
        {
            // 容错约定：转换失败降级为纯文本，粘贴永远不应报错
        }
        return ExtractPlainText(html);
    }

    #region 解析

    private sealed class Node
    {
        public Node(string tag) => Tag = tag;

        public string Tag { get; }

        public string? Text { get; set; }

        public Dictionary<string, string>? Attrs { get; set; }

        public List<Node>? Children { get; set; }

        public bool IsText => Tag == "#text";

        public string? Attr(string name) =>
            Attrs != null && Attrs.TryGetValue(name, out string? value) ? value : null;

        public void AddChild(Node child) => (Children ??= []).Add(child);
    }

    private static Node Parse(string html)
    {
        var root = new Node("");
        var stack = new List<Node> { root };
        int pos = 0;
        while (pos < html.Length)
        {
            int lt = html.IndexOf('<', pos);
            if (lt < 0)
            {
                AddText(stack[^1], html.Substring(pos));
                break;
            }
            if (lt > pos) AddText(stack[^1], html.Substring(pos, lt - pos));

            if (StartsWithAt(html, lt, "<!--"))
            {
                int end = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                pos = end < 0 ? html.Length : end + 3;
            }
            else if (lt + 1 < html.Length && (html[lt + 1] == '!' || html[lt + 1] == '?'))
            {
                int end = html.IndexOf('>', lt + 2);
                pos = end < 0 ? html.Length : end + 1;
            }
            else if (lt + 1 < html.Length && html[lt + 1] == '/')
            {
                int end = html.IndexOf('>', lt + 2);
                end = end < 0 ? html.Length : end + 1;
                CloseTag(stack, ExtractName(html, lt + 2, end));
                pos = end;
            }
            else if (lt + 1 < html.Length && char.IsLetter(html[lt + 1]))
            {
                int end = FindTagEnd(html, lt + 1);
                string raw = html.Substring(lt, end - lt);
                string name = ExtractName(html, lt + 1, end);
                bool selfClosing = raw.TrimEnd().EndsWith("/>", StringComparison.Ordinal);
                pos = end;

                if (SkipElements.Contains(name))
                {
                    if (!selfClosing && !VoidElements.Contains(name))
                        pos = SkipRawContent(html, pos, name);
                    continue;
                }

                if (!selfClosing && !VoidElements.Contains(name))
                {
                    // 隐式闭合（如 <li> 后紧跟 <li>）须先弹出再挂接，否则会嵌套成子级
                    while (stack.Count > 1 && ImplicitlyCloses(name, stack[^1].Tag))
                        stack.RemoveAt(stack.Count - 1);
                }

                var node = new Node(name) { Attrs = ParseAttrs(raw) };
                stack[^1].AddChild(node);

                // pre/textarea 不做原文消费：网页复制来的代码块常带 <span>/<code> 语法着色标签，
                // 正常解析后由 CollectRawText 拼接文本；空白保留靠渲染端不折叠实现
                if (!selfClosing && !VoidElements.Contains(name))
                {
                    stack.Add(node);
                }
            }
            else
            {
                AddText(stack[^1], "<");
                pos = lt + 1;
            }
        }
        return root;
    }

    private static void AddText(Node parent, string text)
    {
        if (text.Length == 0) return;
        parent.AddChild(new Node("#text") { Text = WebUtility.HtmlDecode(text) });
    }

    private static void CloseTag(List<Node> stack, string name)
    {
        for (int i = stack.Count - 1; i >= 1; i--)
        {
            if (stack[i].Tag == name)
            {
                stack.RemoveRange(i, stack.Count - i);
                return;
            }
        }
    }

    private static bool ImplicitlyCloses(string newTag, string openTag) => newTag switch
    {
        "li" => openTag == "li",
        "tr" => openTag is "tr" or "td" or "th",
        "td" or "th" => openTag is "td" or "th",
        "dt" or "dd" => openTag is "dt" or "dd",
        "option" => openTag == "option",
        _ => BlockElements.Contains(newTag) && openTag == "p",
    };

    // 跳过元素内容直至匹配结束标签之后；找不到结束标签则丢弃剩余全部内容
    private static int SkipRawContent(string html, int from, string name)
    {
        int closeStart = FindCloseStart(html, from, name);
        if (closeStart < 0) return html.Length;
        int end = html.IndexOf('>', closeStart + 2);
        return end < 0 ? html.Length : end + 1;
    }

    private static int FindCloseStart(string html, int from, string name) =>
        html.IndexOf("</" + name, from, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithAt(string input, int offset, string value) =>
        offset + value.Length <= input.Length &&
        string.Compare(input, offset, value, 0, value.Length, StringComparison.Ordinal) == 0;

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
            else if (c is '"' or '\'')
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

    private static string ExtractName(string html, int from, int limit)
    {
        int i = from;
        while (i < limit && !char.IsWhiteSpace(html[i]) && html[i] != '>' && html[i] != '/') i++;
        return html.Substring(from, i - from).ToLowerInvariant();
    }

    private static Dictionary<string, string>? ParseAttrs(string rawTag)
    {
        Dictionary<string, string>? attrs = null;
        int i = 1;
        while (i < rawTag.Length && !char.IsWhiteSpace(rawTag[i]) && rawTag[i] != '>' && rawTag[i] != '/') i++;
        while (i < rawTag.Length && rawTag[i] != '>')
        {
            while (i < rawTag.Length && (char.IsWhiteSpace(rawTag[i]) || rawTag[i] == '/')) i++;
            if (i >= rawTag.Length || rawTag[i] == '>') break;
            int nameStart = i;
            while (i < rawTag.Length && !char.IsWhiteSpace(rawTag[i]) && rawTag[i] != '=' && rawTag[i] != '>' && rawTag[i] != '/') i++;
            string name = rawTag.Substring(nameStart, i - nameStart).ToLowerInvariant();
            string value = "";
            while (i < rawTag.Length && char.IsWhiteSpace(rawTag[i])) i++;
            if (i < rawTag.Length && rawTag[i] == '=')
            {
                i++;
                while (i < rawTag.Length && char.IsWhiteSpace(rawTag[i])) i++;
                if (i < rawTag.Length && rawTag[i] is '"' or '\'')
                {
                    char quote = rawTag[i++];
                    int valueStart = i;
                    while (i < rawTag.Length && rawTag[i] != quote) i++;
                    value = rawTag.Substring(valueStart, i - valueStart);
                    if (i < rawTag.Length) i++;
                }
                else
                {
                    int valueStart = i;
                    while (i < rawTag.Length && !char.IsWhiteSpace(rawTag[i]) && rawTag[i] != '>') i++;
                    value = rawTag.Substring(valueStart, i - valueStart);
                }
            }
            if (name.Length > 0)
                (attrs ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))[name] =
                    WebUtility.HtmlDecode(value);
        }
        return attrs;
    }

    private static string ExtractPlainText(string html)
    {
        var builder = new StringBuilder(html.Length);
        bool inTag = false;
        foreach (char c in html)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) builder.Append(c);
        }
        return WebUtility.HtmlDecode(builder.ToString()).Trim() + "\n";
    }

    #endregion

    #region 渲染

    private sealed class Renderer
    {
        private readonly StringBuilder _sb = new();

        // 紧凑模式（列表项内）：块间用单个换行而非空行，避免松散列表
        private readonly bool _tight;

        private Renderer(bool tight = false) => _tight = tight;

        public static string Render(Node root)
        {
            var renderer = new Renderer();
            renderer.RenderContainer(root);
            return renderer.Finish();
        }

        private string Finish() => _sb.ToString().TrimEnd('\n') + "\n";

        // 块间恰好一个空行：先剥掉尾部换行再补分隔
        private void BeginBlock()
        {
            int len = _sb.Length;
            while (len > 0 && _sb[len - 1] == '\n') len--;
            _sb.Length = len;
            if (len > 0) _sb.Append(_tight ? "\n" : "\n\n");
        }

        private void RenderContainer(Node container)
        {
            if (container.Children == null) return;
            var inline = new StringBuilder();
            foreach (Node child in container.Children)
            {
                if (child.IsText)
                {
                    inline.Append(RenderText(child));
                }
                else if (IsInlineTag(child.Tag))
                {
                    inline.Append(RenderInlineElement(child, inTableCell: false));
                }
                else
                {
                    FlushParagraph(inline);
                    RenderBlock(child);
                }
            }
            FlushParagraph(inline);
        }

        private void FlushParagraph(StringBuilder inline)
        {
            string text = inline.ToString().Trim();
            inline.Clear();
            if (text.Length == 0) return;
            BeginBlock();
            _sb.Append(text);
        }

        private void RenderBlock(Node el)
        {
            switch (el.Tag)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    string heading = RenderInlineChildren(el, inTableCell: false).Trim();
                    if (heading.Length == 0) return;
                    BeginBlock();
                    _sb.Append('#', el.Tag[1] - '0').Append(' ').Append(heading);
                    break;

                case "hr":
                    BeginBlock();
                    _sb.Append("---");
                    break;

                case "pre":
                    RenderPre(el);
                    break;

                case "blockquote":
                    RenderBlockquote(el);
                    break;

                case "ul" or "ol":
                    RenderList(el);
                    break;

                case "table":
                    RenderTable(el);
                    break;

                case "p":
                    string paragraph = RenderInlineChildren(el, inTableCell: false).Trim();
                    if (paragraph.Length == 0) return;
                    BeginBlock();
                    _sb.Append(paragraph);
                    break;

                case "html" or "body":
                    RenderContainer(el);
                    break;

                default:
                    if (HasBlockChild(el))
                    {
                        RenderContainer(el);
                    }
                    else
                    {
                        string text = RenderInlineChildren(el, inTableCell: false).Trim();
                        if (text.Length == 0) return;
                        BeginBlock();
                        _sb.Append(text);
                    }
                    break;
            }
        }

        private void RenderPre(Node el)
        {
            string code = CollectRawText(el).Trim('\r', '\n');
            if (code.Trim().Length == 0) return;
            string fence = code.Contains("```", StringComparison.Ordinal) ? "````" : "```";
            BeginBlock();
            _sb.Append(fence).Append(ExtractLanguage(el)).Append('\n').Append(code).Append('\n').Append(fence);
        }

        private static string ExtractLanguage(Node pre)
        {
            string? classValue = pre.Attr("class");
            Node? code = pre.Children?.FirstOrDefault(c => c.Tag == "code");
            classValue ??= code?.Attr("class");
            if (classValue == null) return "";
            foreach (string part in classValue.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.StartsWith("language-", StringComparison.Ordinal)) return part[9..];
                if (part.StartsWith("lang-", StringComparison.Ordinal)) return part[5..];
            }
            return "";
        }

        private void RenderBlockquote(Node el)
        {
            var inner = new Renderer();
            inner.RenderContainer(el);
            string text = inner._sb.ToString().TrimEnd('\n');
            if (text.Trim().Length == 0) return;
            BeginBlock();
            foreach (string line in text.Split('\n'))
                _sb.Append(line.Length == 0 ? ">" : "> " + line.TrimEnd('\r')).Append('\n');
            _sb.Length--; // 块约定不带尾部换行
        }

        private void RenderList(Node list)
        {
            if (list.Children == null) return;
            bool ordered = list.Tag == "ol";
            int number = ParsePositiveInt(list.Attr("start"), 1);
            BeginBlock();
            bool first = true;
            foreach (Node child in list.Children)
            {
                if (child.Tag != "li") continue;
                var item = new Renderer(tight: true);
                item.RenderContainer(child);
                string content = item._sb.ToString().TrimEnd('\n');
                string checkbox = ExtractCheckbox(child);
                if (content.Trim().Length == 0 && checkbox.Length == 0) continue;

                if (!first) _sb.Append('\n');
                first = false;
                string marker = ordered ? number++ + ". " : "- ";
                string[] lines = content.Split('\n');
                _sb.Append(marker).Append(checkbox).Append(lines[0]);
                for (int i = 1; i < lines.Length; i++)
                {
                    _sb.Append('\n');
                    // 续行按标记宽度缩进，嵌套列表/引用保持归属当前项
                    if (lines[i].Length > 0) _sb.Append(' ', marker.Length).Append(lines[i].TrimEnd('\r'));
                }
            }
        }

        private static string ExtractCheckbox(Node li)
        {
            Node? input = li.Children?.FirstOrDefault(c =>
                c.Tag == "input" && string.Equals(c.Attr("type"), "checkbox", StringComparison.OrdinalIgnoreCase));
            if (input == null) return "";
            return input.Attr("checked") != null ? "[x] " : "[ ] ";
        }

        private void RenderTable(Node table)
        {
            var rows = new List<List<string>>();
            int columns = 0;
            CollectRows(table, rows, ref columns);
            if (rows.Count == 0 || columns == 0) return;

            BeginBlock();
            AppendTableRow(rows[0], columns);
            _sb.Append('\n');
            _sb.Append('|');
            for (int i = 0; i < columns; i++) _sb.Append(" --- |");
            for (int r = 1; r < rows.Count; r++)
            {
                _sb.Append('\n');
                AppendTableRow(rows[r], columns);
            }
        }

        // 只收集属于本表的行，不下钻嵌套表格（嵌套表内容作为单元格行内文本拍平）
        private static void CollectRows(Node node, List<List<string>> rows, ref int columns)
        {
            if (node.Children == null) return;
            foreach (Node child in node.Children)
            {
                if (child.Tag == "tr")
                {
                    var cells = new List<string>();
                    if (child.Children != null)
                    {
                        foreach (Node cell in child.Children)
                        {
                            if (cell.Tag is not ("td" or "th")) continue;
                            // 单元格内的换行降级为空格：预览渲染器不处理 HtmlInline，<br> 会被丢弃
                            string text = RenderInlineChildren(cell, inTableCell: true).Trim()
                                .Replace("\r", "").Replace("\n", " ").Replace("|", "\\|");
                            cells.Add(text);
                            // colspan 无法表达：首列放内容，其余补空单元格；rowspan 忽略
                            int span = ParsePositiveInt(cell.Attr("colspan"), 1);
                            for (int i = 1; i < span; i++) cells.Add("");
                        }
                    }
                    if (cells.Count == 0) continue;
                    columns = Math.Max(columns, cells.Count);
                    rows.Add(cells);
                }
                else if (child.Tag != "table")
                {
                    CollectRows(child, rows, ref columns);
                }
            }
        }

        private void AppendTableRow(List<string> cells, int columns)
        {
            _sb.Append('|');
            for (int i = 0; i < columns; i++)
                _sb.Append(' ').Append(i < cells.Count ? cells[i] : "").Append(" |");
        }

        private static bool HasBlockChild(Node node) =>
            node.Children != null && node.Children.Any(c => !c.IsText && IsBlockTag(c.Tag));

        private static bool IsBlockTag(string tag) => BlockElements.Contains(tag);

        private static bool IsInlineTag(string tag) =>
            InlineElements.Contains(tag) || tag is "img" or "br" or "input";

        private static string RenderInlineChildren(Node node, bool inTableCell)
        {
            if (node.Children == null) return "";
            var builder = new StringBuilder();
            foreach (Node child in node.Children)
                builder.Append(child.IsText
                    ? RenderText(child)
                    : RenderInlineElement(child, inTableCell));
            return builder.ToString();
        }

        private static string RenderInlineElement(Node el, bool inTableCell)
        {
            switch (el.Tag)
            {
                case "b" or "strong": return Wrap(el, "**", inTableCell);
                case "i" or "em": return Wrap(el, "*", inTableCell);
                case "s" or "del" or "strike": return Wrap(el, "~~", inTableCell);
                case "u" or "ins": return RenderInlineChildren(el, inTableCell);
                case "q": return "“" + RenderInlineChildren(el, inTableCell).Trim() + "”";
                case "code":
                    string code = CollapseWhitespace(CollectRawText(el)).Trim();
                    if (code.Length == 0) return "";
                    return code.Contains('`') ? "`` " + code + " ``" : "`" + code + "`";
                case "a":
                    string text = RenderInlineChildren(el, inTableCell).Trim();
                    string? href = el.Attr("href");
                    if (string.IsNullOrEmpty(href)) return text;
                    if (text.Length == 0) text = href;
                    return "[" + text + "](" + href + ")";
                case "img":
                    string? src = el.Attr("src");
                    if (string.IsNullOrEmpty(src)) return "";
                    return "![" + (el.Attr("alt") ?? "") + "](" + src + ")";
                case "br":
                    return inTableCell ? " " : "  \n";
                case "wbr":
                case "input":
                    return "";
                default:
                    return RenderInlineChildren(el, inTableCell);
            }
        }

        private static string Wrap(Node el, string marker, bool inTableCell)
        {
            string inner = RenderInlineChildren(el, inTableCell).Trim();
            return inner.Length == 0 ? "" : marker + inner + marker;
        }

        private static string RenderText(Node node)
        {
            string text = node.Text ?? "";
            text = text.Replace('\u00A0', ' ');
            bool leadingSpace = text.Length > 0 && char.IsWhiteSpace(text[0]);
            string core = CollapseWhitespace(text);
            bool trailingSpace = core.Length > 0 && text.Length > 0 && char.IsWhiteSpace(text[^1]);
            return (leadingSpace ? " " : "") + EscapeMarkdown(core) + (trailingSpace ? " " : "");
        }

        private static string CollectRawText(Node node)
        {
            var builder = new StringBuilder();
            Collect(node);
            // 文本节点在解析期已解码实体，这里直接拼接，不做二次解码
            return builder.ToString();

            void Collect(Node current)
            {
                if (current.IsText)
                {
                    builder.Append(current.Text);
                    return;
                }
                if (current.Children == null) return;
                foreach (Node child in current.Children) Collect(child);
            }
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

        private static string EscapeMarkdown(string text)
        {
            if (text.Length == 0) return text;
            return text
                .Replace("\\", "\\\\")
                .Replace("`", "\\`")
                .Replace("*", "\\*")
                .Replace("_", "\\_")
                .Replace("[", "\\[")
                .Replace("]", "\\]");
        }

        private static int ParsePositiveInt(string? value, int fallback) =>
            int.TryParse(value, out int result) && result > 0 ? result : fallback;
    }

    #endregion
}

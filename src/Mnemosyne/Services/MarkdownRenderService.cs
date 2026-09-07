using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdTable = Markdig.Extensions.Tables.Table;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace Mnemosyne.Services;

/// <summary>
/// Markdig（GFM 扩展）解析 Markdown → WPF 原生控件树（不用 WebView2，requirements.md 2）。
/// 全部颜色经 SetResourceReference 引用主题资源键，主题切换自动跟随；仅在 UI 线程调用。
/// </summary>
public class MarkdownRenderService
{
    private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private readonly LocalizationService _localization;

    public MarkdownRenderService(LocalizationService localization)
    {
        _localization = localization;
    }

    private sealed record RenderContext(string? BaseDirectory, Action<string>? LinkHandler, string ForegroundKey, double BaseFontSize);

    /// <summary>渲染 Markdown 文本为控件树；baseDirectory 用于解析图片与链接的相对路径（源 md 文件目录）；字体字号沿用编辑器设置</summary>
    public FrameworkElement Render(string markdown, string? baseDirectory, Action<string>? linkHandler, string fontFamily, double fontSize)
    {
        var context = new RenderContext(baseDirectory, linkHandler, "Brush.Window.Foreground", Math.Clamp(fontSize, 6, 72));
        MarkdownDocument document = Markdown.Parse(markdown, s_pipeline);
        var panel = new StackPanel();
        // 根上设可继承字体属性，整棵树（含代码块）跟随编辑器字体字号
        if (!string.IsNullOrWhiteSpace(fontFamily)) TextElement.SetFontFamily(panel, new WpfFontFamily(fontFamily));
        TextElement.SetFontSize(panel, context.BaseFontSize);
        foreach (MdBlock block in document)
        {
            panel.Children.Add(RenderBlock(block, context));
        }
        return panel;
    }

    private UIElement RenderBlock(MdBlock block, RenderContext context)
    {
        UIElement element = block switch
        {
            HeadingBlock heading => RenderHeading(heading, context),
            ParagraphBlock paragraph => RenderParagraph(paragraph, context),
            ListBlock list => RenderList(list, context),
            FencedCodeBlock fenced => RenderCodeBlock(fenced.Lines, context),
            CodeBlock code => RenderCodeBlock(code.Lines, context),
            QuoteBlock quote => RenderQuote(quote, context),
            MdTable table => RenderTable(table, context),
            ThematicBreakBlock => RenderHorizontalRule(),
            HtmlBlock html => RenderHtmlBlock(html, context),
            _ => new TextBlock(),
        };
        if (element is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 0, 10);
        return element;
    }

    private UIElement RenderHeading(HeadingBlock heading, RenderContext context)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold };
        // 标题字号按编辑器字号等比放大（比例对应原 12pt 基准下的 24/20/17/15/13.5）
        text.FontSize = heading.Level switch
        {
            1 => context.BaseFontSize * 2.0,
            2 => context.BaseFontSize * 1.67,
            3 => context.BaseFontSize * 1.42,
            4 => context.BaseFontSize * 1.25,
            _ => context.BaseFontSize * 1.125,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, context.ForegroundKey);
        if (heading.Inline is not null) AppendInlines(text.Inlines, heading.Inline, context);

        // 一/二级标题带下边框线（常见 Markdown 预览观感）
        if (heading.Level <= 2)
        {
            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 4),
                Child = text,
            };
            border.SetResourceReference(Border.BorderBrushProperty, "Brush.Control.Border");
            return border;
        }
        return text;
    }

    private UIElement RenderParagraph(ParagraphBlock paragraph, RenderContext context)
    {
        // 段落仅含一张图片时按块级图片渲染（占整行，不受文本行高约束）
        if (paragraph.Inline?.FirstChild is LinkInline { IsImage: true } image && ReferenceEquals(image, paragraph.Inline.LastChild))
        {
            return CreateImage(image.Url, ExtractPlainText(image), context);
        }

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, context.ForegroundKey);
        if (paragraph.Inline is not null) AppendInlines(text.Inlines, paragraph.Inline, context);
        return text;
    }

    private UIElement RenderList(ListBlock list, RenderContext context)
    {
        var panel = new StackPanel();
        int number = 1;
        if (list.IsOrdered && int.TryParse(list.OrderedStart, out int start)) number = start;

        foreach (MdBlock item in list)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var marker = new TextBlock
            {
                Text = list.IsOrdered ? number.ToString() + "." : "•",
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 8, 0),
            };
            marker.SetResourceReference(TextBlock.ForegroundProperty, context.ForegroundKey);
            number++;

            var content = new StackPanel();
            if (item is Markdig.Syntax.ContainerBlock container)
            {
                foreach (MdBlock child in container)
                {
                    content.Children.Add(RenderBlock(child, context));
                }
            }
            Grid.SetColumn(content, 1);
            grid.Children.Add(marker);
            grid.Children.Add(content);
            panel.Children.Add(grid);
        }
        return panel;
    }

    private UIElement RenderCodeBlock(StringLineGroup lines, RenderContext context)
    {
        var builder = new StringBuilder();
        foreach (StringLine line in lines.Lines)
        {
            builder.Append(line.ToString()).Append('\n');
        }
        if (builder.Length > 0) builder.Length--;

        var text = new TextBlock { Text = builder.ToString() };
        text.SetResourceReference(TextBlock.ForegroundProperty, context.ForegroundKey);
        var border = new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(4),
            Child = text,
        };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Control.Background");
        return border;
    }

    private UIElement RenderQuote(QuoteBlock quote, RenderContext context)
    {
        // 引用内文字用引用色（套嵌引用保持同一前景色）
        var innerContext = context with { ForegroundKey = "Brush.Markdown.Quote" };
        var content = new StackPanel();
        foreach (MdBlock child in quote)
        {
            content.Children.Add(RenderBlock(child, innerContext));
        }
        var border = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 2, 0, 2),
            Child = content,
        };
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Markdown.Quote");
        return border;
    }

    private UIElement RenderTable(MdTable table, RenderContext context)
    {
        var grid = new Grid();
        int columnCount = 0;
        foreach (MdTableRow row in table)
        {
            columnCount = Math.Max(columnCount, row.Count);
        }
        for (int i = 0; i < columnCount; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        int rowIndex = 0;
        foreach (MdTableRow row in table)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                MdTableCell cell = (MdTableCell)row[columnIndex];
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
                text.SetResourceReference(TextBlock.ForegroundProperty, context.ForegroundKey);
                if (row.IsHeader) text.FontWeight = FontWeights.Bold;
                if (columnIndex < table.ColumnDefinitions.Count &&
                    table.ColumnDefinitions[columnIndex].Alignment is { } alignment)
                {
                    text.TextAlignment = alignment switch
                    {
                        TableColumnAlign.Center => TextAlignment.Center,
                        TableColumnAlign.Right => TextAlignment.Right,
                        _ => TextAlignment.Left,
                    };
                }
                foreach (MdBlock cellBlock in cell)
                {
                    if (cellBlock is Markdig.Syntax.ParagraphBlock cellParagraph && cellParagraph.Inline is not null)
                    {
                        AppendInlines(text.Inlines, cellParagraph.Inline, context);
                    }
                }

                // 单元格只画右/下边框，外层 Border 补左/上边框，拼出完整表格线
                var cellBorder = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(8, 4, 8, 4),
                    Child = text,
                };
                cellBorder.SetResourceReference(Border.BorderBrushProperty, "Brush.Control.Border");
                if (row.IsHeader) cellBorder.SetResourceReference(Border.BackgroundProperty, "Brush.Control.Background");
                Grid.SetRow(cellBorder, rowIndex);
                Grid.SetColumn(cellBorder, columnIndex);
                grid.Children.Add(cellBorder);
            }
            rowIndex++;
        }

        var outer = new Border
        {
            BorderThickness = new Thickness(1, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = grid,
        };
        outer.SetResourceReference(Border.BorderBrushProperty, "Brush.Control.Border");
        return outer;
    }

    private UIElement RenderHorizontalRule()
    {
        var border = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4) };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Control.Border");
        return border;
    }

    private UIElement RenderHtmlBlock(HtmlBlock html, RenderContext context)
    {
        var builder = new StringBuilder();
        foreach (StringLine line in html.Lines.Lines)
        {
            builder.Append(line.ToString()).Append('\n');
        }
        var text = new TextBlock { Text = builder.ToString().TrimEnd() };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Secondary.Foreground");
        return text;
    }

    private void AppendInlines(InlineCollection inlines, ContainerInline container, RenderContext context)
    {
        for (MdInline? inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            AppendInline(inlines, inline, context);
        }
    }

    private void AppendInline(InlineCollection inlines, MdInline inline, RenderContext context)
    {
        switch (inline)
        {
            case LiteralInline literal:
                inlines.Add(new Run(literal.Content.ToString()));
                break;
            case LineBreakInline lineBreak:
                inlines.Add(lineBreak.IsHard ? (System.Windows.Documents.Inline)new LineBreak() : new Run(" "));
                break;
            case EmphasisInline emphasis:
            {
                var span = new Span();
                if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.Bold;
                else span.FontStyle = FontStyles.Italic;
                AppendInlines(span.Inlines, emphasis, context);
                inlines.Add(span);
                break;
            }
            case CodeInline code:
            {
                var run = new Run(code.Content);
                run.SetResourceReference(TextElement.BackgroundProperty, "Brush.Control.Background");
                inlines.Add(run);
                break;
            }
            case LinkInline { IsImage: true } image:
                inlines.Add(new InlineUIContainer(CreateImage(image.Url, ExtractPlainText(image), context)));
                break;
            case LinkInline link:
            {
                var hyperlink = new Hyperlink { Cursor = Cursors.Hand };
                hyperlink.SetResourceReference(TextElement.ForegroundProperty, "Brush.Markdown.Link");
                hyperlink.TextDecorations = TextDecorations.Underline;
                if (link.FirstChild is null)
                {
                    hyperlink.Inlines.Add(new Run(link.Url ?? string.Empty));
                }
                else
                {
                    AppendInlines(hyperlink.Inlines, link, context);
                }
                string url = link.Url ?? string.Empty;
                if (!string.IsNullOrEmpty(link.Title)) hyperlink.ToolTip = link.Title;
                hyperlink.Click += (_, _) => context.LinkHandler?.Invoke(url);
                inlines.Add(hyperlink);
                break;
            }
            case ContainerInline container:
                AppendInlines(inlines, container, context);
                break;
        }
    }

    private static string ExtractPlainText(ContainerInline container)
    {
        var builder = new StringBuilder();
        for (MdInline? inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            if (inline is LiteralInline literal) builder.Append(literal.Content.ToString());
            else if (inline is ContainerInline nested) builder.Append(ExtractPlainText(nested));
        }
        return builder.ToString();
    }

    private UIElement CreateImage(string? url, string alt, RenderContext context)
    {
        string? path = url is null ? null : ResolveLocalPath(url, context);
        if (path is not null)
        {
            try
            {
                // 读字节后经 MemoryStream 加载 + OnLoad + Freeze：不占文件锁，保存后刷新能拿到新图
                byte[] bytes = File.ReadAllBytes(path);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.EndInit();
                bitmap.Freeze();
                return new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    MaxWidth = 800,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    ToolTip = string.IsNullOrEmpty(alt) ? url : alt,
                };
            }
            catch (Exception)
            {
                // 文件缺失/格式不支持：落到占位提示
            }
        }

        var placeholderText = new TextBlock
        {
            Text = string.Format(_localization.GetString("Loc.Markdown.ImageFailed"),
                string.IsNullOrEmpty(alt) ? url ?? string.Empty : alt),
            FontStyle = FontStyles.Italic,
        };
        placeholderText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Secondary.Foreground");
        var placeholder = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = placeholderText,
        };
        placeholder.SetResourceReference(Border.BackgroundProperty, "Brush.Control.Background");
        placeholder.SetResourceReference(Border.BorderBrushProperty, "Brush.Control.Border");
        return placeholder;
    }

    private static string? ResolveLocalPath(string url, RenderContext context)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            // 需求仅要求本地相对路径图片；网络图片按加载失败处理（占位提示）
            return uri.IsFile ? uri.LocalPath : null;
        }
        if (context.BaseDirectory is null) return null;
        string relative = Uri.UnescapeDataString(url.Replace('/', Path.DirectorySeparatorChar));
        return Path.GetFullPath(Path.Combine(context.BaseDirectory, relative));
    }
}

using System.Text;
using System.Xml;
using System.Xml.Linq;
using Mnemosyne.Plugin.Abstractions;

namespace Mnemosyne.Formatters.Xml;

/// <summary>XML 格式化器：XDocument 解析后按 FormatterOptions 缩进美化，保留 XML 声明、注释、CDATA 与处理指令。</summary>
public sealed class XmlFormatter : ICodeFormatter
{
    public string DisplayName => "XML";

    public IReadOnlyList<string> LanguageIds => ["xml"];

    public string Format(string input, FormatterOptions options)
    {
        XDocument document;
        try
        {
            // 不带 PreserveWhitespace：丢弃仅含空白的文本节点以便重新缩进；
            // 混合内容（元素内含文本）XmlWriter 不会插入换行，不会破坏语义
            document = XDocument.Parse(input);
        }
        catch (XmlException ex)
        {
            // XmlException 的行列号本身 1 起始，0 表示无位置信息
            int? line = ex.LineNumber > 0 ? ex.LineNumber : null;
            int? column = ex.LinePosition > 0 ? ex.LinePosition : null;
            throw new FormatterException($"Invalid XML (line {line?.ToString() ?? "?"}, column {column?.ToString() ?? "?"}): {ex.Message}", line, column, ex);
        }

        var builder = new StringBuilder(input.Length + input.Length / 8);
        // XmlWriter 走 StringBuilder 时会把声明里的 encoding 写成 utf-16，
        // 因此声明按原文的版本/编码/独立标记手工重写，正文交给 XmlWriter（OmitXmlDeclaration）
        if (document.Declaration is { } declaration)
        {
            builder.Append("<?xml version=\"").Append(string.IsNullOrEmpty(declaration.Version) ? "1.0" : declaration.Version).Append('"');
            if (!string.IsNullOrEmpty(declaration.Encoding))
            {
                builder.Append(" encoding=\"").Append(declaration.Encoding).Append('"');
            }
            if (!string.IsNullOrEmpty(declaration.Standalone))
            {
                builder.Append(" standalone=\"").Append(declaration.Standalone).Append('"');
            }
            builder.Append("?>\n");
        }

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = options.UseTabs ? "\t" : new string(' ', Math.Max(1, options.IndentWidth)),
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = true,
        };
        using (XmlWriter writer = XmlWriter.Create(builder, settings))
        {
            document.Save(writer);
        }
        return builder.ToString();
    }
}

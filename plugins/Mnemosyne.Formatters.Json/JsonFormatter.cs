using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Mnemosyne.Plugin.Abstractions;

namespace Mnemosyne.Formatters.Json;

/// <summary>JSON 格式化器：System.Text.Json 严格解析后按 FormatterOptions 缩进美化输出。</summary>
public sealed class JsonFormatter : IMnemosynePlugin, ICodeFormatter
{
    // 宽松转义：非 ASCII（中文等）保持原字符可读，不转 \uXXXX
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Id => "mnemosyne.formatters.json";

    public string DisplayName => "JSON";

    public string Version => "1.0.0";

    public string Description => "JSON 代码格式化";

    public IReadOnlyList<PluginSettingDescriptor> Settings => [];

    public void Initialize(IPluginContext context)
    {
    }

    public IReadOnlyList<string> LanguageIds => ["json"];

    public string Format(string input, FormatterOptions options)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(input);
        }
        catch (JsonException ex)
        {
            // LineNumber/BytePositionInLine 均为 0 起始；列是行内字节偏移，多字节字符下有偏差，仅作定位参考
            int? line = ex.LineNumber is { } ln ? (int)ln + 1 : null;
            int? column = ex.BytePositionInLine is { } bp ? (int)bp + 1 : null;
            throw new FormatterException($"Invalid JSON (line {line?.ToString() ?? "?"}, column {column?.ToString() ?? "?"}): {ex.Message}", line, column, ex);
        }

        using (document)
        {
            var builder = new StringBuilder(input.Length + input.Length / 4);
            WriteElement(document.RootElement, options, 0, builder);
            return builder.ToString();
        }
    }

    private static void WriteElement(JsonElement element, FormatterOptions options, int level, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(element, options, level, builder);
                break;
            case JsonValueKind.Array:
                WriteArray(element, options, level, builder);
                break;
            case JsonValueKind.String:
                builder.Append(JsonSerializer.Serialize(element.GetString(), SerializerOptions));
                break;
            default:
                // 数字/布尔/null 原样输出原始文本
                builder.Append(element.GetRawText());
                break;
        }
    }

    private static void WriteObject(JsonElement element, FormatterOptions options, int level, StringBuilder builder)
    {
        if (!element.EnumerateObject().Any())
        {
            builder.Append("{}");
            return;
        }
        builder.Append('{');
        bool first = true;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            builder.Append(first ? '\n' : ',');
            if (!first) builder.Append('\n');
            first = false;
            builder.Append(options.GetIndent(level + 1));
            builder.Append(JsonSerializer.Serialize(property.Name, SerializerOptions));
            builder.Append(": ");
            WriteElement(property.Value, options, level + 1, builder);
        }
        builder.Append('\n');
        builder.Append(options.GetIndent(level));
        builder.Append('}');
    }

    private static void WriteArray(JsonElement element, FormatterOptions options, int level, StringBuilder builder)
    {
        if (!element.EnumerateArray().Any())
        {
            builder.Append("[]");
            return;
        }
        builder.Append('[');
        bool first = true;
        foreach (JsonElement item in element.EnumerateArray())
        {
            builder.Append(first ? '\n' : ',');
            if (!first) builder.Append('\n');
            first = false;
            builder.Append(options.GetIndent(level + 1));
            WriteElement(item, options, level + 1, builder);
        }
        builder.Append('\n');
        builder.Append(options.GetIndent(level));
        builder.Append(']');
    }
}

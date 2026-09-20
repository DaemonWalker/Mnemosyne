namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 代码格式化能力契约。插件类需同时实现 <see cref="IMnemosynePlugin"/>（提供身份与设置），
/// 主程序扫描 exe 同目录 plugins/ 下的 dll 发现插件后按能力登记。
/// </summary>
public interface ICodeFormatter
{
    /// <summary>
    /// 支持的语言标识列表（不区分大小写），如 "json"、"xml"、"html"。
    /// 主程序按当前文档的语言标识匹配格式化器。
    /// </summary>
    IReadOnlyList<string> LanguageIds { get; }

    /// <summary>
    /// 格式化输入文本并返回结果。实现必须是纯函数：不修改外部状态、不产生副作用。
    /// 输入无法解析时应抛出 <see cref="FormatterException"/>（携带位置信息），
    /// 主程序保证只有 Format 成功返回后才会用结果替换原文，抛异常时原文不受影响。
    /// </summary>
    /// <param name="input">文档全文。</param>
    /// <param name="options">格式化选项（缩进方式等，来自当前编辑器设置）。</param>
    /// <returns>格式化后的文本。行尾符约定为 "\n"，由主程序按文档当前行尾符转换。</returns>
    string Format(string input, FormatterOptions options);
}

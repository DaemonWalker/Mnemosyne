namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 自定义词法器样式的语义类别。主程序按语义从当前主题取色
/// （Default→Color.Editor.Foreground，其余→同名 Color.Editor.* 键）。
/// </summary>
public enum LexerStyleSemantic
{
    /// <summary>普通文本（前景色）。</summary>
    Default,
    /// <summary>注释。</summary>
    Comment,
    /// <summary>关键字。</summary>
    Keyword,
    /// <summary>字符串/字符字面量。</summary>
    String,
    /// <summary>数字字面量。</summary>
    Number,
    /// <summary>类型名。</summary>
    Type,
    /// <summary>函数名/插值表达式。</summary>
    Function,
    /// <summary>预处理器/指令。</summary>
    Preprocessor,
    /// <summary>错误。</summary>
    Error,
}

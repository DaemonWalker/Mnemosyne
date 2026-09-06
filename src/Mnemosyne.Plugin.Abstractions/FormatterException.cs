namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 格式化失败异常。插件因输入无法解析而失败时抛出此类型（消息应含位置信息，界面直接展示），
/// 与插件自身的意外异常区分：主程序对两者都会保护原文，但本类型的 Message 视为面向用户的说明。
/// </summary>
public sealed class FormatterException : Exception
{
    /// <summary>出错行号（1 起始）；无法定位时为 null。</summary>
    public int? Line { get; }

    /// <summary>出错列号（1 起始）；无法定位时为 null。</summary>
    public int? Column { get; }

    public FormatterException(string message, int? line = null, int? column = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Line = line;
        Column = column;
    }
}

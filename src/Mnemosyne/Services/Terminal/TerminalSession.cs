using System.Text;
using XtermSharp;
using XTerminal = XtermSharp.Terminal;

namespace Mnemosyne.Services.Terminal;

/// <summary>
/// 组合 XtermSharp.Terminal（VT 解析/屏幕缓冲）与 ConPtySession（shell 进程）。
/// Terminal 非线程安全：Feed/Resize/缓冲读取必须在同一线程（UI 线程）调用，
/// ConPTY 输出到达后经 OutputReceived（后台线程）转发给订阅者，由其切回 UI 线程再调 Feed。
/// </summary>
public sealed class TerminalSession : IDisposable
{
    public const int ScrollbackLines = 5000;

    private readonly XTerminal _terminal;
    private readonly SelectionService _selection;
    private readonly ConPtySession _conPty;
    private bool _disposed;

    public TerminalSession()
    {
        _terminal = new XTerminal(new DelegateSink(this), new TerminalOptions
        {
            Cols = 80,
            Rows = 24,
            Scrollback = ScrollbackLines,
            TermName = "xterm-256color",
        });
        _selection = new SelectionService(_terminal);
        _conPty = new ConPtySession();
        _conPty.OutputReceived += data => OutputReceived?.Invoke(data);
        _conPty.Exited += code => Exited?.Invoke(code);
    }

    public XTerminal Terminal => _terminal;

    public SelectionService Selection => _selection;

    public bool IsRunning => _conPty.IsRunning;

    /// <summary>ConPTY 原始输出到达（后台线程；订阅者须切到 UI 线程后调用 Feed）</summary>
    public event Action<byte[]>? OutputReceived;

    /// <summary>shell 进程退出（后台线程），参数为退出码</summary>
    public event Action<int>? Exited;

    /// <summary>启动 shell；不支持/启动失败时抛异常，由调用方转为界面提示</summary>
    public void Start(string shellExe, string workingDirectory, int cols, int rows)
    {
        _terminal.Resize(cols, rows);
        _conPty.Start(shellExe, workingDirectory, cols, rows);
    }

    /// <summary>把 shell 输出喂给 VT 解析器（必须在 UI 线程）</summary>
    public void Feed(byte[] data)
    {
        if (_disposed) return;
        _terminal.Feed(data, data.Length);
    }

    /// <summary>发送原始字节到 shell（按键序列，UI 线程调用）</summary>
    public void SendBytes(byte[] data) => _conPty.Write(data);

    /// <summary>发送文本输入（键入字符或粘贴）；粘贴内容在 bracketed paste 模式下加包装序列</summary>
    public void SendText(string text, bool isPaste = false)
    {
        if (text.Length == 0) return;
        byte[] payload = Encoding.UTF8.GetBytes(text);
        if (isPaste && _terminal.BracketedPasteMode)
        {
            byte[] wrapped = new byte[payload.Length + 12];
            Array.Copy(Encoding.ASCII.GetBytes("\x1b[200~"), wrapped, 6);
            Array.Copy(payload, 0, wrapped, 6, payload.Length);
            Array.Copy(Encoding.ASCII.GetBytes("\x1b[201~"), 0, wrapped, 6 + payload.Length, 6);
            payload = wrapped;
        }
        _conPty.Write(payload);
    }

    /// <summary>终端行列数变化（UI 线程）：同步 VT 缓冲与伪控制台尺寸</summary>
    public void Resize(int cols, int rows)
    {
        if (_disposed || cols <= 0 || rows <= 0) return;
        if (_terminal.Cols == cols && _terminal.Rows == rows) return;
        _terminal.Resize(cols, rows);
        _conPty.Resize(cols, rows);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conPty.Dispose();
    }

    /// <summary>XtermSharp 回写（如 DA/CPR 应答、标题设置回执）直接转给 shell</summary>
    private sealed class DelegateSink(TerminalSession session) : SimpleTerminalDelegate
    {
        public override void Send(byte[] data) => session._conPty.Write(data);

        public override bool IsProcessTrusted() => true;
    }
}

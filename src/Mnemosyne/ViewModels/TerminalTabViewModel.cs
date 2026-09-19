using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemosyne.Services;
using Mnemosyne.Services.Terminal;

namespace Mnemosyne.ViewModels;

/// <summary>
/// 单个终端 tab 的 ViewModel：持有一个独立 ConPTY 会话。
/// 隐藏面板保留会话；进程退出时显示 overlay，重新激活后自动重建。
/// 会话惰性启动——由视图在获得实际像素尺寸后调用 EnsureSession。
/// </summary>
public partial class TerminalTabViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly Func<string> _workingDirectoryProvider;
    private readonly Dispatcher _dispatcher;
    private int _lastCols = 80;
    private int _lastRows = 24;
    private bool _exited;

    public TerminalTabViewModel(LocalizationService localization, Func<string> workingDirectoryProvider, string shellName, string title)
    {
        _localization = localization;
        _workingDirectoryProvider = workingDirectoryProvider;
        _dispatcher = Dispatcher.CurrentDispatcher;
        ShellName = shellName;
        _title = title;
    }

    /// <summary>该 tab 的 shell（powershell/pwsh/cmd），创建时定死</summary>
    public string ShellName { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private TerminalSession? _session;

    /// <summary>是否当前激活 tab（由容器维护，视图据此切 Visibility）</summary>
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOverlay))]
    private string? _overlayMessage;

    public bool ShowOverlay => OverlayMessage is not null;

    /// <summary>视图按当前像素尺寸换算的行列数调用；首次创建并启动会话，进程已退出则自动重建</summary>
    public void EnsureSession(int cols, int rows)
    {
        // 面板被压扁时换算出行列可能为 0：钳到最小尺寸，保证会话总能启动
        _lastCols = Math.Max(cols, 20);
        _lastRows = Math.Max(rows, 5);
        if (Session is not null && !_exited)
        {
            Session.Resize(_lastCols, _lastRows);
            return;
        }
        CloseSession();
        StartNewSession();
    }

    /// <summary>终止 shell 进程并释放会话（关闭 tab / 窗口关闭 / 退出后重建时调用）</summary>
    public void CloseSession()
    {
        Session?.Dispose();
        Session = null;
        OverlayMessage = null;
        _exited = false;
    }

    private void StartNewSession()
    {
        _exited = false;
        if (!ConPtySession.IsSupported)
        {
            OverlayMessage = _localization.GetString("Loc.Terminal.Unsupported");
            return;
        }

        var session = new TerminalSession();
        session.Exited += OnSessionExited;
        Session = session;
        try
        {
            session.Start(ShellExePath(ShellName), _workingDirectoryProvider(), _lastCols, _lastRows);
            OverlayMessage = null;
        }
        catch (Exception ex)
        {
            OverlayMessage = string.Format(_localization.GetString("Loc.Terminal.StartFailed"), ShellName, ex.Message);
        }
    }

    private void OnSessionExited(int exitCode)
    {
        _dispatcher.BeginInvoke(() =>
        {
            _exited = true;
            OverlayMessage = string.Format(_localization.GetString("Loc.Terminal.ProcessExited"), exitCode);
        });
    }

    private static string ShellExePath(string shell) => shell switch
    {
        "cmd" => "cmd.exe",
        "pwsh" => "pwsh.exe",
        _ => "powershell.exe",
    };
}

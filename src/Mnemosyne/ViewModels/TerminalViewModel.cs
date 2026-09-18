using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemosyne.Models;
using Mnemosyne.Services;
using Mnemosyne.Services.Terminal;

namespace Mnemosyne.ViewModels;

/// <summary>
/// 终端面板 ViewModel。v1 单会话：VSCode 式显隐——隐藏面板保留会话，
/// 进程退出/切换 shell/窗口关闭时才销毁；重新展开时发现会话已退出则自动重建。
/// 终端实例懒创建于首次展开面板，不进入冷启动路径。
/// </summary>
public partial class TerminalViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly AppSettings _settings;
    private readonly Func<string> _workingDirectoryProvider;
    private readonly Dispatcher _dispatcher;
    private int _lastCols = 80;
    private int _lastRows = 24;
    private bool _exited;

    public TerminalViewModel(LocalizationService localization, AppSettings settings, Func<string> workingDirectoryProvider)
    {
        _localization = localization;
        _settings = settings;
        _workingDirectoryProvider = workingDirectoryProvider;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _selectedShell = NormalizeShell(_settings.TerminalShell);
    }

    private static string NormalizeShell(string? shell) =>
        string.IsNullOrWhiteSpace(shell) ? "powershell" : shell;

    /// <summary>终端字号（沿用编辑器字号设置）</summary>
    public double FontSize => _settings.FontSize;

    [ObservableProperty]
    private TerminalSession? _session;

    [ObservableProperty]
    private string _selectedShell;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOverlay))]
    private string? _overlayMessage;

    public bool ShowOverlay => OverlayMessage is not null;

    /// <summary>面板展开后由视图按当前像素尺寸换算的行列数调用；首次创建并启动会话，进程已退出则自动重建</summary>
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

    /// <summary>切换 shell 时重建会话</summary>
    private void RestartSession()
    {
        CloseSession();
        StartNewSession();
    }

    /// <summary>终止 shell 进程并释放会话（切换 shell / 窗口关闭 / 退出后重建时调用）</summary>
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
            session.Start(ShellExePath(SelectedShell), _workingDirectoryProvider(), _lastCols, _lastRows);
            OverlayMessage = null;
        }
        catch (Exception ex)
        {
            OverlayMessage = string.Format(_localization.GetString("Loc.Terminal.StartFailed"), SelectedShell, ex.Message);
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

    partial void OnSelectedShellChanged(string value)
    {
        // ComboBox TwoWay 绑定在 ItemsSource 刷新瞬间可能回推 null/空，忽略并还原，避免选中框被清空
        if (string.IsNullOrWhiteSpace(value))
        {
            SelectedShell = NormalizeShell(_settings.TerminalShell);
            return;
        }
        // 切换 shell 下拉即重建会话（v1 单会话语义）；持久化默认值走设置窗口
        if (Session is not null) RestartSession();
    }

    private static string ShellExePath(string shell) => shell switch
    {
        "cmd" => "cmd.exe",
        "pwsh" => "pwsh.exe",
        _ => "powershell.exe",
    };
}

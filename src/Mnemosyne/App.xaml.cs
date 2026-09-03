using System.IO;
using System.Windows;
using Mnemosyne.Models;
using Mnemosyne.Services;
using Mnemosyne.Views;

namespace Mnemosyne;

public partial class App : Application
{
    public ConfigService ConfigService { get; private set; } = null!;
    public ThemeService ThemeService { get; private set; } = null!;
    public LocalizationService LocalizationService { get; private set; } = null!;

    // 命令行/次实例转发来的待打开路径暂存于此，Step 3 才消费
    public List<string> PendingOpenPaths { get; } = [];

    private SingleInstanceManager? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceManager();
        if (!_singleInstance.TryBecomePrimary(e.Args))
        {
            Shutdown();
            return;
        }

        ConfigService = new ConfigService();
        AppSettings settings = ConfigService.Load();

        ThemeService = new ThemeService(this);
        ThemeService.ApplyTheme(settings.Theme);

        LocalizationService = new LocalizationService(this);
        LocalizationService.SetLanguage(settings.Language);

        AddPendingPaths(e.Args);
        _singleInstance.ArgsReceived += args => Dispatcher.Invoke(() =>
        {
            AddPendingPaths(args);
            ActivateMainWindow();
        });
        _singleInstance.StartListening();

        MainWindow window = new(ConfigService, ThemeService, LocalizationService);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void AddPendingPaths(IEnumerable<string> args)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith('-')) continue;
            try
            {
                PendingOpenPaths.Add(Path.GetFullPath(arg.Trim('"')));
            }
            catch (Exception)
            {
                // 非法路径参数忽略，不影响启动
            }
        }
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is null) return;
        if (MainWindow.WindowState == WindowState.Minimized)
        {
            MainWindow.WindowState = WindowState.Normal;
        }
        MainWindow.Activate();
        // 解决 SetForegroundWindow 受限时窗口不置顶的问题：短暂 Topmost 再取消
        MainWindow.Topmost = true;
        MainWindow.Topmost = false;
        MainWindow.Focus();
    }
}

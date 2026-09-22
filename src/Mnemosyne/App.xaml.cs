using System.IO;
using System.Text;
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
    public FileService FileService { get; private set; } = null!;
    public RecentFilesService RecentFilesService { get; private set; } = null!;
    public PluginService PluginService { get; private set; } = null!;
    public MarkdownRenderService MarkdownRenderer { get; private set; } = null!;
    public SessionService SessionService { get; private set; } = null!;

    // 命令行/次实例转发来的待打开路径暂存于此，由 MainWindow 消费
    public List<string> PendingOpenPaths { get; } = [];

    private SingleInstanceManager? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 只记录不拦截：Handled 保持 false，维持默认崩溃退出语义
        DispatcherUnhandledException += (_, args) => CrashLogger.Log("Dispatcher", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                CrashLogger.Log("AppDomain", ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) => CrashLogger.Log("Task", args.Exception);

        // 配置要先于单实例检查加载：AllowMultipleInstances 决定是否走互斥/参数转发
        ConfigService = new ConfigService();
        AppSettings settings = ConfigService.Load();

        if (!settings.AllowMultipleInstances)
        {
            _singleInstance = new SingleInstanceManager();
            if (!_singleInstance.TryBecomePrimary(e.Args))
            {
                Shutdown();
                return;
            }
        }

        // GBK/Big5 等代码页编码需要注册 Provider（.NET Core 默认只有 UTF 系列）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ThemeService = new ThemeService(this);
        ThemeService.ApplyTheme(settings.Theme);

        LocalizationService = new LocalizationService(this);
        LocalizationService.SetLanguage(settings.Language);

        FileService = new FileService();
        RecentFilesService = new RecentFilesService();
        PluginService = new PluginService(ConfigService);
        MarkdownRenderer = new MarkdownRenderService(LocalizationService);
        SessionService = new SessionService();

        AddPendingPaths(e.Args);
        if (_singleInstance is not null)
        {
            _singleInstance.ArgsReceived += args => Dispatcher.Invoke(() =>
            {
                AddPendingPaths(args);
                ActivateMainWindow();
                (MainWindow as MainWindow)?.OpenPendingPaths();
            });
            _singleInstance.StartListening();
        }

        MainWindow window = new(ConfigService, ThemeService, LocalizationService, FileService, RecentFilesService, PluginService, MarkdownRenderer, SessionService);
        MainWindow = window;
        window.Show();
        // 插件扫描与会话恢复都放窗口显示后异步进行，不拖慢冷启动（architecture.md 4.1）。
        // 语言与主题由插件提供：须先扫描登记再恢复会话，否则恢复的文件按纯文本打开、
        // 插件主题因未注册回退 Dark；扫描仅反射加载几个小 dll，毫秒级
        _ = InitializeAsync(window);
    }

    private async Task InitializeAsync(MainWindow window)
    {
        await PluginService.ScanAsync();
        // 扫描后回到 UI 线程：登记插件主题并重放一次主题设置，命中插件主题时触发 ThemeChanged 重刷
        ThemeService.RegisterPluginThemes(PluginService.Themes);
        ThemeService.ApplyTheme(ConfigService.Settings.Theme);
        // 语言注册表就绪后再消费命令行路径、再恢复会话，否则这些文件会按纯文本打开
        window.OpenPendingPaths();
        await window.RestoreSessionAsync();
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

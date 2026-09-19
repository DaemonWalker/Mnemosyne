using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemosyne.Models;
using Mnemosyne.Services;

namespace Mnemosyne.ViewModels;

/// <summary>
/// 终端面板 ViewModel（tab 容器）：VSCode 式显隐——隐藏面板保留全部会话，
/// 窗口关闭时才销毁。每个 tab（TerminalTabViewModel）持有独立 ConPTY 会话。
/// 终端实例懒创建于首次展开面板，不进入冷启动路径。
/// </summary>
public partial class TerminalViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly AppSettings _settings;
    private readonly Func<string> _workingDirectoryProvider;

    public TerminalViewModel(LocalizationService localization, AppSettings settings, Func<string> workingDirectoryProvider)
    {
        _localization = localization;
        _settings = settings;
        _workingDirectoryProvider = workingDirectoryProvider;
        _selectedShell = NormalizeShell(_settings.TerminalShell);
    }

    private static string NormalizeShell(string? shell) =>
        string.IsNullOrWhiteSpace(shell) ? "powershell" : shell;

    /// <summary>终端字号（沿用编辑器字号设置）</summary>
    public double FontSize => _settings.FontSize;

    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private TerminalTabViewModel? _activeTab;

    /// <summary>新建 tab 使用的 shell；切换下拉只影响之后新建的 tab，不重启现有会话</summary>
    [ObservableProperty]
    private string _selectedShell;

    partial void OnActiveTabChanged(TerminalTabViewModel? oldValue, TerminalTabViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsActive = false;
        if (newValue is not null) newValue.IsActive = true;
    }

    partial void OnSelectedShellChanged(string value)
    {
        // ComboBox TwoWay 绑定在 ItemsSource 刷新瞬间可能回推 null/空，忽略并还原，避免选中框被清空
        if (string.IsNullOrWhiteSpace(value))
            SelectedShell = NormalizeShell(_settings.TerminalShell);
    }

    /// <summary>确保至少有一个 tab（首次展开面板时由视图调用）</summary>
    public void EnsureTab()
    {
        if (Tabs.Count == 0) NewTab();
    }

    /// <summary>用当前选中的 shell 新建 tab 并激活；会话惰性启动，由视图 EnsureSession 触发</summary>
    [RelayCommand]
    public void NewTab()
    {
        var tab = new TerminalTabViewModel(_localization, _workingDirectoryProvider, SelectedShell, MakeTitle(SelectedShell));
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    /// <summary>点击 tab 切换激活</summary>
    [RelayCommand]
    public void ActivateTab(TerminalTabViewModel? tab)
    {
        if (tab is not null && Tabs.Contains(tab)) ActiveTab = tab;
    }

    /// <summary>关闭 tab：终止其 shell 进程；关闭最后一个 tab 时自动补一个空白 tab（面板关闭走 × 按钮或 Ctrl+`）</summary>
    [RelayCommand]
    public void CloseTab(TerminalTabViewModel? tab)
    {
        if (tab is null || !Tabs.Contains(tab)) return;
        int index = Tabs.IndexOf(tab);
        tab.CloseSession();
        Tabs.Remove(tab);
        if (Tabs.Count == 0)
        {
            NewTab();
            return;
        }
        if (ReferenceEquals(ActiveTab, tab) || ActiveTab is null)
            ActiveTab = Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    /// <summary>窗口关闭时终止所有 tab 的 shell 进程</summary>
    public void CloseAllSessions()
    {
        foreach (TerminalTabViewModel tab in Tabs)
            tab.CloseSession();
    }

    // 同名 shell 的 tab 加序号区分：powershell、powershell (2)……
    private string MakeTitle(string shell)
    {
        int count = Tabs.Count(t => t.ShellName == shell);
        return count == 0 ? shell : $"{shell} ({count + 1})";
    }
}

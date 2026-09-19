using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Mnemosyne.Controls;
using Mnemosyne.ViewModels;

namespace Mnemosyne.Views;

public partial class TerminalPanelView : UserControl
{
    private TerminalViewModel? _viewModel;

    public TerminalPanelView()
    {
        InitializeComponent();
    }

    private void TerminalPanelView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as TerminalViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            // 首次展开面板时补一个 tab（会话惰性启动，等 GridSizeChanged 提供实际行列数）
            _viewModel.EnsureTab();
            EnsureActiveTabSession();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TerminalViewModel.ActiveTab)) return;
        EnsureActiveTabSession();
        // 切换 tab 后布局尚未完成，焦点延后到输入优先级再设置
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => FindActiveTerminal()?.Focus());
    }

    // 激活 tab 的控件可能已有尺寸（展开时尺寸事件先于 DataContext/激活到达），补一次 EnsureSession
    private void EnsureActiveTabSession()
    {
        if (_viewModel?.ActiveTab is not { } tab) return;
        TerminalControl? control = FindActiveTerminal();
        if (control is not null && control.ActualWidth > 0 && control.ActualHeight > 0)
            tab.EnsureSession(control.Cols, control.Rows);
    }

    // GridSizeChanged 无可变 sender，但只有可见（激活）tab 的控件尺寸 > 0 会触发，路由到 ActiveTab 即可
    private void Terminal_GridSizeChanged(int cols, int rows)
        => _viewModel?.ActiveTab?.EnsureSession(cols, rows);

    private TerminalControl? FindActiveTerminal()
    {
        if (_viewModel?.ActiveTab is null) return null;
        return FindTerminalFor(TerminalHosts, _viewModel.ActiveTab);
    }

    private static TerminalControl? FindTerminalFor(DependencyObject root, object dataContext)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is TerminalControl terminal && ReferenceEquals(terminal.DataContext, dataContext))
                return terminal;
            if (FindTerminalFor(child, dataContext) is { } found)
                return found;
        }
        return null;
    }

    public void FocusTerminal() => FindActiveTerminal()?.Focus();
}

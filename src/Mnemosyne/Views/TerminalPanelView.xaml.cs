using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
            Terminal.FontSize = _viewModel.FontSize;
            Terminal.Session = _viewModel.Session;
            // 面板可能已有尺寸（展开时尺寸事件先于 DataContext 到达），此时 GridSizeChanged 不会再触发，补一次
            if (Terminal.ActualWidth > 0 && Terminal.ActualHeight > 0)
                _viewModel.EnsureSession(Terminal.Cols, Terminal.Rows);
        }
        else
        {
            Terminal.Session = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalViewModel.Session))
        {
            Terminal.Session = _viewModel?.Session;
        }
    }

    private void Terminal_GridSizeChanged(int cols, int rows)
    {
        _viewModel?.EnsureSession(cols, rows);
    }

    public void FocusTerminal() => Terminal.Focus();
}

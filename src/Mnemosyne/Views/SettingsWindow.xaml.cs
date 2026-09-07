using System.Windows;
using Mnemosyne.ViewModels;

namespace Mnemosyne.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested = result => DialogResult = result;
    }
}

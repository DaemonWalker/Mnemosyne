using System.Windows;
using System.Windows.Input;
using Mnemosyne.Services;
using Mnemosyne.ViewModels;

namespace Mnemosyne.Views;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly ThemeService _themeService;
    private readonly LocalizationService _localization;
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow(ConfigService configService, ThemeService themeService, LocalizationService localization)
    {
        InitializeComponent();
        _configService = configService;
        _themeService = themeService;
        _localization = localization;
        DataContext = _viewModel;
    }

    // 仅注册快捷键与菜单入口，具体功能由后续 Step 实现
    private void PlaceholderCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
    }

    private void SearchInFolderCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.ShowSearchPanelCommand.Execute(null);
    }
}

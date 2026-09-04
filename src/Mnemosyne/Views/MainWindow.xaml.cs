using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Mnemosyne.Commands;
using Mnemosyne.Controls;
using Mnemosyne.Models;
using Mnemosyne.Services;
using Mnemosyne.ViewModels;
using WinForms = System.Windows.Forms;

namespace Mnemosyne.Views;

public partial class MainWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly MainWindowViewModel _viewModel;
    private readonly IReadOnlyList<RoutedUICommand> _appCommands;

    public MainWindow(ConfigService configService, ThemeService themeService, LocalizationService localization, FileService fileService, RecentFilesService recentFiles)
    {
        InitializeComponent();
        _localization = localization;
        _viewModel = new MainWindowViewModel(fileService, localization, configService.Settings, recentFiles);
        DataContext = _viewModel;

        _appCommands = typeof(AppCommands)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(p => p.GetValue(null))
            .OfType<RoutedUICommand>()
            .ToList();

        themeService.ThemeChanged += (_, _) => ScintillaHost.ApplyThemeToAll();

        _viewModel.OpenFilePicker = () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = _localization.GetString("Loc.Dialog.OpenFile.Filter"),
            };
            return dialog.ShowDialog(this) == true ? dialog.FileNames : null;
        };
        _viewModel.SaveFilePicker = suggestedName =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = suggestedName,
                Filter = _localization.GetString("Loc.Dialog.OpenFile.Filter"),
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        };
        _viewModel.ConfirmUnsavedClose = document =>
        {
            MessageBoxResult result = MessageBox.Show(this,
                string.Format(_localization.GetString("Loc.Dialog.SaveChanges.Message"), document.Title),
                _localization.GetString("Loc.Dialog.SaveChanges.Title"),
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            return result switch
            {
                MessageBoxResult.Yes => SavePromptResult.Save,
                MessageBoxResult.No => SavePromptResult.DontSave,
                _ => SavePromptResult.Cancel,
            };
        };
        _viewModel.ConfirmEncodingReload = () => MessageBox.Show(this,
            _localization.GetString("Loc.Encoding.Reload.Message"),
            _localization.GetString("Loc.Dialog.Confirm.Title"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        _viewModel.ShowError = (message, title) =>
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        _viewModel.OpenFolderPicker = () =>
        {
            // WPF 无自带文件夹选择框，复用已启用的 WinForms
            using var dialog = new WinForms.FolderBrowserDialog();
            return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.SelectedPath : null;
        };
        _viewModel.FileTree.ConfirmDelete = node =>
        {
            string key = node.IsDirectory ? "Loc.Dialog.Delete.Folder.Message" : "Loc.Dialog.Delete.File.Message";
            return MessageBox.Show(this,
                string.Format(_localization.GetString(key), node.FullPath),
                _localization.GetString("Loc.Dialog.Confirm.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        };
        _viewModel.FileTree.ShowError = (message, title) =>
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        _viewModel.FindBar.FocusEditorRequested = () => _viewModel.ActiveDocument?.Editor.FocusEditor();

        _viewModel.Documents.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (DocumentViewModel doc in e.OldItems) EditorHostGrid.Children.Remove(doc.Editor);
            }
            if (e.NewItems is not null)
            {
                foreach (DocumentViewModel doc in e.NewItems)
                {
                    doc.Editor.Visibility = Visibility.Collapsed;
                    doc.Editor.EditorKeyDown += OnEditorKeyDown;
                    EditorHostGrid.Children.Add(doc.Editor);
                }
            }
            UpdateEditorVisibility();
        };
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.ActiveDocument)) UpdateEditorVisibility();
        };

        Loaded += (_, _) => OpenPendingPaths();
    }

    /// <summary>消费 App.PendingOpenPaths（命令行与次实例转发来的路径）</summary>
    public void OpenPendingPaths()
    {
        if (Application.Current is not App app || app.PendingOpenPaths.Count == 0) return;
        string[] paths = app.PendingOpenPaths.ToArray();
        app.PendingOpenPaths.Clear();
        _ = _viewModel.OpenPathsAsync(paths);
    }

    private void UpdateEditorVisibility()
    {
        foreach (UIElement child in EditorHostGrid.Children)
        {
            child.Visibility = Visibility.Collapsed;
        }
        DocumentViewModel? active = _viewModel.ActiveDocument;
        if (active is null) return;
        active.Editor.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => active.Editor.FocusEditor());
    }

    // WinForms 子控件聚焦时 WPF 收不到快捷键，ScintillaHost 转发按键后在此匹配 AppCommands 手势
    private void OnEditorKeyDown(object? sender, WinForms.KeyEventArgs e)
    {
        if (!e.Control && !e.Alt) return;
        Key key = KeyInterop.KeyFromVirtualKey((int)e.KeyCode);
        ModifierKeys modifiers = ModifierKeys.None;
        if (e.Control) modifiers |= ModifierKeys.Control;
        if (e.Shift) modifiers |= ModifierKeys.Shift;
        if (e.Alt) modifiers |= ModifierKeys.Alt;

        foreach (RoutedUICommand command in _appCommands)
        {
            foreach (InputGesture gesture in command.InputGestures)
            {
                if (gesture is KeyGesture keyGesture && keyGesture.Key == key && keyGesture.Modifiers == modifiers)
                {
                    if (command.CanExecute(null, this)) command.Execute(null, this);
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _ = _viewModel.OpenPathsAsync(paths);
        }
    }

    private void EncodingButton_Click(object sender, RoutedEventArgs e)
    {
        DocumentViewModel? document = _viewModel.ActiveDocument;
        if (document is null || document.FilePath is null) return;

        var menu = new ContextMenu { Style = (Style)FindResource("PopupContextMenuStyle") };
        foreach (System.Text.Encoding encoding in EncodingCatalog.Options)
        {
            var item = new MenuItem
            {
                Style = (Style)FindResource("PopupMenuItemStyle"),
                Header = EncodingCatalog.DisplayName(encoding),
                IsChecked = EncodingCatalog.SameAs(document.CurrentEncoding, encoding),
            };
            System.Text.Encoding selected = encoding;
            item.Click += async (_, _) => await _viewModel.SwitchEncodingAsync(selected);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = (Button)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    private void LineEndingButton_Click(object sender, RoutedEventArgs e)
    {
        DocumentViewModel? document = _viewModel.ActiveDocument;
        if (document is null) return;

        var menu = new ContextMenu { Style = (Style)FindResource("PopupContextMenuStyle") };
        foreach ((string label, LineEnding ending) in new[] { ("CRLF", LineEnding.CrLf), ("LF", LineEnding.Lf) })
        {
            var item = new MenuItem
            {
                Style = (Style)FindResource("PopupMenuItemStyle"),
                Header = label,
                IsChecked = document.Editor.CurrentLineEnding == ending,
            };
            item.Click += (_, _) => document.ConvertLineEnding(ending);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = (Button)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        DocumentViewModel? document = _viewModel.ActiveDocument;
        if (document is null) return;

        var picker = new LanguagePickerWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedLanguage is { } language)
        {
            document.SetLanguage(language);
        }
    }

    private void OpenFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.OpenFileCommand.Execute(null);
    }

    private void OpenFolderCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.OpenFolderCommand.Execute(null);
    }

    // 最近打开子菜单每次展开时重建，保证内容与顺序最新；表头用 TextBlock 避免文件名中的下划线被当作快捷键标记
    private void RecentFilesMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        RebuildRecentMenu(RecentFilesMenu, _viewModel.RecentFiles.RecentFiles,
            path => _viewModel.OpenRecentFileCommand.Execute(path));
    }

    private void RecentFoldersMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        RebuildRecentMenu(RecentFoldersMenu, _viewModel.RecentFiles.RecentFolders,
            path => _viewModel.OpenRecentFolderCommand.Execute(path));
    }

    private void RebuildRecentMenu(MenuItem parent, IReadOnlyList<RecentEntry> entries, Action<string> open)
    {
        parent.Items.Clear();
        if (entries.Count == 0)
        {
            parent.Items.Add(new MenuItem
            {
                Header = _localization.GetString("Loc.Menu.File.RecentEmpty"),
                IsEnabled = false,
            });
            return;
        }
        foreach (RecentEntry entry in entries)
        {
            var item = new MenuItem
            {
                Header = new TextBlock { Text = entry.DisplayName },
                ToolTip = entry.FullPath,
            };
            string path = entry.FullPath;
            item.Click += (_, _) => open(path);
            parent.Items.Add(item);
        }
    }

    private void SaveCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.SaveActiveCommand.Execute(null);
    }

    private void SaveAsCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.SaveActiveAsCommand.Execute(null);
    }

    private void SaveCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _viewModel.HasActiveDocument;
    }

    private void CloseTabCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.CloseActiveTabCommand.Execute(null);
    }

    private void SearchInFolderCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.ShowSearchPanelCommand.Execute(null);
        // 面板刚从 Collapsed 变 Visible 时尚未布局完成，焦点延后到输入优先级再设置
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => SearchPanel.FocusSearchBox());
    }

    private void FindCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        OpenFindBar(replace: false);
    }

    private void ReplaceCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        OpenFindBar(replace: true);
    }

    private void OpenFindBar(bool replace)
    {
        _viewModel.FindBar.Open(replace);
        // 浮层刚从 Collapsed 变 Visible 时尚未布局完成，焦点延后到输入优先级再设置
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => FindBar.FocusSearchBox());
    }

    // 仅注册快捷键与菜单入口，具体功能由后续 Step 实现
    private void PlaceholderCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
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
    private readonly ConfigService _configService;
    private readonly PluginService _pluginService;
    private readonly ThemeService _themeService;
    private readonly MainWindowViewModel _viewModel;
    private readonly IReadOnlyList<RoutedUICommand> _appCommands;

    // 每个 Tab 在 EditorHostGrid 中的常驻内容：普通文档是其 ScintillaHost，预览 Tab 是纯 WPF 视图
    private readonly Dictionary<DocumentViewModel, UIElement> _tabContents = new();

    public MainWindow(ConfigService configService, ThemeService themeService, LocalizationService localization, FileService fileService, RecentFilesService recentFiles, PluginService pluginService, MarkdownRenderService markdownRenderer, SessionService sessionService)
    {
        InitializeComponent();
        _localization = localization;
        _configService = configService;
        _pluginService = pluginService;
        _themeService = themeService;
        _viewModel = new MainWindowViewModel(fileService, localization, configService, themeService, recentFiles, pluginService, markdownRenderer, sessionService);
        DataContext = _viewModel;
        _viewModel.ApplyUiFontSettings = ApplyUiFont;
        ApplyUiFont();

        _appCommands = typeof(AppCommands)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(p => p.GetValue(null))
            .OfType<RoutedUICommand>()
            .ToList();

        themeService.ThemeChanged += (_, _) =>
        {
            ScintillaHost.ApplyThemeToAll();
            TerminalControl.ApplyThemeToAll();
        };

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
        _viewModel.ConfirmCancelledLoad = document => MessageBox.Show(this,
            string.Format(_localization.GetString("Loc.Dialog.LoadCancelled.Message"), document.Title),
            _localization.GetString("Loc.Dialog.Confirm.Title"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        _viewModel.ConfirmPartialSave = document => MessageBox.Show(this,
            string.Format(_localization.GetString("Loc.Dialog.PartialSave.Message"), document.Title),
            _localization.GetString("Loc.Dialog.Confirm.Title"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        _viewModel.ConfirmExternalReload = document => MessageBox.Show(this,
            string.Format(_localization.GetString("Loc.Dialog.ExternalReload.Message"), document.Title),
            _localization.GetString("Loc.Dialog.Confirm.Title"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        _viewModel.ConfirmExternalConflict = document => MessageBox.Show(this,
            string.Format(_localization.GetString("Loc.Dialog.ExternalConflict.Message"), document.Title),
            _localization.GetString("Loc.Dialog.Confirm.Title"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        _viewModel.NotifyExternalDeleted = document => MessageBox.Show(this,
            string.Format(_localization.GetString("Loc.Dialog.ExternalDeleted.Message"), document.Title),
            _localization.GetString("Loc.Dialog.Confirm.Title"),
            MessageBoxButton.OK, MessageBoxImage.Information);
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
                foreach (DocumentViewModel doc in e.OldItems)
                {
                    if (_tabContents.Remove(doc, out UIElement? content)) EditorHostGrid.Children.Remove(content);
                }
            }
            if (e.NewItems is not null)
            {
                foreach (DocumentViewModel doc in e.NewItems)
                {
                    UIElement content;
                    if (doc is MarkdownPreviewViewModel preview)
                    {
                        // 预览 Tab 是纯 WPF 内容，不受空域限制，直接放常驻 Grid
                        content = new MarkdownPreviewView { DataContext = preview };
                    }
                    else
                    {
                        content = doc.Editor;
                        doc.Editor.EditorKeyDown += OnEditorKeyDown;
                        doc.Editor.EditorRightClick += OnEditorRightClick;
                    }
                    content.Visibility = Visibility.Collapsed;
                    _tabContents[doc] = content;
                    EditorHostGrid.Children.Add(content);
                }
            }
            UpdateEditorVisibility();
        };
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.ActiveDocument)) UpdateEditorVisibility();
        };

        // 命令行路径不由 Loaded 消费：语言由插件提供，须等 App 完成插件扫描后再打开（InitializeAsync 驱动）
        // 热退出：进程退出不提示保存（需求 4.9），只兜底暂存脏文档并落盘会话
        Closing += (_, _) => _viewModel.OnWindowClosing();
    }

    /// <summary>启动时恢复上次会话（由 App 在窗口显示后调用，异步进行不拖慢冷启动）</summary>
    public Task RestoreSessionAsync() => _viewModel.RestoreSessionAsync();

    /// <summary>消费 App.PendingOpenPaths（命令行与次实例转发来的路径）</summary>
    public void OpenPendingPaths()
    {
        if (Application.Current is not App app || app.PendingOpenPaths.Count == 0) return;
        string[] paths = app.PendingOpenPaths.ToArray();
        app.PendingOpenPaths.Clear();
        _ = _viewModel.OpenPathsAsync(paths);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        string tooltipKey = maximized ? "Loc.TitleBar.Restore" : "Loc.TitleBar.Maximize";
        MaximizeButton.SetResourceReference(ToolTipProperty, tooltipKey);
        MaximizeButton.SetResourceReference(AutomationProperties.NameProperty, tooltipKey);
        // WindowChrome 最大化时窗口整体外扩 ResizeBorderThickness（6px），补偿使内容与正常窗口的屏幕内边距一致
        RootGrid.Margin = maximized ? new Thickness(6) : new Thickness(0);
    }

    private void UpdateEditorVisibility()
    {
        foreach (UIElement child in EditorHostGrid.Children)
        {
            child.Visibility = Visibility.Collapsed;
        }
        DocumentViewModel? active = _viewModel.ActiveDocument;
        if (active is null || !_tabContents.TryGetValue(active, out UIElement? content)) return;
        content.Visibility = Visibility.Visible;
        if (active is not MarkdownPreviewViewModel)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => active.Editor.FocusEditor());
        }
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

    private void IndentButton_Click(object sender, RoutedEventArgs e)
    {
        DocumentViewModel? document = _viewModel.ActiveDocument;
        if (document is null) return;

        var menu = new ContextMenu { Style = (Style)FindResource("PopupContextMenuStyle") };
        foreach ((string key, bool useTabs) in new[] { ("Loc.Indent.UseSpaces", false), ("Loc.Indent.UseTabs", true) })
        {
            var item = new MenuItem
            {
                Style = (Style)FindResource("PopupMenuItemStyle"),
                Header = _localization.GetString(key),
                IsChecked = document.IndentUseTabs == useTabs,
            };
            item.Click += (_, _) => document.SetIndentation(useTabs, document.IndentWidth);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        foreach (int width in new[] { 2, 3, 4, 8 })
        {
            var item = new MenuItem
            {
                Style = (Style)FindResource("PopupMenuItemStyle"),
                Header = width.ToString(),
                IsChecked = document.IndentWidth == width,
            };
            int selected = width;
            item.Click += (_, _) => document.SetIndentation(document.IndentUseTabs, selected);
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

    // ===== Tab 交互：中键关闭 / 右键菜单 / 拖拽排序 =====
    private System.Windows.Point _tabDragStart;
    private DocumentViewModel? _tabDragCandidate;

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null and not T)
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        return source as T;
    }

    private static DocumentViewModel? TabItemDocument(TabItem? tab) =>
        tab?.Content as DocumentViewModel ?? tab?.DataContext as DocumentViewModel;

    private void DocumentTabs_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        DocumentViewModel? doc = TabItemDocument(FindAncestor<TabItem>(e.OriginalSource as DependencyObject));
        if (doc is null) return;

        if (e.ChangedButton == MouseButton.Middle)
        {
            _viewModel.CloseDocumentCommand.Execute(doc);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            _viewModel.ActiveDocument = doc;
            ShowTabContextMenu(doc);
            e.Handled = true;
        }
    }

    private void ShowTabContextMenu(DocumentViewModel document)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("PopupContextMenuStyle") };

        void AddItem(string key, bool enabled, Action action)
        {
            var item = new MenuItem
            {
                Style = (Style)FindResource("PopupMenuItemStyle"),
                Header = _localization.GetString(key),
                IsEnabled = enabled,
            };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        AddItem("Loc.Tab.Close", true, () => _viewModel.CloseDocumentCommand.Execute(document));
        AddItem("Loc.Tab.CloseOthers", _viewModel.Documents.Count > 1,
            () => _ = _viewModel.CloseOthersAsync(document));
        AddItem("Loc.Tab.CloseToRight", _viewModel.Documents.IndexOf(document) < _viewModel.Documents.Count - 1,
            () => _ = _viewModel.CloseToRightAsync(document));
        menu.Items.Add(new Separator());
        AddItem("Loc.Tree.OpenInExplorer", document.FilePath is not null,
            () => RevealInExplorer(document.FilePath!));
        AddItem("Loc.Tab.CopyPath", document.FilePath is not null,
            () => CopyPath(document.FilePath!));

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void RevealInExplorer(string path)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            _viewModel.ShowError?.Invoke(
                string.Format(_localization.GetString("Loc.Error.Reveal.Message"), path, ex.Message),
                _localization.GetString("Loc.Error.Title"));
        }
    }

    private void CopyPath(string path)
    {
        try
        {
            Clipboard.SetText(path);
        }
        catch (Exception ex)
        {
            _viewModel.ShowError?.Invoke(ex.Message, _localization.GetString("Loc.Error.Title"));
        }
    }

    private void DocumentTabs_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 点在 Tab 头部关闭按钮上时不启动拖拽
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null) return;
        _tabDragCandidate = TabItemDocument(FindAncestor<TabItem>(e.OriginalSource as DependencyObject));
        _tabDragStart = e.GetPosition(null);
    }

    private void DocumentTabs_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _tabDragCandidate is null) return;
        System.Windows.Point pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _tabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DocumentViewModel doc = _tabDragCandidate;
        _tabDragCandidate = null;
        DragDrop.DoDragDrop(DocumentTabs, doc, DragDropEffects.Move);
    }

    private void DocumentTabs_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(DocumentViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void DocumentTabs_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(DocumentViewModel)) is not DocumentViewModel doc) return;
        DocumentViewModel? targetDoc = TabItemDocument(FindAncestor<TabItem>(e.OriginalSource as DependencyObject));
        if (targetDoc is null || ReferenceEquals(doc, targetDoc)) return;
        _viewModel.MoveDocument(doc, _viewModel.Documents.IndexOf(targetDoc));
        e.Handled = true;
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

    private void SaveLikeCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        // XAML 中按钮直接挂命令时，InitializeComponent 期间就会查询 CanExecute，此时 _viewModel 尚未赋值
        e.CanExecute = _viewModel is not null && _viewModel.CanSaveActive;
    }

    private void OpenMarkdownPreviewCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.OpenMarkdownPreview();
    }

    private void OpenMarkdownPreviewCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _viewModel is not null && _viewModel.CanPreviewActiveDocument;
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

    private void SelectNextOccurrenceCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.ActiveDocument?.Editor.SelectNextOccurrence();
    }

    private void FormatDocumentCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _ = _viewModel.FormatActiveDocumentAsync();
    }

    private void FormatDocumentCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _viewModel.CanFormatActiveDocument;
    }

    // 编辑器内右键（Scintilla 内置英文菜单已在 ScintillaHost 关闭，改用本地化 WPF 菜单）
    private void OnEditorRightClick(object? sender, EventArgs e)
    {
        if (sender is not ScintillaHost editor) return;

        var menu = new ContextMenu { Style = (Style)FindResource("PopupContextMenuStyle") };

        void AddItem(string key, bool enabled, Action action)
        {
            var item = new MenuItem
            {
                Style = (Style)FindResource("PopupMenuItemStyle"),
                Header = _localization.GetString(key),
                IsEnabled = enabled,
            };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        AddItem("Loc.Editor.Undo", editor.CanUndo, editor.Undo);
        AddItem("Loc.Editor.Redo", editor.CanRedo, editor.Redo);
        menu.Items.Add(new Separator());
        AddItem("Loc.Editor.Cut", !editor.IsReadOnly && editor.HasSelection, editor.Cut);
        AddItem("Loc.Editor.Copy", editor.HasSelection, editor.Copy);
        AddItem("Loc.Editor.Paste", editor.CanPaste, editor.Paste);
        menu.Items.Add(new Separator());
        AddItem("Loc.Editor.SelectAll", true, editor.SelectAll);
        menu.Items.Add(new Separator());
        AddItem("Loc.Editor.CollapseAll", editor.FoldingEnabled, editor.FoldAll);
        AddItem("Loc.Editor.ExpandAll", editor.FoldingEnabled, editor.UnfoldAll);
        menu.Items.Add(new Separator());
        AddItem("Loc.Menu.Edit.FormatDocument", _viewModel.CanFormatActiveDocument,
            () => _ = _viewModel.FormatActiveDocumentAsync());

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void NewFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.NewFileCommand.Execute(null);
    }

    private void OpenSettingsCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        // 模态对话框：每次打开新建实例，保存才生效，取消/Esc 放弃全部修改
        var window = new SettingsWindow(new SettingsViewModel(_configService, _localization, _viewModel, _pluginService, _themeService))
        {
            Owner = this,
            FontFamily = FontFamily,
            FontSize = FontSize,
        };
        window.ShowDialog();
    }

    private void ToggleTerminalCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.ToggleTerminal();
        if (_viewModel.IsTerminalVisible)
        {
            // 命令式设置 DataContext 与行高（不用绑定，规避 DataBind 优先级晚于 Render 的竞态）
            if (!ReferenceEquals(TerminalPanel.DataContext, _viewModel.Terminal))
                TerminalPanel.DataContext = _viewModel.Terminal;
            TerminalRow.Height = _viewModel.GetTerminalHeightToRestore();
            // 面板刚展开时尚未布局完成，焦点延后到输入优先级再设置
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => TerminalPanel.FocusTerminal());
        }
        else
        {
            TerminalRow.Height = new GridLength(0);
        }
    }

    private void NewTerminalTabCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        // 面板隐藏时先展开；展开过程中若已自动补首个 tab（EnsureTab）则不重复新建
        bool hadTabs = _viewModel.Terminal is { Tabs.Count: > 0 };
        if (!_viewModel.IsTerminalVisible) ToggleTerminalCommand_Executed(sender, e);
        if (hadTabs) _viewModel.Terminal?.NewTab();
    }

    private void TerminalSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        => _viewModel.RememberTerminalHeight(TerminalRow.Height);

    private void ApplyUiFont()
    {
        AppSettings settings = _configService.Settings;
        if (string.IsNullOrEmpty(settings.UiFontFamily)) ClearValue(FontFamilyProperty);
        else FontFamily = new System.Windows.Media.FontFamily(settings.UiFontFamily);
        FontSize = settings.UiFontSize;
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mnemosyne.Services;
using Mnemosyne.ViewModels;

namespace Mnemosyne.Views;

public partial class FilePanelView : UserControl
{
    private readonly LocalizationService _localization;

    public FilePanelView() : this(((App)Application.Current).LocalizationService)
    {
    }

    public FilePanelView(LocalizationService localization)
    {
        InitializeComponent();
        _localization = localization;
    }

    private FileTreeViewModel? ViewModel => DataContext as FileTreeViewModel;

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileTreeNodeViewModel node) ViewModel?.ActivateNode(node);
    }

    private void TreeItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        ((TreeViewItem)sender).IsSelected = true;
    }

    // 节点右键菜单（e.Handled 阻止冒泡到空白区域菜单）
    private void TreeItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if (ViewModel is not { } vm || ((TreeViewItem)sender).DataContext is not FileTreeNodeViewModel node) return;
        if (node.IsDummy || node.IsPlaceholder) return;

        ContextMenu menu = BuildMenu();
        AddItem(menu, "Loc.Tree.NewFile", () => vm.BeginCreateFileCommand.Execute(node));
        AddItem(menu, "Loc.Tree.NewFolder", () => vm.BeginCreateFolderCommand.Execute(node));
        if (!node.IsRoot)
        {
            menu.Items.Add(new Separator());
            AddItem(menu, "Loc.Tree.Rename", () => vm.BeginRenameCommand.Execute(node));
            AddItem(menu, "Loc.Tree.Delete", () => vm.DeleteCommand.Execute(node));
        }
        menu.Items.Add(new Separator());
        AddItem(menu, "Loc.Tree.OpenInExplorer", () => vm.RevealInExplorerCommand.Execute(node));
        if (node.IsRoot)
        {
            menu.Items.Add(new Separator());
            AddItem(menu, "Loc.SideBar.Files.CloseFolder", () => vm.CloseFolderCommand.Execute(null));
        }
        OpenMenu(menu, (TreeViewItem)sender);
    }

    // 空白区域右键：作用于根目录
    private void FileTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ViewModel is not { } vm || vm.RootNode is not { } root) return;

        ContextMenu menu = BuildMenu();
        AddItem(menu, "Loc.Tree.NewFile", () => vm.BeginCreateFileCommand.Execute(root));
        AddItem(menu, "Loc.Tree.NewFolder", () => vm.BeginCreateFolderCommand.Execute(root));
        menu.Items.Add(new Separator());
        AddItem(menu, "Loc.Tree.OpenInExplorer", () => vm.RevealInExplorerCommand.Execute(root));
        menu.Items.Add(new Separator());
        AddItem(menu, "Loc.SideBar.Files.CloseFolder", () => vm.CloseFolderCommand.Execute(null));
        OpenMenu(menu, FileTree);
    }

    private ContextMenu BuildMenu() =>
        new() { Style = (Style)FindResource("PopupContextMenuStyle") };

    private void AddItem(ContextMenu menu, string headerKey, Action action)
    {
        var item = new MenuItem
        {
            Style = (Style)FindResource("PopupMenuItemStyle"),
            Header = _localization.GetString(headerKey),
        };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static void OpenMenu(ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    // ---- 内联编辑框 ----

    private void EditBox_Loaded(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        if (box.DataContext is not FileTreeNodeViewModel { IsEditing: true } node) return;
        box.Focus();
        Keyboard.Focus(box);
        if (node.IsPlaceholder)
        {
            box.SelectAll();
        }
        else
        {
            // 重命名文件时预选主文件名（不含扩展名），贴近主流编辑器行为
            string name = box.Text;
            int length = node.IsDirectory ? name.Length : Path.GetFileNameWithoutExtension(name) is { Length: > 0 } stem ? stem.Length : name.Length;
            box.SelectionStart = 0;
            box.SelectionLength = length;
        }
    }

    private void EditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (((TextBox)sender).DataContext is not FileTreeNodeViewModel node || ViewModel is not { } vm) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            vm.CommitEdit(node);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelEdit(node);
        }
    }

    private void EditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (((TextBox)sender).DataContext is FileTreeNodeViewModel { IsEditing: true } node)
        {
            ViewModel?.CommitEdit(node);
        }
    }
}

using System.Windows.Controls;
using System.Windows.Input;
using Mnemosyne.ViewModels;

namespace Mnemosyne.Views;

public partial class SearchPanelView : UserControl
{
    public SearchPanelView()
    {
        InitializeComponent();
    }

    /// <summary>Ctrl+Shift+F 打开面板后把焦点给关键字输入框</summary>
    public void FocusSearchBox() => SearchBox.Focus();

    private void Box_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SearchPanelViewModel viewModel)
        {
            viewModel.SearchNowCommand.Execute(null);
            e.Handled = true;
        }
    }

    // 双击匹配行跳转；双击文件分组行走 TreeView 默认的展开/收起
    private void ResultItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: SearchResultMatchViewModel match })
        {
            match.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }
}

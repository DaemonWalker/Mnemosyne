using System.Windows.Controls;
using System.Windows.Input;
using Mnemosyne.ViewModels;

namespace Mnemosyne.Views;

public partial class FindBarView : UserControl
{
    public FindBarView()
    {
        InitializeComponent();
    }

    private FindBarViewModel ViewModel => (FindBarViewModel)DataContext;

    /// <summary>Ctrl+F/Ctrl+H 打开后由窗口调用：焦点进搜索框并全选已有关键字</summary>
    public void FocusSearchBox()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // Enter=下一个、Shift+Enter=上一个（替换框内 Enter=替换当前）、Esc=关闭并聚焦编辑器
    private void FindBar_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ViewModel.CloseCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter || e.OriginalSource is not TextBox) return;

        if (ReferenceEquals(e.OriginalSource, ReplaceBox))
        {
            ViewModel.ReplaceCurrentCommand.Execute(null);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ViewModel.FindPreviousCommand.Execute(null);
        }
        else
        {
            ViewModel.FindNextCommand.Execute(null);
        }
        e.Handled = true;
    }
}

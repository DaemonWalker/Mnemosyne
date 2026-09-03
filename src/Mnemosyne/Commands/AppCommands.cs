using System.Windows.Input;

namespace Mnemosyne.Commands;

public static class AppCommands
{
    public static RoutedUICommand OpenFile { get; } = Create(nameof(OpenFile), Key.O);

    public static RoutedUICommand OpenFolder { get; } = Create(nameof(OpenFolder), Key.O, ModifierKeys.Shift);

    public static RoutedUICommand Save { get; } = Create(nameof(Save), Key.S);

    public static RoutedUICommand SaveAs { get; } = Create(nameof(SaveAs), Key.S, ModifierKeys.Shift);

    public static RoutedUICommand Find { get; } = Create(nameof(Find), Key.F);

    public static RoutedUICommand Replace { get; } = Create(nameof(Replace), Key.H);

    public static RoutedUICommand SearchInFolder { get; } = Create(nameof(SearchInFolder), Key.F, ModifierKeys.Shift);

    public static RoutedUICommand CloseTab { get; } = Create(nameof(CloseTab), Key.W);

    public static RoutedUICommand SelectNextOccurrence { get; } = Create(nameof(SelectNextOccurrence), Key.D);

    public static RoutedUICommand OpenSettings { get; } = Create(nameof(OpenSettings), Key.OemComma);

    // 手势直接挂在命令上：菜单自动显示快捷键文本，窗口注册 CommandBinding 后即全局生效
    private static RoutedUICommand Create(string name, Key key, ModifierKeys extraModifiers = ModifierKeys.None)
    {
        return new RoutedUICommand(name, name, typeof(AppCommands),
            new InputGestureCollection { new KeyGesture(key, ModifierKeys.Control | extraModifiers) });
    }
}

using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemosyne.Models;

namespace Mnemosyne.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private GridLength _lastSidebarWidth = new(260);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarVisible))]
    [NotifyPropertyChangedFor(nameof(IsFilePanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsSearchPanelVisible))]
    private ActivityPanel? _activePanel = Models.ActivityPanel.Files;

    // 与侧边栏列宽双向绑定：拖动分隔条改这里，收起/展开时由 ViewModel 置 0 或恢复
    [ObservableProperty]
    private GridLength _sidebarWidth = new(260);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasOpenDocuments;

    public bool IsSidebarVisible => ActivePanel is not null;

    public bool IsFilePanelVisible => ActivePanel == Models.ActivityPanel.Files;

    public bool IsSearchPanelVisible => ActivePanel == Models.ActivityPanel.Search;

    public bool ShowEmptyState => !HasOpenDocuments;

    partial void OnActivePanelChanged(ActivityPanel? value)
    {
        SidebarWidth = value is null ? new GridLength(0) : _lastSidebarWidth;
    }

    partial void OnSidebarWidthChanged(GridLength value)
    {
        if (value.Value > 0) _lastSidebarWidth = value;
    }

    [RelayCommand]
    private void ToggleActivity(ActivityPanel panel)
    {
        ActivePanel = ActivePanel == panel ? null : panel;
    }

    [RelayCommand]
    private void ShowSearchPanel()
    {
        ActivePanel = Models.ActivityPanel.Search;
    }
}

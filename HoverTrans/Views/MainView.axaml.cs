using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HoverTrans.Views;

/// <summary>The app's frame: the navigation, the top bar and the page that is showing.</summary>
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void OnToggleSidebar(object? sender, RoutedEventArgs e) => Nav.ToggleCollapsed();
}

using Avalonia;
using Avalonia.Controls;
using HoverTrans.ViewModels;

namespace HoverTrans.Views.Pages;

/// <summary>The history page; it re-reads the file every time it is shown, so a translation just made is there.</summary>
public partial class HistoryPage : UserControl
{
    public HistoryPage()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as HistoryPageViewModel)?.Refresh();
    }
}

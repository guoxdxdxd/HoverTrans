using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HoverTrans.ViewModels;
using TKCode.Arc;

namespace HoverTrans.Views.Pages;

/// <summary>
/// The settings page: it re-reads the model file state every time it is shown, opens at the top, and stays still while
/// one of its segmented controls changes.
/// </summary>
public partial class SettingsPage : UserControl
{
    private readonly HashSet<SegmentedControl> _hookedSegments = [];
    private bool _openedAtTop;

    public SettingsPage()
    {
        InitializeComponent();
        LayoutUpdated += OnLayoutUpdated;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as SettingsPageViewModel)?.Refresh();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // The rows of a settings group are realized by an items presenter, so the segmented controls only exist once
        // the first layout has run.
        HookSegmentedControls();

        if (_openedAtTop)
        {
            return;
        }

        _openedAtTop = true;
        // The first selection of a segment asks the page to scroll to it; open on the first group instead.
        Dispatcher.UIThread.Post(() => PageScroll.Offset = default, DispatcherPriority.Background);
    }

    /// <summary>
    /// A segmented control asks for the newly selected segment to be brought into view, and that request leaves the
    /// control and scrolls this page: choosing a theme jumped the page to the appearance group. Swallow only requests
    /// for a segment, so keyboard focus on the control itself still scrolls it into view.
    /// </summary>
    private void HookSegmentedControls()
    {
        foreach (var segmented in this.GetVisualDescendants().OfType<SegmentedControl>())
        {
            if (!_hookedSegments.Add(segmented))
            {
                continue;
            }

            segmented.AddHandler(
                Control.RequestBringIntoViewEvent,
                OnSegmentBringIntoView,
                RoutingStrategies.Bubble,
                handledEventsToo: true);
        }
    }

    private static void OnSegmentBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is Visual target && target.GetVisualAncestors().OfType<SegmentedControl>().Any())
        {
            e.Handled = true;
        }
    }
}

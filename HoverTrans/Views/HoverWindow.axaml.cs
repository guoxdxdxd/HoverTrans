using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using HoverTrans.Services;
using HoverTrans.ViewModels;
using TKCode.Arc;
using TKCode.Arc.Motion;

namespace HoverTrans.Views;

/// <summary>Topmost, non-activating translation popup shown next to the cursor.</summary>
public partial class HoverWindow : Window
{
    public HoverWindow()
    {
        InitializeComponent();
        DataContext = new HoverViewModel();

        Root.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) },
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(220),
                Easing = new CubicEaseOut()
            }
        ];
    }

    /// <summary>Raised when the popup's "open main window" button is pressed.</summary>
    public event EventHandler? OpenMainRequested;

    public void Apply(HoverSnapshot snapshot)
    {
        if (DataContext is HoverViewModel viewModel)
        {
            viewModel.Apply(snapshot);
        }
    }

    /// <summary>The popup rises into place each time it appears; with motion reduced or off it simply appears.</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (ArcTheme.State.MotionLevel != ArcMotionLevel.Full)
        {
            Root.Opacity = 1;
            Root.RenderTransform = null;
            return;
        }

        Root.Opacity = 0;
        Root.RenderTransform = TransformOperations.Parse("translateY(8px)");
        Dispatcher.UIThread.Post(
            () =>
            {
                Root.Opacity = 1;
                Root.RenderTransform = TransformOperations.Parse("translateY(0px)");
            },
            DispatcherPriority.Background);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Hide();

    private void OnOpenMainClick(object? sender, RoutedEventArgs e)
    {
        Hide();
        OpenMainRequested?.Invoke(this, EventArgs.Empty);
    }
}

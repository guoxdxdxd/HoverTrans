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
using System.Runtime.InteropServices;
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
        KeepFocusWhereItIs();

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

    /// <summary>
    /// A topmost popup that appears under the pointer must never take the keyboard from the window the person is
    /// working in: without WS_EX_NOACTIVATE, showing it activates it, and the next keystrokes (typing into the main
    /// window, Ctrl+V, Ctrl+Enter) land in the popup instead, where nothing happens. Avalonia's ShowActivated alone
    /// does not leave that bit on the window, so it is set here once the native window exists. Clicks still reach the
    /// popup's own buttons; only activation is refused.
    /// </summary>
    private void KeepFocusWhereItIs()
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle()?.Handle is not { } handle)
        {
            return;
        }

        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetExtendedStyle(handle);
        if ((style & NoActivate) != 0)
        {
            return;
        }

        SetExtendedStyle(handle, style | NoActivate);
    }

    private const long NoActivate = 0x08000000; // WS_EX_NOACTIVATE
    private const int ExtendedStyleIndex = -20; // GWL_EXSTYLE

    private static long GetExtendedStyle(IntPtr handle) =>
        IntPtr.Size == 8 ? GetWindowLongPtrW(handle, ExtendedStyleIndex).ToInt64() : GetWindowLongW(handle, ExtendedStyleIndex);

    private static void SetExtendedStyle(IntPtr handle, long style)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtrW(handle, ExtendedStyleIndex, new IntPtr(style));
        }
        else
        {
            SetWindowLongW(handle, ExtendedStyleIndex, (int)style);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLongW(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLongW(IntPtr handle, int index, int style);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr handle, int index, IntPtr style);

    private void OnOpenMainClick(object? sender, RoutedEventArgs e)
    {
        Hide();
        OpenMainRequested?.Invoke(this, EventArgs.Empty);
    }
}

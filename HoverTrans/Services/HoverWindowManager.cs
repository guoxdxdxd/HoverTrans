using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HoverTrans.ViewModels;
using HoverTrans.Views;

namespace HoverTrans.Services;

/// <summary>Shows <see cref="HoverWindow"/> for hover translations and hides it after a quiet period.</summary>
public sealed class HoverWindowManager : IDisposable
{
    private static readonly TimeSpan HideAfter = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan HideAfterSlowStart = TimeSpan.FromSeconds(60);

    private readonly IHoverService _hover;
    private HoverWindow? _window;
    private DispatcherTimer? _hideTimer;
    private bool _initialized;
    private bool _disposed;

    public HoverWindowManager(IHoverService hover)
    {
        _hover = hover;
    }

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _hover.SnapshotChanged += OnSnapshotChanged;

        // Coming back to the app means the popup has done its job: leaving it on top would cover the composer and
        // swallow the click and the keystrokes meant for it.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } main)
        {
            main.Activated += OnMainWindowActivated;
        }
    }

    private void OnMainWindowActivated(object? sender, EventArgs e) => _window?.Hide();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hover.SnapshotChanged -= OnSnapshotChanged;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } main)
        {
            main.Activated -= OnMainWindowActivated;
        }

        _hideTimer?.Stop();
        _window?.Close();
        _window = null;
    }

    private void OnSnapshotChanged(object? sender, HoverSnapshot snapshot) =>
        Dispatcher.UIThread.Post(() => Show(snapshot));

    private void Show(HoverSnapshot snapshot)
    {
        if (_disposed || string.IsNullOrWhiteSpace(snapshot.SourceText))
        {
            return;
        }

        if (_window is null)
        {
            _window = new HoverWindow();
            _window.OpenMainRequested += (_, _) => OpenMainWindow();
        }

        _window.Apply(snapshot);

        var screen = _window.Screens.ScreenFromPoint(new PixelPoint(snapshot.X, snapshot.Y))
                     ?? _window.Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            var area = screen.WorkingArea;
            var x = Math.Clamp(snapshot.X + 18, area.X + 8, area.Right - 452);
            var y = Math.Clamp(snapshot.Y + 22, area.Y + 8, area.Bottom - 252);
            _window.Position = new PixelPoint(x, y);
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        _hideTimer ??= CreateHideTimer();
        _hideTimer.Stop();
        // The first translation can spend 5-20 seconds loading the model; keep the popup open for that.
        _hideTimer.Interval = snapshot.IsTranslating ? HideAfterSlowStart : HideAfter;
        _hideTimer.Start();
    }

    private DispatcherTimer CreateHideTimer()
    {
        var timer = new DispatcherTimer { Interval = HideAfter };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            // The pointer resting on the popup means the person is reading, selecting or copying: stay open.
            if (_window is { IsPointerOver: true })
            {
                timer.Start();
                return;
            }

            _window?.Hide();
        };
        return timer;
    }

    /// <summary>Brings the main window forward from the popup's "open main window" button.</summary>
    private static void OpenMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } main)
        {
            return;
        }

        if (!main.IsVisible)
        {
            main.Show();
        }

        if (main.WindowState == WindowState.Minimized)
        {
            main.WindowState = WindowState.Normal;
        }

        main.Activate();
    }
}

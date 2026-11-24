using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HoverTrans.Core.Platform;
using HoverTrans.Core.Translation;
using HoverTrans.Core.Updates;
using HoverTrans.Services;
using HoverTrans.Settings;

namespace HoverTrans.ViewModels;

/// <summary>
/// What every page shows about the app itself rather than about one translation: whether hover translation is on,
/// whether the local model is running, and the version. It owns the hover service, so the top bar's switch and the
/// settings page's switch write the same value instead of two copies that drift apart.
/// </summary>
public sealed partial class AppState : ObservableObject
{
    private readonly IHoverService _hover;
    private readonly ITranslationService _translation;
    private readonly DispatcherTimer _statusTimer;
    private bool _initialized;
    private bool _syncing;

    [ObservableProperty]
    private bool _hoverEnabled;

    [ObservableProperty]
    private string _modelStatus = string.Empty;

    [ObservableProperty]
    private bool _isModelRunning;

    public AppState(IHoverService hover, ITranslationService translation)
    {
        _hover = hover;
        _translation = translation;
        IsHoverSupported = hover.IsSupported;
        HoverHint = hover.IsSupported
            ? "把鼠标停在外文上约半秒，就会在旁边弹出译文。"
            : hover.UnsupportedMessage;
        Version = AppVersion.Display;
        _modelStatus = translation.StatusText;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => RefreshModelStatus();
    }

    /// <summary>Whether this build can read text under the mouse (Windows only for now).</summary>
    public bool IsHoverSupported { get; }

    /// <summary>What hover translation does, or why it cannot be used on this platform.</summary>
    public string HoverHint { get; }

    /// <summary>Short model state for the top bar: running, or started on demand.</summary>
    public string ModelBadgeText => IsModelRunning ? "模型运行中" : "模型按需启动";

    /// <summary>Whether the model is idle (the inverse of <see cref="IsModelRunning"/>, for a binding).</summary>
    public bool IsModelStopped => !IsModelRunning;

    /// <summary>The app version, for the About card.</summary>
    public string Version { get; }

    /// <summary>Where the model, runtime and log files live.</summary>
    public string DataDirectory => AppPaths.Root;

    /// <summary>Starts the live model status and turns hover translation back on if it was on last time.</summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _statusTimer.Start();
        RefreshModelStatus();

        _syncing = true;
        HoverEnabled = AppSettings.Current.HoverEnabled && IsHoverSupported;
        _syncing = false;

        if (HoverEnabled)
        {
            _hover.Start();
        }
    }

    /// <summary>Reads the model state now, for a page that has just saved new paths or stopped the model.</summary>
    public void RefreshModelStatus()
    {
        IsModelRunning = _translation.IsModelRunning;
        ModelStatus = _translation.StatusText;
    }

    partial void OnIsModelRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(ModelBadgeText));
        OnPropertyChanged(nameof(IsModelStopped));
    }

    partial void OnHoverEnabledChanged(bool value)    {
        if (_syncing)
        {
            return;
        }

        var settings = AppSettings.Current;
        settings.HoverEnabled = value;
        settings.Save();

        if (!IsHoverSupported)
        {
            return;
        }

        if (value)
        {
            _hover.Start();
        }
        else
        {
            _ = _hover.StopAsync();
        }
    }
}

using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoverTrans.Core.Model;
using HoverTrans.Core.Platform;
using HoverTrans.Core.Translation;
using HoverTrans.Core.Updates;
using HoverTrans.Settings;
using TKCode.Arc;
using TKCode.Arc.Motion;

namespace HoverTrans.ViewModels;

/// <summary>
/// The settings page. Appearance, accent and motion apply the moment they change (the whole window restyles); the
/// inference backend is saved and takes effect the next time the app starts.
/// </summary>
public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly ITranslationService _translation;
    private readonly IUpdateChecker _updates;
    private Uri? _updateUrl;
    private bool _syncing;

    [ObservableProperty]
    private string? _backend;

    [ObservableProperty]
    private double _idleUnloadMinutes = 10;

    [ObservableProperty]
    private double _hoverDelay = 550;

    [ObservableProperty]
    private string? _appearance;

    [ObservableProperty]
    private string? _accent;

    [ObservableProperty]
    private string? _motionLevel;

    [ObservableProperty]
    private string _saveStatus = string.Empty;

    [ObservableProperty]
    private bool _isSaveError;

    [ObservableProperty]
    private string _updateHint = "点「检查更新」到 GitHub 看有没有新版本。";

    [ObservableProperty]
    private bool _isUpdateError;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdateCommand))]
    private bool _isCheckingUpdate;

    public SettingsPageViewModel(ITranslationService translation, AppState state, IUpdateChecker updates)
    {
        _translation = translation;
        _updates = updates;
        State = state;

        var settings = AppSettings.Current;
        _backend = HunyuanEngineOptions.Normalize(settings.Backend);
        _idleUnloadMinutes = Math.Clamp(settings.IdleUnloadMinutes, 0, 120);
        _hoverDelay = settings.HoverDebounceMilliseconds;
        _motionLevel = ((ArcMotionLevel)Math.Clamp(settings.MotionLevel, 0, 2)).ToString();
        ShowTheme(ArcTheme.State);
        ArcTheme.Manager.StateChanged += (_, e) => ShowTheme(e.NewState);
        State.PropertyChanged += OnStateChanged;
    }

    /// <summary>Hover state, model status and version, shared with the shell.</summary>
    public AppState State { get; }

    /// <summary>The accent presets, in the order the select shows them.</summary>
    public IReadOnlyList<string> Accents { get; } =
        ["Neutral", "Violet", "Blue", "Green", "Amber", "Orange", "Coral", "Rose"];

    /// <summary>Where the settings, history and log files live.</summary>
    public string DataDirectory => AppPaths.Root;

    /// <summary>What the model is and where it comes from; the settings page shows it above the status.</summary>
    public string ModelInfo =>
        "Hy-MT3 1.8B（Q4_K_M，约 1.3 GB）随应用分发，在本进程内推理：不启动外部服务、不占端口、不联网。";

    /// <summary>「0 分钟」 reads better as 「常驻」 than as a zero-length timeout.</summary>
    public string IdleUnloadText => IdleUnloadMinutes < 0.5
        ? "常驻，不释放"
        : $"闲置 {IdleUnloadMinutes:0} 分钟后释放内存";

    /// <summary>Whether the release button can do anything.</summary>
    public bool CanUnloadModel => State.IsModelLoaded;

    /// <summary>Whether the update button can run; greyed out while a check is in flight.</summary>
    public bool CanCheckUpdate => !IsCheckingUpdate;

    /// <summary>The version this build reports, so "已是最新" has a number next to it.</summary>
    public string CurrentVersion => AppVersion.Display;

    /// <summary>Reads the model state again; called when the page is shown.</summary>
    public void Refresh()
    {
        State.RefreshModelStatus();
        OnPropertyChanged(nameof(ModelInfo));
        OnPropertyChanged(nameof(CanUnloadModel));
    }

    /// <summary>Saves the settings; the idle timeout applies at once, the backend at the next start.</summary>
    [RelayCommand]
    private void Save()
    {
        var settings = AppSettings.Current;
        settings.Backend = HunyuanEngineOptions.Normalize(Backend);
        settings.IdleUnloadMinutes = (int)Math.Clamp(Math.Round(IdleUnloadMinutes), 0, 120);
        settings.Save();

        var idle = settings.IdleUnloadMinutes > 0
            ? TimeSpan.FromMinutes(settings.IdleUnloadMinutes)
            : (TimeSpan?)null;
        _translation.SetIdleUnload(idle);
        State.RefreshModelStatus();

        IsSaveError = false;
        SaveStatus = "已保存。闲置释放立即生效；推理后端在下次启动应用后生效。";
    }

    /// <summary>Releases the model's memory now; the next translation loads it again.</summary>
    [RelayCommand(CanExecute = nameof(CanUnloadModel))]
    private async Task UnloadModelAsync()
    {
        await _translation.UnloadModelAsync();
        State.RefreshModelStatus();
        OnPropertyChanged(nameof(CanUnloadModel));
        IsSaveError = false;
        SaveStatus = "已释放模型内存，下次翻译会重新加载（约 2 秒）。";
    }

    /// <summary>Opens the folder with the log file.</summary>
    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            Process.Start(new ProcessStartInfo { FileName = AppPaths.Root, UseShellExecute = true });
        }
        catch (Exception)
        {
            IsSaveError = true;
            SaveStatus = "打不开数据目录：" + AppPaths.Root;
        }
    }

    /// <summary>Asks GitHub for the newest release and says what it found. Quietly does nothing twice at once.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckUpdate))]
    private async Task CheckUpdateAsync()
    {
        IsCheckingUpdate = true;
        IsUpdateError = false;
        HasUpdate = false;
        _updateUrl = null;
        UpdateHint = "正在检查…";

        try
        {
            ShowUpdate(await _updates.CheckAsync());
        }
        catch (Exception exception)
        {
            // The checker answers failures itself; this is the last resort (a broken DI, an unexpected throw).
            IsUpdateError = true;
            UpdateHint = "检查失败：" + exception.Message;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    /// <summary>Opens the release page in the browser, or the installer attached to it when there is one.</summary>
    [RelayCommand]
    private void OpenUpdate()
    {
        if (_updateUrl is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = _updateUrl.ToString(), UseShellExecute = true });
        }
        catch (Exception)
        {
            IsUpdateError = true;
            UpdateHint = "打不开浏览器：" + _updateUrl;
        }
    }

    private void ShowUpdate(UpdateCheck check)
    {
        HasUpdate = check.HasUpdate;
        _updateUrl = check.Release?.Download;
        IsUpdateError = check.Status == UpdateStatus.Failed;

        UpdateHint = check.Status switch
        {
            UpdateStatus.UpdateAvailable => $"有新版本 v{check.Release!.Version}（当前 v{check.CurrentVersion}）。",
            UpdateStatus.UpToDate => $"已是最新版本（v{check.CurrentVersion}）。",
            UpdateStatus.NoReleases => "GitHub 上还没有发布版本。",
            _ => check.Failure switch
            {
                UpdateFailure.Network => "连不上 GitHub，检查网络后再试。",
                UpdateFailure.RateLimited => "GitHub 限流了，过一会儿再试。",
                UpdateFailure.HttpError => $"GitHub 返回错误（{check.Detail}）。",
                _ => "GitHub 返回的内容看不懂，稍后再试。"
            }
        };
    }

    partial void OnAppearanceChanged(string? value)
    {
        if (_syncing || !TryParseAppearance(value, out var appearance) || appearance == ArcTheme.State.Appearance)
        {
            return;
        }

        ArcTheme.Apply(theme => theme.Appearance(appearance));
    }

    partial void OnAccentChanged(string? value)
    {
        if (_syncing || !Enum.TryParse<ArcAccentPreset>(value, out var accent))
        {
            return;
        }

        ArcTheme.Apply(theme => theme.Accent(accent));
    }

    partial void OnMotionLevelChanged(string? value)
    {
        if (_syncing || !Enum.TryParse<ArcMotionLevel>(value, out var level))
        {
            return;
        }

        ArcTheme.Apply(theme => theme.MotionLevel(level));
        AppSettings.Current.MotionLevel = (int)level;
        AppSettings.Current.Save();
    }

    partial void OnBackendChanged(string? value) => SaveStatus = string.Empty;

    partial void OnIdleUnloadMinutesChanged(double value)
    {
        SaveStatus = string.Empty;
        OnPropertyChanged(nameof(IdleUnloadText));
    }

    partial void OnHoverDelayChanged(double value)
    {
        var settings = AppSettings.Current;
        var milliseconds = (int)Math.Clamp(Math.Round(value), 150, 2000);
        if (settings.HoverDebounceMilliseconds == milliseconds)
        {
            return;
        }

        settings.HoverDebounceMilliseconds = milliseconds;
        settings.Save();
    }

    /// <summary>Keeps the release button in step with the model the top bar shows.</summary>
    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.IsModelLoaded))
        {
            OnPropertyChanged(nameof(CanUnloadModel));
        }
    }

    private void ShowTheme(ArcThemeState state)    {
        _syncing = true;
        Appearance = state.Appearance.ToString();
        Accent = AccentName(state.Accent);
        _syncing = false;
    }

    /// <summary>The preset whose seed color is showing, or null for a custom accent.</summary>
    private static string? AccentName(Color? seed)
    {
        foreach (var preset in Enum.GetValues<ArcAccentPreset>())
        {
            if (seed == ArcPalette.GetSeed(preset))
            {
                return preset.ToString();
            }
        }

        return null;
    }

    private static bool TryParseAppearance(string? value, out ArcAppearance appearance)
    {
        if (string.Equals(value, nameof(ArcAppearance.System), StringComparison.OrdinalIgnoreCase))
        {
            appearance = ArcAppearance.System;
            return true;
        }

        return Enum.TryParse(value, out appearance);
    }
}

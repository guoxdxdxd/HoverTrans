using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoverTrans.Core.Platform;
using HoverTrans.Core.Translation;
using HoverTrans.Settings;
using TKCode.Arc;
using TKCode.Arc.Motion;

namespace HoverTrans.ViewModels;

/// <summary>
/// The settings page. Appearance, accent and motion apply the moment they change (the whole window restyles); the
/// model paths, port and idle timeout are saved together because they take effect when the app starts again.
/// </summary>
public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly ITranslationService _translation;
    private bool _syncing;

    [ObservableProperty]
    private string _serverPath = string.Empty;

    [ObservableProperty]
    private string _modelPath = string.Empty;

    [ObservableProperty]
    private string _portText = string.Empty;

    [ObservableProperty]
    private double _idleMinutes = 10;

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
    private string _modelStateText = string.Empty;

    [ObservableProperty]
    private bool _modelFileExists;

    [ObservableProperty]
    private string _serverStateText = string.Empty;

    [ObservableProperty]
    private bool _serverFileExists;

    public SettingsPageViewModel(ITranslationService translation, AppState state)
    {
        _translation = translation;
        State = state;

        var settings = AppSettings.Current;
        _serverPath = settings.ServerPath;
        _modelPath = settings.ModelPath;
        _portText = settings.Port.ToString();
        _idleMinutes = settings.IdleMinutes;
        _hoverDelay = settings.HoverDebounceMilliseconds;
        _motionLevel = ((ArcMotionLevel)Math.Clamp(settings.MotionLevel, 0, 2)).ToString();
        ShowTheme(ArcTheme.State);
        ArcTheme.Manager.StateChanged += (_, e) => ShowTheme(e.NewState);
        State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.IsModelRunning))
            {
                OnPropertyChanged(nameof(CanStopModel));
            }
        };
        RefreshFileState();
    }

    /// <summary>Hover state, model status and version, shared with the shell.</summary>
    public AppState State { get; }

    /// <summary>The accent presets, in the order the select shows them.</summary>
    public IReadOnlyList<string> Accents { get; } =
        ["Neutral", "Violet", "Blue", "Green", "Amber", "Orange", "Coral", "Rose"];

    /// <summary>Where the model, runtime and log files live.</summary>
    public string DataDirectory => AppPaths.Root;

    /// <summary>Whether the stop button can do anything.</summary>
    public bool CanStopModel => State.IsModelRunning;

    /// <summary>Reads the model file state and the model process again; called when the page is shown.</summary>
    public void Refresh()
    {
        RefreshFileState();
        State.RefreshModelStatus();
        OnPropertyChanged(nameof(CanStopModel));
    }

    /// <summary>Saves the model settings, then says what happened.</summary>
    [RelayCommand]
    private void Save()
    {
        var settings = AppSettings.Current;

        if (!int.TryParse(PortText, out var port) || port is < 1024 or > 65535)
        {
            IsSaveError = true;
            SaveStatus = "端口要在 1024 到 65535 之间，换一个再保存。";
            return;
        }

        settings.ServerPath = ServerPath.Trim();
        settings.ModelPath = ModelPath.Trim();
        settings.Port = port;
        settings.IdleMinutes = Math.Clamp((int)Math.Round(IdleMinutes), 1, 120);
        settings.Save();

        IsSaveError = false;
        SaveStatus = "已保存。模型路径、端口和闲置时长在下次启动应用时生效。";
        RefreshFileState();
    }

    /// <summary>Stops the model now; the next translation starts it again.</summary>
    [RelayCommand]
    private async Task StopModelAsync()
    {
        await _translation.StopModelAsync();
        State.RefreshModelStatus();
        OnPropertyChanged(nameof(CanStopModel));
        IsSaveError = false;
        SaveStatus = "模型已停止，下次翻译会自动重新启动。";
    }

    /// <summary>Opens the folder with the downloaded model and the llama-server log.</summary>
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

    partial void OnPortTextChanged(string value) => SaveStatus = string.Empty;

    partial void OnIdleMinutesChanged(double value) => SaveStatus = string.Empty;

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

    private void ShowTheme(ArcThemeState state)
    {
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

    private void RefreshFileState()
    {
        ModelFileExists = File.Exists(AppSettings.Current.ModelPath);
        ModelStateText = ModelFileExists
            ? "模型文件已就绪"
            : "还没有模型文件：运行 scripts/setup-model.ps1 下载 Hy-MT2 1.8B。";

        ServerFileExists = File.Exists(AppSettings.Current.ServerPath);
        ServerStateText = ServerFileExists
            ? "llama-server 已就绪"
            : "还没有 llama-server：同一脚本会一起下载。";
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

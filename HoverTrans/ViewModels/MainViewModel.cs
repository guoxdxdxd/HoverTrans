using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using HoverTrans.Navigation;
using HoverTrans.Views.Pages;

namespace HoverTrans.ViewModels;

/// <summary>
/// The shell: which page is showing, what the top bar says about it, and the app-wide state (model, hover) every page
/// reads. Pages are created the first time they are shown and kept, so text, scroll position and history survive a
/// trip to another page.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<string, Control> _views = [];
    private readonly IUiNavigation _navigation;
    private bool _disposed;

    [ObservableProperty]
    private string _selectedKey = AppPages.Translate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private Control? _currentPageView;

    [ObservableProperty]
    private string? _historyBadge;

    public MainViewModel(
        TranslatePageViewModel translate,
        HistoryPageViewModel history,
        SettingsPageViewModel settings,
        AppState state,
        IUiNavigation navigation)
    {
        Translate = translate;
        History = history;
        Settings = settings;
        State = state;
        _navigation = navigation;

        _navigation.NavigateRequested += OnNavigateRequested;
        _navigation.TranslateRequested += OnTranslateRequested;
        Translate.AppStateChanged += (_, _) => State.RefreshModelStatus();
        History.CountChanged += (_, _) => UpdateHistoryBadge();
        UpdateHistoryBadge();

        ShowPage(SelectedKey);
    }

    /// <summary>The translate page.</summary>
    public TranslatePageViewModel Translate { get; }

    /// <summary>The history page.</summary>
    public HistoryPageViewModel History { get; }

    /// <summary>The settings page.</summary>
    public SettingsPageViewModel Settings { get; }

    /// <summary>The model and hover state the top bar shows.</summary>
    public AppState State { get; }

    /// <summary>The current page's title, for the top bar.</summary>
    public string? Title => AppPages.Find(SelectedKey)?.Title;

    /// <summary>The current page's subtitle, for the top bar.</summary>
    public string? Subtitle => AppPages.Find(SelectedKey)?.Subtitle;

    /// <summary>Starts the live model status and restores the hover preference. Call once after the window exists.</summary>
    public void Initialize() => State.Initialize();

    partial void OnSelectedKeyChanged(string value)
    {
        if (AppPages.Find(value) is null)
        {
            // A key that names no page keeps the page that is showing.
            return;
        }

        ShowPage(value!);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _navigation.NavigateRequested -= OnNavigateRequested;
        _navigation.TranslateRequested -= OnTranslateRequested;
        Translate.Dispose();
    }

    private void ShowPage(string key)
    {
        if (!_views.TryGetValue(key, out var view))
        {
            view = CreateView(key);
            _views[key] = view;
        }

        if (!ReferenceEquals(view, CurrentPageView))
        {
            CurrentPageView = view;
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }

    private Control CreateView(string key) => key switch
    {
        AppPages.History => new HistoryPage { DataContext = History },
        AppPages.Settings => new SettingsPage { DataContext = Settings },
        _ => new TranslatePage { DataContext = Translate }
    };

    private void OnNavigateRequested(object? sender, string key) => SelectedKey = key;

    private void OnTranslateRequested(object? sender, string text)
    {
        SelectedKey = AppPages.Translate;
        Translate.LoadText(text);
    }

    private void UpdateHistoryBadge() =>
        HistoryBadge = History.Count > 0 ? History.Count.ToString(CultureInfo.InvariantCulture) : null;
}

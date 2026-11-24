using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoverTrans.Core.History;
using HoverTrans.Navigation;

namespace HoverTrans.ViewModels;

/// <summary>
/// The history page: the recent translations, newest first, narrowed as you type. A row can put its text back in the
/// translate page, copy the translation or be removed; clearing everything takes a second press.
/// </summary>
public sealed partial class HistoryPageViewModel : ObservableObject
{
    private readonly HistoryStore _store;
    private readonly IUiNavigation _navigation;
    private readonly DispatcherTimer _confirmTimer;
    private readonly List<HistoryEntry> _entries = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isConfirmingClear;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public HistoryPageViewModel(HistoryStore store, IUiNavigation navigation)
    {
        _store = store;
        _navigation = navigation;
        _store.Changed += (_, _) => Refresh();
        _confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _confirmTimer.Tick += (_, _) =>
        {
            _confirmTimer.Stop();
            IsConfirmingClear = false;
        };
        Refresh();
    }

    /// <summary>The rows on screen (already filtered by <see cref="SearchText"/>).</summary>
    public ObservableCollection<HistoryEntryViewModel> Items { get; } = [];

    /// <summary>Whether there is nothing to show and nothing typed.</summary>
    public bool IsEmpty => Items.Count == 0 && string.IsNullOrWhiteSpace(SearchText);

    /// <summary>Whether a search hid every row.</summary>
    public bool HasNoMatches => Items.Count == 0 && !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>"共 12 条" or "还没有记录".</summary>
    public string CountText => _entries.Count == 0 ? "还没有记录" : $"共 {_entries.Count} 条";

    /// <summary>What the clear button says; the second press is the one that clears.</summary>
    public string ClearButtonText => IsConfirmingClear ? "再点一次清空" : "清空历史";

    /// <summary>How many entries are stored, for the sidebar badge.</summary>
    public int Count => _entries.Count;

    /// <summary>Raised when the number of stored entries changes.</summary>
    public event EventHandler? CountChanged;

    /// <summary>Re-reads the file; called when the page is shown and when a translation finishes.</summary>
    public void Refresh()
    {
        _entries.Clear();
        _entries.AddRange(_store.Load());
        ApplyFilter();
        OnPropertyChanged(nameof(CountText));
        CountChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void GoTranslate() => _navigation.Navigate(AppPages.Translate);

    [RelayCommand]
    private void ClearAll()
    {
        if (!IsConfirmingClear)
        {
            IsConfirmingClear = true;
            StatusText = "清空后无法恢复，再点一次确认。";
            _confirmTimer.Start();
            OnPropertyChanged(nameof(ClearButtonText));
            return;
        }

        _confirmTimer.Stop();
        IsConfirmingClear = false;
        _store.Clear();
        StatusText = "历史已清空。";
        OnPropertyChanged(nameof(ClearButtonText));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnIsConfirmingClearChanged(bool value) => OnPropertyChanged(nameof(ClearButtonText));

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var matches = _entries
            .Where(entry => query.Length == 0
                            || entry.SourceText.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || entry.ResultText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Items.Clear();
        foreach (var entry in matches)
        {
            Items.Add(new HistoryEntryViewModel(entry, TranslateAgain, Remove));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoMatches));
    }

    private void TranslateAgain(HistoryEntryViewModel row)
    {
        _navigation.Translate(row.FullSource);
    }

    private void Remove(HistoryEntryViewModel row)
    {
        _entries.Remove(row.Entry);
        _store.Save(_entries);
        ApplyFilter();
        OnPropertyChanged(nameof(CountText));
        StatusText = "已删除一条记录。";
        CountChanged?.Invoke(this, EventArgs.Empty);
    }
}

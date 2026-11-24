using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoverTrans.Core.History;
using HoverTrans.Core.Languages;

namespace HoverTrans.ViewModels;

/// <summary>One row of the history page, with the text already trimmed to what a row can show.</summary>
public sealed partial class HistoryEntryViewModel : ObservableObject
{
    private const int PreviewLength = 160;

    public HistoryEntryViewModel(HistoryEntry entry, Action<HistoryEntryViewModel> translateAgain, Action<HistoryEntryViewModel> remove)
    {
        Entry = entry;
        TranslateAgainCommand = new RelayCommand(() => translateAgain(this));
        RemoveCommand = new RelayCommand(() => remove(this));

        SourcePreview = Shorten(entry.SourceText);
        ResultPreview = Shorten(entry.ResultText);
        LanguagePair = $"{LanguageCatalog.Find(entry.SourceLanguage).DisplayName} → {LanguageCatalog.Find(entry.TargetLanguage).DisplayName}";
        TimeText = FormatTime(entry.CreatedAt);
        IsFromImage = entry.FromImage;
        FullSource = entry.SourceText;
        FullResult = entry.ResultText;
    }

    /// <summary>The stored entry.</summary>
    public HistoryEntry Entry { get; }

    /// <summary>The source text, trimmed for the row.</summary>
    public string SourcePreview { get; }

    /// <summary>The translation, trimmed for the row.</summary>
    public string ResultPreview { get; }

    /// <summary>The full source text (what "translate again" uses).</summary>
    public string FullSource { get; }

    /// <summary>The full translation (what the copy action uses).</summary>
    public string FullResult { get; }

    /// <summary>"英语 → 中文（简体）".</summary>
    public string LanguagePair { get; }

    /// <summary>"今天 14:02" or "11月23日".</summary>
    public string TimeText { get; }

    /// <summary>Whether the text came from an image.</summary>
    public bool IsFromImage { get; }

    /// <summary>Translates the source text again and shows the translate page.</summary>
    public IRelayCommand TranslateAgainCommand { get; }

    /// <summary>Removes this row from the history.</summary>
    public IRelayCommand RemoveCommand { get; }

    private static string Shorten(string text)
    {
        var flat = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= PreviewLength ? flat : flat[..PreviewLength] + "…";
    }

    private static string FormatTime(DateTimeOffset time)
    {
        var local = time.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today)
        {
            return "今天 " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        if (local.Date == today.AddDays(-1))
        {
            return "昨天 " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        return local.Year == DateTimeOffset.Now.Year
            ? local.ToString("M月d日", CultureInfo.InvariantCulture)
            : local.ToString("yyyy年M月d日", CultureInfo.InvariantCulture);
    }
}

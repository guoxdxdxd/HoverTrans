using CommunityToolkit.Mvvm.ComponentModel;
using HoverTrans.Services;

namespace HoverTrans.ViewModels;

/// <summary>The small always-on-top popup shown next to the mouse.</summary>
public sealed partial class HoverViewModel : ObservableObject
{
    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private string _translationText = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isTranslating;

    [ObservableProperty]
    private bool _isError;

    public bool HasTranslation => !string.IsNullOrWhiteSpace(TranslationText);

    public void Apply(HoverSnapshot snapshot)
    {
        SourceText = snapshot.SourceText;
        TranslationText = snapshot.Translation ?? string.Empty;
        Status = snapshot.Status;
        IsTranslating = snapshot.IsTranslating;
        IsError = snapshot.IsError;
        OnPropertyChanged(nameof(HasTranslation));
    }
}
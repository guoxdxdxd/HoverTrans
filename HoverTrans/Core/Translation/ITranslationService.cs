namespace HoverTrans.Core.Translation;

/// <summary>The translation pipeline used by the UI and by hover mode.</summary>
public interface ITranslationService
{
    /// <summary>A short, localized description of the local model state.</summary>
    string StatusText { get; }

    /// <summary>Whether the model weights are loaded and translations need no warm-up.</summary>
    bool IsModelLoaded { get; }

    /// <summary>
    /// Ensures the model is loaded, translates <paramref name="request"/> and reports
    /// incremental text through <paramref name="progress"/>.
    /// </summary>
    Task TranslateAsync(
        TranslationRequest request,
        IProgress<TranslationProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases the model's memory now; the next translation loads it again. Does nothing while a
    /// translation is running.
    /// </summary>
    Task UnloadModelAsync(CancellationToken cancellationToken = default);
}

namespace HoverTrans.Core.Translation;

/// <summary>The translation pipeline used by the UI and by hover mode.</summary>
public interface ITranslationService
{
    /// <summary>A short, localized description of the model process state.</summary>
    string StatusText { get; }

    bool IsModelRunning { get; }

    /// <summary>
    /// Ensures the model is running, translates <paramref name="request"/> and reports
    /// incremental text through <paramref name="progress"/>.
    /// </summary>
    Task TranslateAsync(
        TranslationRequest request,
        IProgress<TranslationProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>Stops the model process now (it will be started again on the next request).</summary>
    Task StopModelAsync(CancellationToken cancellationToken = default);
}
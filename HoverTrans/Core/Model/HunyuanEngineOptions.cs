namespace HoverTrans.Core.Model;

/// <summary>
/// How to load and drive the local Hy-MT3 model, and when to give its memory back.
/// </summary>
/// <param name="Backend">"auto" (Vulkan when the driver can host the model, else CPU), "cpu" or "vulkan".</param>
/// <param name="Threads">Worker threads; 0 lets the library bind the physical P-cores.</param>
/// <param name="MaxTokens">Upper bound of generated tokens per request (a sentence needs tens).</param>
/// <param name="IdleUnload">
/// Release the weights after this long without a translation; null or zero keeps them for the life of the
/// process. A released model is loaded again on the next translation.
/// </param>
public sealed record HunyuanEngineOptions(
    string Backend = "auto",
    int Threads = 0,
    int MaxTokens = 2048,
    TimeSpan? IdleUnload = null)
{
    /// <summary>
    /// The backend pref as the library expects it. Anything the library does not know becomes "auto"
    /// rather than an error, so a hand-edited settings file cannot stop the app from translating.
    /// </summary>
    public static string Normalize(string? backend) => backend?.Trim().ToLowerInvariant() switch
    {
        "cpu" => "cpu",
        "vulkan" => "vulkan",
        "metal" => "metal",
        _ => "auto"
    };

    /// <summary>
    /// The largest prompt the engine will accept. The library preallocates its KV cache for
    /// min(context length, 4096) tokens, so anything longer would grow the cache past what the
    /// model was loaded with; <see cref="Core.Translation.TextChunker"/> keeps chunks far below it.
    /// </summary>
    public const int MaxPromptTokens = 4096;

    /// <summary>The backend pref as the library expects it: lower case, "auto" when unset or unknown.</summary>
    public string BackendPreference => Normalize(Backend);

    /// <summary>How long to wait without a translation before releasing the weights, or null to never release.</summary>
    public TimeSpan? IdleUnloadValue => IdleUnload is { } value && value > TimeSpan.Zero ? value : null;

    /// <summary>
    /// How often to look for an idle engine: a quarter of the timeout, at most every 30 seconds and at
    /// least every second, so a short timeout is still honoured and a long one costs nothing.
    /// </summary>
    public TimeSpan IdleCheckInterval => IdleUnloadValue is { } value
        ? TimeSpan.FromSeconds(Math.Clamp(value.TotalSeconds / 4, 1, 30))
        : TimeSpan.FromSeconds(30);
}

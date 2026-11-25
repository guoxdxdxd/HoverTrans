namespace HoverTrans.Core.Model;

/// <summary>
/// How to load and drive the local Hy-MT3 model. Everything here is read once at start-up by
/// <see cref="HunyuanEngine"/>; there is no process, port or idle timer to configure.
/// </summary>
/// <param name="Backend">"auto" (Vulkan when the driver can host the model, else CPU), "cpu" or "vulkan".</param>
/// <param name="Threads">Worker threads; 0 lets the library bind the physical P-cores.</param>
/// <param name="MaxTokens">Upper bound of generated tokens per request (a sentence needs tens).</param>
public sealed record HunyuanEngineOptions(string Backend = "auto", int Threads = 0, int MaxTokens = 2048)
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

    /// <summary>The backend pref as the library expects it: lower case, "auto" when unset or unknown.</summary>
    public string BackendPreference => Normalize(Backend);

    /// <summary>
    /// The largest prompt the engine will accept. The library preallocates its KV cache for
    /// min(context length, 4096) tokens, so anything longer would grow the cache past what the
    /// model was loaded with; <see cref="Core.Translation.TextChunker"/> keeps chunks far below it.
    /// </summary>
    public const int MaxPromptTokens = 4096;
}

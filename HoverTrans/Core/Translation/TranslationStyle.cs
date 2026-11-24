namespace HoverTrans.Core.Translation;

/// <summary>
/// How the model should translate: word for word, or for the sense and the voice of the original. Both run on the
/// same model; the difference is one style clause in the instruction (see <see cref="TranslationPrompt"/>).
/// </summary>
public enum TranslationStyle
{
    /// <summary>
    /// Faithful: the model's trained default. Best for a hovered word, a UI label or a contract, where "what it says"
    /// matters more than "how it would be said".
    /// </summary>
    Literal,

    /// <summary>
    /// Natural: colloquial everyday speech that keeps the speaker's tone and turns slang and idioms into the
    /// equivalent in the target language. For chat, subtitles and social posts.
    /// </summary>
    Natural
}

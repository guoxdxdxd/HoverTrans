using HoverTrans.Core.Languages;

namespace HoverTrans.Core.Translation;

/// <summary>
/// The instruction sent to Hy-MT2. Tencent's translation models are trained with the
/// "Translate the following ..." pattern and follow a short style clause in brackets, so the instruction is one
/// English sentence plus the style, then the source text after a blank line.
/// </summary>
public static class TranslationPrompt
{
    /// <summary>
    /// The style clause of <see cref="TranslationStyle.Natural"/>. Hy-MT2 states the wanted style in
    /// <c>[...]</c> and adjusts vocabulary and sentence structure to it; keep it short and concrete.
    /// </summary>
    private const string NaturalStyle =
        "colloquial, natural everyday speech: keep the speaker's tone and force, and render slang, idioms and wordplay " +
        "with their established equivalents instead of word-for-word literals";

    public static string Build(
        string text,
        string? sourceLanguage,
        string targetLanguage,
        TranslationStyle style = TranslationStyle.Literal)
    {
        var target = LanguageCatalog.PromptName(targetLanguage);
        var source = string.IsNullOrWhiteSpace(sourceLanguage) || sourceLanguage == LanguageCatalog.AutoCode
            ? null
            : LanguageCatalog.PromptName(sourceLanguage);

        var subject = source is null ? "text" : $"{source} text";
        var instruction = style == TranslationStyle.Natural
            ? $"Translate the following {subject} into {target}. Note that the translation style must strictly " +
              $"conform to [{NaturalStyle}], and that you should only output the translated result without any " +
              "additional explanation."
            : $"Translate the following {subject} into {target}. Note that you should only output the translated " +
              "result without any additional explanation.";

        return instruction + "\n\n" + text.Trim();
    }
}

using HoverTrans.Core.Languages;

namespace HoverTrans.Core.Translation;

/// <summary>
/// The instruction sent to Hy-MT2. Tencent's translation models are trained with this
/// "Translate the following ... without additional explanation." pattern; keeping the
/// instruction short and literal gives the best behaviour on the 1.8B model.
/// </summary>
public static class TranslationPrompt
{
    public static string Build(string text, string? sourceLanguage, string targetLanguage)
    {
        var target = LanguageCatalog.PromptName(targetLanguage);
        var source = string.IsNullOrWhiteSpace(sourceLanguage) || sourceLanguage == LanguageCatalog.AutoCode
            ? null
            : LanguageCatalog.PromptName(sourceLanguage);

        var instruction = source is null
            ? $"Translate the following text into {target}, without additional explanation."
            : $"Translate the following {source} text into {target}, without additional explanation.";

        return instruction + "\n\n" + text.Trim();
    }
}
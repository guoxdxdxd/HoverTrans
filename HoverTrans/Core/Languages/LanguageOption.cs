namespace HoverTrans.Core.Languages;

/// <summary>A language the app can translate from or to.</summary>
/// <param name="Code">BCP-47-ish code used by the app ("zh-CN", "en", "ja", ...).</param>
/// <param name="DisplayName">Name shown in the UI.</param>
/// <param name="PromptName">Name written into the prompt sent to the model.</param>
public sealed record LanguageOption(string Code, string DisplayName, string PromptName);
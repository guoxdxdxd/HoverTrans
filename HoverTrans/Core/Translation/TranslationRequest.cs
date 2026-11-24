namespace HoverTrans.Core.Translation;

/// <summary>One translation request. <see cref="SourceLanguage"/> null or "auto" means detect.</summary>
public sealed record TranslationRequest(string Text, string? SourceLanguage, string TargetLanguage);
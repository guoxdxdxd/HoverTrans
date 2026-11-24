namespace HoverTrans.Core.Translation;

public enum TranslationPhase
{
    StartingModel,
    Translating,
    Delta,
    Completed
}

/// <summary>Progress reported while a translation runs. <see cref="Text"/> is the result so far.</summary>
public sealed record TranslationProgress(TranslationPhase Phase, string Text, string? Message = null)
{
    public static TranslationProgress StartingModel() =>
        new(TranslationPhase.StartingModel, string.Empty, "正在启动本地翻译模型…");

    public static TranslationProgress Translating() =>
        new(TranslationPhase.Translating, string.Empty, "正在翻译…");

    public static TranslationProgress Delta(string text) =>
        new(TranslationPhase.Delta, text, "正在翻译…");

    public static TranslationProgress Completed(string text) =>
        new(TranslationPhase.Completed, text, "翻译完成");
}
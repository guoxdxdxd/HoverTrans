namespace HoverTrans.Core.History;

/// <summary>One translation the person made in the main window: enough to look back at, copy again or restore.</summary>
public sealed class HistoryEntry
{
    /// <summary>The text that was sent to the model (image text already recognized).</summary>
    public string SourceText { get; set; } = string.Empty;

    /// <summary>The finished translation.</summary>
    public string ResultText { get; set; } = string.Empty;

    /// <summary>"auto" or a language code.</summary>
    public string SourceLanguage { get; set; } = "auto";

    /// <summary>The language code the text was translated into.</summary>
    public string TargetLanguage { get; set; } = "zh-CN";

    /// <summary>When the translation finished, in local time.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>Whether the text came from an image (so the history can show it differently).</summary>
    public bool FromImage { get; set; }
}

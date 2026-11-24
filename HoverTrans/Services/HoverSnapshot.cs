namespace HoverTrans.Services;

/// <summary>One state of the hover popup. Raised on a background thread; the window manager marshals it to the UI.</summary>
public sealed record HoverSnapshot(
    string SourceText,
    string? Translation,
    string Status,
    bool IsTranslating,
    bool IsError,
    int X,
    int Y);
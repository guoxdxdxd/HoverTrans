namespace HoverTrans.Core.Platform;

/// <summary>The text under the mouse, with the screen rectangle of the element that owns it.</summary>
public sealed record HoverText(string Text, int X, int Y, int Width, int Height);

/// <summary>Reads the text of the control under the mouse pointer.</summary>
public interface IHoverTextSource
{
    bool IsSupported { get; }

    string UnsupportedMessage { get; }

    /// <summary>Returns null when the pointer is over nothing useful, over the app itself, or over a password field.</summary>
    HoverText? GetTextAtCursor();
}
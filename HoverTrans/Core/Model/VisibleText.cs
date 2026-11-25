namespace HoverTrans.Core.Model;

/// <summary>
/// Turns the decoded text of a growing token list into the text that can be shown now.
/// </summary>
/// <remarks>
/// The tokenizer decodes one token at a time, and a multi-byte character is sometimes split across
/// two or three tokens: decoding the first one alone yields half a UTF-8 sequence, which shows up as
/// U+FFFD. Holding the trailing replacement character until the next token completes it keeps the
/// stream free of those flashes (see HyMT2Sharp's "pitfalls" note); the final text is unaffected
/// either way. The tracker also absorbs the defensive case where the decoded text does not extend
/// the previous one: the visible text is what the caller shows, never an accumulated delta.
/// </remarks>
public sealed class VisibleText
{
    private string _visible = string.Empty;

    /// <summary>The text that can be shown after this many tokens.</summary>
    public string Value => _visible;

    /// <summary>
    /// Takes the decode of every token generated so far and returns the text to show, or an empty
    /// string when the new tokens only completed a character that is not complete yet.
    /// </summary>
    public string Update(string decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        // A trailing U+FFFD is a UTF-8 sequence split across tokens: wait for the next token.
        var next = decoded.TrimEnd('\uFFFD');
        if (next.Length == 0)
        {
            return string.Empty;
        }

        // Normally the text only grows. If it does not (a defensive case), show what is there.
        _visible = next;
        return next;
    }
}

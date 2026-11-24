namespace HoverTrans.Core.Translation;

/// <summary>Splits long input on paragraph or sentence boundaries before sending it to the model.</summary>
public static class TextChunker
{
    private static readonly char[] SentenceEnds = ['.', '!', '?', '。', '！', '？', '；', ';'];

    public static IReadOnlyList<string> Split(string? text, int maxChars = 1800)
    {
        if (maxChars < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChars));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length <= maxChars)
        {
            return [normalized];
        }

        var chunks = new List<string>();
        var start = 0;

        while (start < normalized.Length)
        {
            var remaining = normalized.Length - start;
            if (remaining <= maxChars)
            {
                Add(chunks, normalized[start..]);
                break;
            }

            var windowEnd = start + maxChars;
            var cut = normalized.LastIndexOf('\n', windowEnd - 1, maxChars);

            if (cut <= start)
            {
                cut = normalized.LastIndexOfAny(SentenceEnds, windowEnd - 1, maxChars);
                if (cut > start)
                {
                    cut++; // keep the punctuation with the sentence
                }
            }

            if (cut <= start)
            {
                cut = windowEnd;
            }

            // Never cut a surrogate pair in half.
            if (cut > start && char.IsHighSurrogate(normalized[cut - 1]))
            {
                cut--;
            }

            Add(chunks, normalized[start..cut]);
            start = cut;
        }

        return chunks;
    }

    private static void Add(List<string> chunks, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 0)
        {
            chunks.Add(trimmed);
        }
    }
}
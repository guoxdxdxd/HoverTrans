namespace HoverTrans.Core.Languages;

/// <summary>
/// A deliberately small, dependency-free script detector. It is not a full language identifier:
/// it answers "is this text worth translating to Chinese?" for hover mode, and gives a best-effort
/// source language for the model when the user leaves the source on "auto".
/// </summary>
public static class LanguageDetector
{
    public static string Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LanguageCatalog.AutoCode;
        }

        var kana = 0;
        var hangul = 0;
        var han = 0;
        var latin = 0;
        var cyrillic = 0;
        var arabic = 0;
        var thai = 0;
        var devanagari = 0;
        var totalLetters = 0;

        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                totalLetters++;
            }

            if (IsKana(ch))
            {
                kana++;
            }
            else if (IsHangul(ch))
            {
                hangul++;
            }
            else if (IsHan(ch))
            {
                han++;
            }
            else if (IsLatin(ch))
            {
                latin++;
            }
            else if (IsCyrillic(ch))
            {
                cyrillic++;
            }
            else if (IsArabic(ch))
            {
                arabic++;
            }
            else if (IsThai(ch))
            {
                thai++;
            }
            else if (IsDevanagari(ch))
            {
                devanagari++;
            }
        }

        if (totalLetters == 0)
        {
            return LanguageCatalog.AutoCode;
        }

        if (kana > 0)
        {
            return "ja";
        }

        if (hangul > 0)
        {
            return "ko";
        }

        if (han > 0 && han >= latin + cyrillic + arabic + thai + devanagari)
        {
            return "zh-CN";
        }

        if (cyrillic > 0 && cyrillic >= latin)
        {
            return "ru";
        }

        if (arabic > 0)
        {
            return "ar";
        }

        if (thai > 0)
        {
            return "th";
        }

        if (devanagari > 0)
        {
            return "hi";
        }

        return "en";
    }

    /// <summary>
    /// True when the hovered text is not Chinese and is long enough to be worth a request.
    /// Mixed text is translated when the foreign part is at least as large as the Chinese part.
    /// </summary>
    public static bool ShouldTranslateToChinese(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length < 2)
        {
            return false;
        }

        var han = 0;
        var kana = 0;
        var hangul = 0;
        var otherLetters = 0;

        foreach (var ch in trimmed)
        {
            if (IsKana(ch))
            {
                kana++;
            }
            else if (IsHangul(ch))
            {
                hangul++;
            }
            else if (IsHan(ch))
            {
                han++;
            }
            else if (char.IsLetter(ch))
            {
                otherLetters++;
            }
        }

        if (kana > 0 || hangul > 0)
        {
            return true;
        }

        if (han == 0)
        {
            return otherLetters > 0;
        }

        return otherLetters >= han && otherLetters > 0;
    }

    private static bool IsHan(char ch) =>
        ch is >= '\u4E00' and <= '\u9FFF' or >= '\u3400' and <= '\u4DBF' or >= '\uF900' and <= '\uFAFF';

    private static bool IsKana(char ch) =>
        ch is >= '\u3040' and <= '\u30FF' or >= '\u31F0' and <= '\u31FF';

    private static bool IsHangul(char ch) =>
        ch is >= '\uAC00' and <= '\uD7AF' or >= '\u1100' and <= '\u11FF' or >= '\u3130' and <= '\u318F';

    private static bool IsLatin(char ch) =>
        ch is >= '\u0041' and <= '\u005A' or >= '\u0061' and <= '\u007A'
            or >= '\u00C0' and <= '\u024F' or >= '\u1E00' and <= '\u1EFF';

    private static bool IsCyrillic(char ch) => ch is >= '\u0400' and <= '\u04FF';

    private static bool IsArabic(char ch) => ch is >= '\u0600' and <= '\u06FF';

    private static bool IsThai(char ch) => ch is >= '\u0E00' and <= '\u0E7F';

    private static bool IsDevanagari(char ch) => ch is >= '\u0900' and <= '\u097F';
}
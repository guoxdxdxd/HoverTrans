namespace HoverTrans.Core.Updates;

/// <summary>
/// A version read from a release tag or from the assembly, compared the way NuGet compares them: a prerelease sorts
/// before the release it belongs to (<c>0.2.0-beta.1 &lt; 0.2.0</c>). Build metadata after <c>+</c> is ignored, because
/// it says where a build came from, not which version it is.
/// </summary>
public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    public SemanticVersion(int major, int minor, int patch, string prerelease = "")
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease ?? string.Empty;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The dotted identifiers after <c>-</c>, or an empty string for a release.</summary>
    public string Prerelease { get; }

    /// <summary>Whether this version has no prerelease part.</summary>
    public bool IsRelease => Prerelease.Length == 0;

    /// <summary>
    /// Reads <c>1.2.3</c>, <c>v1.2.3</c>, <c>1.2</c>, <c>1.2.3-beta.1</c> or <c>1.2.3+sha</c>. Anything else is rejected
    /// rather than guessed at: a tag the app cannot read must not look like a newer version.
    /// </summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text!.Trim();
        if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V'))
        {
            text = text[1..];
        }

        var metadata = text.IndexOf('+');
        if (metadata >= 0)
        {
            text = text[..metadata];
        }

        var prerelease = string.Empty;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            text = text[..dash];
            if (prerelease.Length == 0)
            {
                return false;
            }
        }

        var parts = text.Split('.');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
            {
                return false;
            }
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    /// <summary>Like <see cref="TryParse"/>, but throws when the text is not a version.</summary>
    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"不是版本号：{text}");

    public int CompareTo(SemanticVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        // A release is newer than any prerelease of the same numbers.
        if (IsRelease && other.IsRelease)
        {
            return 0;
        }

        if (IsRelease)
        {
            return 1;
        }

        if (other.IsRelease)
        {
            return -1;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public bool Equals(SemanticVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    public override string ToString() => IsRelease ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
        {
            var leftIsNumber = int.TryParse(leftParts[i], out var leftNumber);
            var rightIsNumber = int.TryParse(rightParts[i], out var rightNumber);
            int result;
            if (leftIsNumber && rightIsNumber)
            {
                result = leftNumber.CompareTo(rightNumber);
            }
            else if (leftIsNumber)
            {
                // Numeric identifiers sort before alphanumeric ones (semver 2.0).
                return -1;
            }
            else if (rightIsNumber)
            {
                return 1;
            }
            else
            {
                result = string.Compare(leftParts[i], rightParts[i], StringComparison.OrdinalIgnoreCase);
            }

            if (result != 0)
            {
                return result;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }
}

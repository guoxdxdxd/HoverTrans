namespace HoverTrans.Core.Updates;

/// <summary>
/// One published release as the app needs it: which version, where to read about it, and what to download. The notes are
/// kept raw because the only thing the app does with them is show the first lines.
/// </summary>
public sealed record AppRelease(
    SemanticVersion Version,
    string TagName,
    string Title,
    string Notes,
    Uri Page,
    Uri? Installer,
    DateTimeOffset? PublishedAt)
{
    /// <summary>The installer when the release has one, otherwise the release page.</summary>
    public Uri Download => Installer ?? Page;
}

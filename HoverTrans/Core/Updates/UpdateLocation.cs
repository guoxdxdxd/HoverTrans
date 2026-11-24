using System.Reflection;

namespace HoverTrans.Core.Updates;

/// <summary>
/// Where the app looks for new versions. Kept in one place so the GitHub repository the app was built for, the page a
/// person opens to download, and the "开源" line in the settings all agree.
/// </summary>
public static class UpdateLocation
{
    /// <summary>The GitHub repository the release feed is read from (<c>owner/name</c>).</summary>
    public const string Repository = "guoxdxdxd/HoverTrans";

    /// <summary>The human page for downloads; the fallback when a release has no installer attached.</summary>
    public static Uri ReleasesPage { get; } = new($"https://github.com/{Repository}/releases");

    /// <summary>The API call behind the check.</summary>
    public static Uri LatestReleaseApi { get; } = new($"https://api.github.com/repos/{Repository}/releases/latest");
}

/// <summary>The version this build reports: <c>0.1.0</c>, or <c>0.2.0-beta.1</c> for a prerelease.</summary>
public static class AppVersion
{
    /// <summary>Read from the assembly once, so the settings page and the update check cannot disagree.</summary>
    public static SemanticVersion Current { get; } = Read();

    /// <summary>The version as the settings page shows it.</summary>
    public static string Display => Current.ToString();

    private static SemanticVersion Read()
    {
        var assembly = typeof(AppVersion).Assembly;

        // <Version> in the project file lands here, together with the source revision after '+'.
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (SemanticVersion.TryParse(informational, out var version))
        {
            return version;
        }

        return SemanticVersion.TryParse(assembly.GetName().Version?.ToString(), out var fallback)
            ? fallback
            : new SemanticVersion(0, 0, 0);
    }
}

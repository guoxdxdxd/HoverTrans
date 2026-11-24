namespace HoverTrans.Core.Updates;

/// <summary>What one look at the release feed found.</summary>
public enum UpdateStatus
{
    /// <summary>The newest release is the version that is running.</summary>
    UpToDate,

    /// <summary>The newest release is newer than the version that is running.</summary>
    UpdateAvailable,

    /// <summary>Nothing has been published yet, so there is nothing to compare against.</summary>
    NoReleases,

    /// <summary>The feed could not be read; <see cref="UpdateCheck.Failure"/> says why.</summary>
    Failed
}

/// <summary>Why a check could not answer the question.</summary>
public enum UpdateFailure
{
    None,

    /// <summary>No connection, DNS failure, timeout or TLS problem.</summary>
    Network,

    /// <summary>GitHub's unauthenticated rate limit, which is per IP and resets within the hour.</summary>
    RateLimited,

    /// <summary>GitHub answered, but not with a release: a server error or a 404 on the repository itself.</summary>
    HttpError,

    /// <summary>A release came back in a shape the app does not understand.</summary>
    BadResponse
}

/// <summary>
/// The answer to "is there a newer version?". It carries the version that was compared as well as the result, so the
/// caller never has to guess what "up to date" was measured against.
/// </summary>
public sealed record UpdateCheck(
    UpdateStatus Status,
    SemanticVersion CurrentVersion,
    AppRelease? Release,
    UpdateFailure Failure = UpdateFailure.None,
    string? Detail = null)
{
    /// <summary>Whether the caller should offer the download.</summary>
    public bool HasUpdate => Status == UpdateStatus.UpdateAvailable;

    /// <summary>A check that found a newer release.</summary>
    public static UpdateCheck Available(SemanticVersion current, AppRelease release) =>
        new(UpdateStatus.UpdateAvailable, current, release);

    /// <summary>A check that found the newest release already running.</summary>
    public static UpdateCheck Latest(SemanticVersion current, AppRelease release) =>
        new(UpdateStatus.UpToDate, current, release);

    /// <summary>A check that found nothing newer to offer (the newest thing is a draft or a prerelease).</summary>
    public static UpdateCheck Newest(SemanticVersion current) => new(UpdateStatus.UpToDate, current, null);

    /// <summary>Nothing has been published at all, so there is nothing to compare against.</summary>
    public static UpdateCheck NoReleases(SemanticVersion current) => new(UpdateStatus.NoReleases, current, null);

    /// <summary>A check that could not run.</summary>
    public static UpdateCheck Failed(SemanticVersion current, UpdateFailure failure, string? detail = null) =>
        new(UpdateStatus.Failed, current, null, failure, detail);
}

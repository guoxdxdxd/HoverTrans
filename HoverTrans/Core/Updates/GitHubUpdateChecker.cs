using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoverTrans.Core.Updates;

/// <summary>
/// Reads GitHub's "latest release" endpoint and compares its tag with the running version. Unauthenticated, which is
/// enough for a check per click: GitHub allows 60 calls an hour per address, and the response is small.
/// </summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private readonly HttpClient _http;
    private readonly SemanticVersion _current;

    /// <summary>Creates the checker over <paramref name="http"/>; the app passes one client and the running version.</summary>
    public GitHubUpdateChecker(HttpClient http, SemanticVersion? current = null)
    {
        _http = http;
        _current = current ?? AppVersion.Current;
    }

    /// <inheritdoc />
    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateLocation.LatestReleaseApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        // GitHub rejects requests without a user agent, and its answers are cached per repository + agent.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HoverTrans", _current.ToString()));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            // Timeout, DNS, TLS interception, airplane mode: all the same answer to the person.
            return UpdateCheck.Failed(_current, UpdateFailure.Network, exception.Message);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Either the repository or its releases are not there; both mean "nothing to upgrade to".
                return UpdateCheck.NoReleases(_current);
            }

            if ((response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) &&
                IsRateLimited(response))
            {
                return UpdateCheck.Failed(_current, UpdateFailure.RateLimited);
            }

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheck.Failed(_current, UpdateFailure.HttpError, ((int)response.StatusCode).ToString());
            }

            string json;
            try
            {
                json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                return UpdateCheck.Failed(_current, UpdateFailure.Network, exception.Message);
            }

            return Read(json);
        }
    }

    /// <summary>Turns the payload into a verdict. Kept separate so a test can feed it text without a server.</summary>
    internal UpdateCheck Read(string json)
    {
        GitHubRelease? release;
        try
        {
            release = JsonSerializer.Deserialize(json, UpdateJsonContext.Default.GitHubRelease);
        }
        catch (JsonException exception)
        {
            return UpdateCheck.Failed(_current, UpdateFailure.BadResponse, exception.Message);
        }

        var tag = release?.TagName;
        if (release is null || string.IsNullOrWhiteSpace(tag) || !SemanticVersion.TryParse(tag, out var version))
        {
            return UpdateCheck.Failed(_current, UpdateFailure.BadResponse, tag);
        }

        // A draft or a prerelease is not something the update dot should point at: the person installed a release.
        if (release.Draft || release.Prerelease)
        {
            return UpdateCheck.Newest(_current);
        }

        var found = ToRelease(release, version);
        return version > _current ? UpdateCheck.Available(_current, found) : UpdateCheck.Latest(_current, found);
    }

    private static AppRelease ToRelease(GitHubRelease release, SemanticVersion version)
    {
        var installer = release.Assets?
            .Where(asset => asset.Name is { Length: > 0 })
            .FirstOrDefault(asset =>
                asset.Name!.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase) ||
                (asset.Name.Contains("setup", StringComparison.OrdinalIgnoreCase) &&
                 asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)));

        return new AppRelease(
            version,
            release.TagName!,
            string.IsNullOrWhiteSpace(release.Name) ? release.TagName! : release.Name!,
            release.Body ?? string.Empty,
            AsUri(release.HtmlUrl) ?? UpdateLocation.ReleasesPage,
            AsUri(installer?.BrowserDownloadUrl),
            release.PublishedAt);
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
            remaining.FirstOrDefault() == "0")
        {
            return true;
        }

        return response.StatusCode == HttpStatusCode.TooManyRequests;
    }

    private static Uri? AsUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri : null;
}

/// <summary>The newest release, as GitHub describes it (only the fields the app reads).</summary>
internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}

/// <summary>One file attached to a release.</summary>
internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}

/// <summary>The release payload's serializer, generated at build time (no reflection, trimming safe).</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext
{
}

using System.Net;
using System.Net.Http;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HoverTrans.Core.Updates;
using HoverTrans.ViewModels;
using HoverTrans.Views;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoverTrans.Tests;

public class UpdateCheckerTests
{
    // ---------- versions ----------

    [Theory]
    [InlineData("0.1.0", 0, 1, 0, "")]
    [InlineData("v1.2.3", 1, 2, 3, "")]
    [InlineData("1.2", 1, 2, 0, "")]
    [InlineData("2", 2, 0, 0, "")]
    [InlineData("0.2.0-beta.1", 0, 2, 0, "beta.1")]
    [InlineData("0.1.0+sha.abc123", 0, 1, 0, "")]
    [InlineData(" V0.3.0-RC.2 ", 0, 3, 0, "RC.2")]
    public void A_version_is_read_from_a_tag(string text, int major, int minor, int patch, string prerelease)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(prerelease, version.Prerelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x.0")]
    [InlineData("-1.0.0")]
    [InlineData("1.0.0-")]
    public void Text_that_is_not_a_version_is_rejected(string text) => Assert.False(SemanticVersion.TryParse(text, out _));

    [Theory]
    [InlineData("0.1.0", "0.1.0", 0)]
    [InlineData("0.1.0", "0.2.0", -1)]
    [InlineData("0.2.0", "0.1.0", 1)]
    [InlineData("1.0.0", "0.9.9", 1)]
    [InlineData("0.1.0-beta.1", "0.1.0", -1)]
    [InlineData("0.1.0", "0.1.0-beta.1", 1)]
    [InlineData("0.1.0-beta.2", "0.1.0-beta.10", -1)]
    [InlineData("0.1.0-alpha", "0.1.0-beta", -1)]
    [InlineData("0.1.0-beta", "0.1.0-beta.1", -1)]
    [InlineData("0.1.0+sha1", "0.1.0+sha2", 0)]
    public void Versions_compare_the_way_releases_do(string left, string right, int expected)
    {
        Assert.Equal(expected, Math.Sign(SemanticVersion.Parse(left).CompareTo(SemanticVersion.Parse(right))));
    }

    // ---------- the check ----------

    [Fact]
    public async Task A_newer_release_is_offered_with_its_installer()
    {
        var checker = Checker(Release("v0.2.0"), out _);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.True(result.HasUpdate);
        Assert.Equal("0.2.0", result.Release!.Version.ToString());
        Assert.Equal(
            "https://github.com/guoxdxdxd/HoverTrans/releases/download/v0.2.0/HoverTrans-0.2.0-win-x64-setup.exe",
            result.Release.Installer!.ToString());
    }

    [Fact]
    public async Task A_release_without_an_installer_falls_back_to_its_page()
    {
        var checker = Checker(Release("v0.2.0", assets: []), out _);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Null(result.Release!.Installer);
        Assert.Equal("https://github.com/guoxdxdxd/HoverTrans/releases/tag/v0.2.0", result.Release.Download.ToString());
    }

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("v0.0.9")]
    public async Task The_running_version_or_an_older_one_is_up_to_date(string tag)
    {
        var checker = Checker(Release(tag), out _);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.False(result.HasUpdate);
        Assert.Equal(tag, result.Release!.TagName);
    }

    [Fact]
    public async Task A_prerelease_is_not_offered_to_someone_on_a_release()
    {
        var checker = Checker(Release("v0.3.0", prerelease: true), out _);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public async Task No_releases_yet_is_not_an_error()
    {
        var checker = Checker(null, out _, status: HttpStatusCode.NotFound);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.NoReleases, result.Status);
        Assert.Equal(UpdateFailure.None, result.Failure);
    }

    [Fact]
    public async Task A_rate_limited_answer_says_so()
    {
        var checker = Checker(null, out _, status: HttpStatusCode.Forbidden, rateLimited: true);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal(UpdateFailure.RateLimited, result.Failure);
    }

    [Fact]
    public async Task A_server_error_is_a_failed_check()
    {
        var checker = Checker(null, out _, status: HttpStatusCode.BadGateway);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal(UpdateFailure.HttpError, result.Failure);
        Assert.Equal("502", result.Detail);
    }

    [Fact]
    public async Task Unreadable_json_is_a_failed_check()
    {
        var checker = Checker("not json at all", out _);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal(UpdateFailure.BadResponse, result.Failure);
    }

    [Fact]
    public async Task A_tag_that_is_not_a_version_is_a_failed_check()
    {
        var checker = Checker(Release("nightly"), out _);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal(UpdateFailure.BadResponse, result.Failure);
    }

    [Fact]
    public async Task An_unreachable_github_is_a_failed_check_and_not_an_exception()
    {
        var http = new HttpClient(new ThrowingHandler());
        var checker = new GitHubUpdateChecker(http, SemanticVersion.Parse("0.1.0"));

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal(UpdateFailure.Network, result.Failure);
    }

    [Fact]
    public async Task The_request_asks_for_the_latest_release_of_the_project_repository()
    {
        var checker = Checker(Release("v0.2.0"), out var handler);

        await checker.CheckAsync();

        Assert.Equal(UpdateLocation.LatestReleaseApi, handler.LastRequest!.RequestUri);
        Assert.Contains("HoverTrans", handler.LastRequest.Headers.UserAgent.ToString());
    }

    // ---------- the settings page ----------

    [AvaloniaFact]
    public async Task Checking_from_the_settings_page_reports_the_newer_version()
    {
        var fake = new FakeChecker(UpdateCheck.Available(
            SemanticVersion.Parse("0.1.0"),
            new AppRelease(
                SemanticVersion.Parse("0.2.0"),
                "v0.2.0",
                "0.2.0",
                string.Empty,
                new Uri("https://github.com/guoxdxdxd/HoverTrans/releases/tag/v0.2.0"),
                null,
                null)));
        var viewModel = new SettingsPageViewModel(
            App.Services.GetRequiredService<Core.Translation.ITranslationService>(),
            App.Services.GetRequiredService<AppState>(),
            fake);

        await viewModel.CheckUpdateCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasUpdate);
        Assert.False(viewModel.IsUpdateError);
        Assert.Contains("v0.2.0", viewModel.UpdateHint);
        Assert.False(viewModel.IsCheckingUpdate);
    }

    [AvaloniaFact]
    public async Task Checking_from_the_settings_page_shows_why_it_could_not_ask()
    {
        var viewModel = new SettingsPageViewModel(
            App.Services.GetRequiredService<Core.Translation.ITranslationService>(),
            App.Services.GetRequiredService<AppState>(),
            new FakeChecker(UpdateCheck.Failed(SemanticVersion.Parse("0.1.0"), UpdateFailure.Network)));

        await viewModel.CheckUpdateCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasUpdate);
        Assert.True(viewModel.IsUpdateError);
        Assert.Contains("连不上 GitHub", viewModel.UpdateHint);
    }

    [AvaloniaFact]
    public void The_settings_page_shows_a_check_for_updates_row()
    {
        var window = new Views.MainWindow { DataContext = App.Services.GetRequiredService<MainViewModel>() };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var main = Assert.IsType<MainViewModel>(window.DataContext);
        main.SelectedKey = "settings";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var page = Assert.IsType<Views.Pages.SettingsPage>(main.CurrentPageView);
        var labels = page.GetVisualDescendants().OfType<Avalonia.Controls.Button>()
            .Select(Avalonia.Automation.AutomationProperties.GetName)
            .ToList();

        Assert.Contains("检查更新", labels);
    }

    // ---------- helpers ----------

    private static GitHubUpdateChecker Checker(
        string? json,
        out StubHandler handler,
        HttpStatusCode status = HttpStatusCode.OK,
        bool rateLimited = false)
    {
        handler = new StubHandler(request =>
        {
            var response = new HttpResponseMessage(status);
            if (json is not null)
            {
                response.Content = new StringContent(json);
            }

            if (rateLimited)
            {
                response.Headers.Add("X-RateLimit-Remaining", "0");
            }

            return response;
        });
        return new GitHubUpdateChecker(new HttpClient(handler), SemanticVersion.Parse("0.1.0"));
    }

    private static string Release(
        string tag,
        bool prerelease = false,
        bool draft = false,
        string[]? assets = null)
    {
        var files = assets ??
        [
            $"HoverTrans-{tag.TrimStart('v')}-win-x64-setup.exe"
        ];

        var assetJson = string.Join(",", files.Select(name =>
            $"{{\"name\":\"{name}\",\"browser_download_url\":\"https://github.com/guoxdxdxd/HoverTrans/releases/download/{tag}/{name}\"}}"));

        return $$"""
                 {
                   "tag_name": "{{tag}}",
                   "name": "悬译 {{tag}}",
                   "body": "测试用发布说明",
                   "html_url": "https://github.com/guoxdxdxd/HoverTrans/releases/tag/{{tag}}",
                   "draft": {{(draft ? "true" : "false")}},
                   "prerelease": {{(prerelease ? "true" : "false")}},
                   "published_at": "2026-11-24T09:00:00Z",
                   "assets": [{{assetJson}}]
                 }
                 """;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("no network in this test");
    }

    private sealed class FakeChecker : IUpdateChecker
    {
        private readonly UpdateCheck _result;

        public FakeChecker(UpdateCheck result) => _result = result;

        public Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }
}

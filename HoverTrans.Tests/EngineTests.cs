using HoverTrans.Core.Model;
using HoverTrans.Core.Translation;
using HoverTrans.Services;
using HoverTrans.Settings;
using HoverTrans.ViewModels;
using HoverTrans.Views;
using HoverTrans.Views.Pages;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoverTrans.Tests;

/// <summary>
/// The in-process engine and the pieces around it. Nothing here loads the 1.3 GB of weights except the
/// one end-to-end test, which stays off unless HOVERTRANS_SLOW_TESTS=1.
/// </summary>
public class EngineTests
{
    [Fact]
    public void Visible_text_never_shows_a_half_character()
    {
        var visible = new VisibleText();

        // A token that ends in the middle of a UTF-8 sequence decodes to a trailing U+FFFD, which is held back.
        Assert.Equal("ご", visible.Update("ご\uFFFD"));

        // The next token completes it, and the whole text is shown.
        Assert.Equal("ござ", visible.Update("ござ"));

        // A completed text stays put.
        Assert.Equal("ござ", visible.Update("ござ"));
    }

    [Fact]
    public void Visible_text_is_the_growing_text_not_an_accumulated_delta()
    {
        var visible = new VisibleText();

        Assert.Equal("你好", visible.Update("你好"));
        Assert.Equal("你好，世界", visible.Update("你好，世界"));
        Assert.Equal("你好，世界", visible.Value);

        // Decoding starts from scratch every time, so a shorter result replaces the longer one instead of
        // being appended to it: the caller shows this text as-is.
        Assert.Equal("你好", visible.Update("你好"));
    }

    [Theory]
    [InlineData("auto", "auto")]
    [InlineData("AUTO", "auto")]
    [InlineData(" cpu ", "cpu")]
    [InlineData("Vulkan", "vulkan")]
    [InlineData("", "auto")]
    [InlineData(null, "auto")]
    [InlineData("cuda", "auto")]
    public void Backend_preference_reaches_the_library_in_its_own_spelling(string? configured, string expected)
    {
        var options = new HunyuanEngineOptions(Backend: configured!);

        Assert.Equal(expected, options.BackendPreference);
    }

    [Fact]
    public void A_fresh_engine_has_loaded_nothing()
    {
        var engine = new HunyuanEngine(new HunyuanEngineOptions(Backend: "cpu"));

        Assert.Equal(EngineState.NotLoaded, engine.State);
        Assert.False(engine.IsLoaded);
        Assert.Null(engine.LastError);
    }

    [Fact]
    public void Status_text_explains_that_the_model_loads_on_the_first_translation()
    {
        var engine = new HunyuanEngine(new HunyuanEngineOptions(Backend: "cpu"));
        var service = new TranslationService(engine);

        Assert.False(service.IsModelLoaded);
        Assert.Contains("首次翻译时加载", service.StatusText);
        Assert.Contains("Hy-MT3", HunyuanEngine.ModelId);
    }

    /// <summary>
    /// The settings page offers a backend choice instead of the llama.cpp era controls (paths, port,
    /// idle minutes, stop button). Those are gone with the external process, so the page must not
    /// resurrect them — a leftover row would silently write settings nothing reads.
    /// </summary>
    [AvaloniaFact]
    public void The_settings_page_has_the_backend_row_and_no_process_rows()
    {
        var window = new MainWindow { DataContext = App.Services.GetRequiredService<MainViewModel>() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var main = Assert.IsType<MainViewModel>(window.DataContext);
        main.SelectedKey = "settings";
        Dispatcher.UIThread.RunJobs();

        var page = Assert.IsType<SettingsPage>(main.CurrentPageView);
        var texts = page.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
            .ToList();

        Assert.Contains("推理后端", texts);
        Assert.Contains("闲置释放内存", texts);
        Assert.Contains("模型状态", texts);
        Assert.Contains("自动", texts);
        Assert.Contains("仅 CPU", texts);

        var buttons = page.GetVisualDescendants()
            .OfType<Button>()
            .Select(Avalonia.Automation.AutomationProperties.GetName)
            .ToList();
        Assert.Contains("立即释放模型内存", buttons);

        Assert.DoesNotContain("llama-server 路径", texts);
        Assert.DoesNotContain("端口", texts);
        Assert.DoesNotContain("停止模型", texts);
    }

    [Fact]
    public void An_idle_timeout_is_optional_and_zero_means_never()
    {
        // No timeout at all: the weights stay for the life of the process.
        Assert.Null(new HunyuanEngineOptions().IdleUnloadValue);
        Assert.Null(new HunyuanEngineOptions(IdleUnload: TimeSpan.Zero).IdleUnloadValue);
        Assert.Null(new HunyuanEngineOptions(IdleUnload: TimeSpan.FromSeconds(-1)).IdleUnloadValue);

        // Ten minutes of no translation releases ~3 GB of private memory, so it is the default.
        Assert.Equal(TimeSpan.FromMinutes(10), new HunyuanEngineOptions(IdleUnload: TimeSpan.FromMinutes(10)).IdleUnloadValue);
    }

    [Theory]
    [InlineData(400, 30)]  // a quarter of the timeout, capped at 30 s
    [InlineData(120, 30)]
    [InlineData(60, 15)]
    [InlineData(8, 2)]
    [InlineData(4, 1)]     // floor of 1 s, so a short timeout still fires soon
    public void The_idle_check_runs_a_quarter_as_often_as_the_timeout(int timeoutSeconds, int expectedSeconds)
    {
        var options = new HunyuanEngineOptions(IdleUnload: TimeSpan.FromSeconds(timeoutSeconds));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.IdleCheckInterval);
    }

    [Fact]
    public void An_engine_that_never_releases_still_polls_on_a_slow_tick()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new HunyuanEngineOptions().IdleCheckInterval);
    }

    [Fact]
    public void The_status_line_says_when_memory_is_released()
    {
        var withTimeout = new TranslationService(new HunyuanEngine(
            new HunyuanEngineOptions(Backend: "cpu", IdleUnload: TimeSpan.FromMinutes(15))));
        Assert.Contains("闲置 15 分钟后释放内存", withTimeout.StatusText);

        var alwaysLoaded = new TranslationService(new HunyuanEngine(new HunyuanEngineOptions(Backend: "cpu")));
        Assert.Contains("之后常驻", alwaysLoaded.StatusText);
    }

    [Fact]
    public async Task Releasing_a_model_that_was_never_loaded_does_nothing()
    {
        await using var engine = new HunyuanEngine(new HunyuanEngineOptions(Backend: "cpu"));

        await engine.UnloadAsync();

        Assert.Equal(EngineState.NotLoaded, engine.State);
        Assert.False(engine.IsLoaded);
    }

    /// <summary>
    /// A fresh engine has loaded nothing, so this only checks the wiring the settings page uses: the
    /// release command reaches the service and reports what happened.
    /// </summary>
    [AvaloniaFact]
    public async Task The_settings_page_can_release_the_model()
    {
        var viewModel = new SettingsPageViewModel(
            App.Services.GetRequiredService<ITranslationService>(),
            App.Services.GetRequiredService<AppState>(),
            App.Services.GetRequiredService<Core.Updates.IUpdateChecker>());

        await viewModel.UnloadModelCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSaveError);
        Assert.Contains("释放模型内存", viewModel.SaveStatus);
    }

    /// <summary>The container is the only place the setting reaches the engine, so it needs its own test.</summary>
    [AvaloniaFact]
    public void The_engine_gets_the_idle_timeout_from_the_settings()
    {
        var settings = AppSettings.Current;
        var original = settings.IdleUnloadMinutes;
        try
        {
            settings.IdleUnloadMinutes = 15;
            var engine = AppServices.Build().GetRequiredService<HunyuanEngine>();
            Assert.Equal(TimeSpan.FromMinutes(15), engine.IdleUnload);

            // 0 in the settings means "keep it loaded", not "release immediately".
            settings.IdleUnloadMinutes = 0;
            var resident = AppServices.Build().GetRequiredService<HunyuanEngine>();
            Assert.Null(resident.IdleUnload);
        }
        finally
        {
            settings.IdleUnloadMinutes = original;
            settings.Save();
        }
    }

    [Fact]
    public async Task Translate_rejects_a_blank_text_before_loading_anything()
    {
        var engine = new HunyuanEngine(new HunyuanEngineOptions(Backend: "cpu"));
        var service = new TranslationService(engine);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.TranslateAsync(new TranslationRequest("  ", null, "zh-CN"), progress: null, CancellationToken.None));

        // The guard runs before the weights are read: a bad request must not cost 4 GB of memory.
        Assert.Equal(EngineState.NotLoaded, engine.State);
    }

    /// <summary>
    /// The real thing: load the shipped weights, translate one sentence and stream it. It takes a few
    /// seconds and about 4 GB of memory, so it runs only when asked:
    /// <c>$env:HOVERTRANS_SLOW_TESTS=1; dotnet test --filter FullyQualifiedName~The_model_translates</c>
    /// </summary>
    [Fact]
    public async Task The_model_translates_a_sentence_end_to_end()
    {
        if (Environment.GetEnvironmentVariable("HOVERTRANS_SLOW_TESTS") != "1")
        {
            Assert.Skip("需要加载 1.3 GB 权重：设 HOVERTRANS_SLOW_TESTS=1 再跑。");
        }

        await using var engine = new HunyuanEngine(new HunyuanEngineOptions(Backend: "cpu"));
        var service = new TranslationService(engine);
        var progress = new RecordingProgress();

        await service.TranslateAsync(
            new TranslationRequest("Hello world", null, "zh-CN"),
            progress,
            TestContext.Current.CancellationToken);

        Assert.True(engine.IsLoaded);
        Assert.Contains("模型已加载", service.StatusText);
        Assert.Contains("你好", progress.Final);
        Assert.True(progress.Deltas > 0);
    }

    /// <summary>
    /// Releasing the model must actually give the memory back and the next translation must still work.
    /// Slow, so it is skipped unless HOVERTRANS_SLOW_TESTS=1.
    /// </summary>
    [Fact]
    public async Task Releasing_the_model_returns_the_memory_and_a_reload_still_translates()
    {
        if (Environment.GetEnvironmentVariable("HOVERTRANS_SLOW_TESTS") != "1")
        {
            Assert.Skip("需要加载 1.3 GB 权重：设 HOVERTRANS_SLOW_TESTS=1 再跑。");
        }

        await using var engine = new HunyuanEngine(new HunyuanEngineOptions(Backend: "cpu"));
        var service = new TranslationService(engine);
        var cancellationToken = TestContext.Current.CancellationToken;

        await service.TranslateAsync(
            new TranslationRequest("Hello world", null, "zh-CN"),
            new RecordingProgress(),
            cancellationToken);

        var loaded = PrivateMb();
        await service.UnloadModelAsync(cancellationToken);
        var released = PrivateMb();

        Assert.False(service.IsModelLoaded);
        Assert.Equal(EngineState.NotLoaded, engine.State);
        // The weights are native arenas: disposing them drops private bytes by gigabytes, not megabytes.
        Assert.True(loaded - released > 1024, $"private memory went from {loaded} MB to {released} MB");

        // The assemblies stay on disk, so the next translation loads them again and works.
        var progress = new RecordingProgress();
        await service.TranslateAsync(
            new TranslationRequest("Good night", null, "zh-CN"),
            progress,
            cancellationToken);

        Assert.True(engine.IsLoaded);
        Assert.False(string.IsNullOrWhiteSpace(progress.Final));
    }

    /// <summary>
    /// The automatic part: with a short timeout the weights are released on their own, without a request.
    /// Slow (it loads twice), so it is skipped unless HOVERTRANS_SLOW_TESTS=1.
    /// </summary>
    [Fact]
    public async Task An_idle_engine_releases_itself()
    {
        if (Environment.GetEnvironmentVariable("HOVERTRANS_SLOW_TESTS") != "1")
        {
            Assert.Skip("需要加载 1.3 GB 权重：设 HOVERTRANS_SLOW_TESTS=1 再跑。");
        }

        await using var engine = new HunyuanEngine(
            new HunyuanEngineOptions(Backend: "cpu", IdleUnload: TimeSpan.FromSeconds(2)));
        var service = new TranslationService(engine);
        var cancellationToken = TestContext.Current.CancellationToken;

        await service.TranslateAsync(
            new TranslationRequest("Hello world", null, "zh-CN"),
            new RecordingProgress(),
            cancellationToken);
        Assert.True(engine.IsLoaded);

        // The loop checks every second with a 2 s timeout; give it plenty of slack for a loaded machine.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (service.IsModelLoaded && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(500, cancellationToken);
        }

        Assert.False(service.IsModelLoaded);
        Assert.Equal(EngineState.NotLoaded, engine.State);
    }

    private static long PrivateMb()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        process.Refresh();
        return process.PrivateMemorySize64 / 1024 / 1024;
    }

    private sealed class RecordingProgress : IProgress<TranslationProgress>
    {
        public string Final { get; private set; } = string.Empty;

        public int Deltas { get; private set; }

        public void Report(TranslationProgress value)
        {
            switch (value.Phase)
            {
                case TranslationPhase.Delta:
                    Deltas++;
                    break;
                case TranslationPhase.Completed:
                    Final = value.Text;
                    break;
            }
        }
    }
}

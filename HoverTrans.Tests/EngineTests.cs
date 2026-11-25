using HoverTrans.Core.Model;
using HoverTrans.Core.Translation;
using HoverTrans.Services;
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
        Assert.Contains("模型状态", texts);
        Assert.Contains("自动", texts);
        Assert.Contains("仅 CPU", texts);

        Assert.DoesNotContain("闲置退出", texts);
        Assert.DoesNotContain("llama-server 路径", texts);
        Assert.DoesNotContain("端口", texts);
        Assert.DoesNotContain("停止模型", texts);
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

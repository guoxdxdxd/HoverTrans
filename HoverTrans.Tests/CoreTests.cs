using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HoverTrans.Core.Languages;
using HoverTrans.Core.Model;
using HoverTrans.Core.Platform;
using HoverTrans.Core.Translation;
using HoverTrans.Services;
using HoverTrans.ViewModels;
using HoverTrans.Views;
using Xunit;

namespace HoverTrans.Tests;

public class CoreTests
{
    [Theory]
    [InlineData("Hello world", true)]
    [InlineData("你好，世界", false)]
    [InlineData("こんにちは", true)]
    [InlineData("안녕하세요", true)]
    [InlineData("Bonjour, ça va ?", true)]
    [InlineData("12345", false)]
    [InlineData("Hello 世界", true)]
    public void Hover_detector_only_translates_non_chinese(string text, bool expected) =>
        Assert.Equal(expected, LanguageDetector.ShouldTranslateToChinese(text));

    [Theory]
    [InlineData("Hello world", "en")]
    [InlineData("你好，世界", "zh-CN")]
    [InlineData("こんにちは", "ja")]
    [InlineData("안녕하세요", "ko")]
    [InlineData("Привет", "ru")]
    [InlineData("مرحبا", "ar")]
    public void Detector_finds_the_script(string text, string expected) =>
        Assert.Equal(expected, LanguageDetector.Detect(text));

    [Fact]
    public void Short_text_is_one_chunk()
    {
        var chunks = TextChunker.Split("Hello", 100);
        Assert.Single(chunks);
        Assert.Equal("Hello", chunks[0]);
    }

    [Fact]
    public void Long_text_is_split_without_exceeding_the_limit()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 2000));
        var chunks = TextChunker.Split(text, 500);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 500));
    }

    [Fact]
    public void Prompt_names_the_target_language()
    {
        var prompt = TranslationPrompt.Build("Hello", null, "zh-CN");
        Assert.Contains("Simplified Chinese", prompt);
        Assert.Contains("Hello", prompt);
    }

    [Fact]
    public void Model_defaults_to_ten_minutes_idle()
    {
        var options = new LlamaServerOptions("server", "model");
        Assert.Equal(TimeSpan.FromMinutes(10), options.IdleTimeoutValue);
    }

    [AvaloniaFact]
    public async Task Translate_command_streams_the_result_into_the_view_model()
    {
        var viewModel = new MainViewModel(
            new FakeTranslationService(),
            new FakeOcrService(),
            new FakeHoverService())
        {
            SourceText = "Hello"
        };

        await viewModel.TranslateCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("你好", viewModel.ResultText);
        Assert.False(viewModel.IsBusy);
        Assert.Equal("翻译完成", viewModel.StatusText);
    }

    [AvaloniaFact]
    public void Hover_window_renders_with_a_snapshot()
    {
        var window = new HoverWindow();
        window.Show();
        window.Apply(new HoverSnapshot("Hello", "你好", "翻译完成", false, false, 100, 100));
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);
        window.Close();
    }

    private sealed class FakeTranslationService : ITranslationService
    {
        public string StatusText => "测试模型";

        public bool IsModelRunning => true;

        public Task TranslateAsync(
            TranslationRequest request,
            IProgress<TranslationProgress>? progress,
            CancellationToken cancellationToken)
        {
            Assert.Equal("zh-CN", request.TargetLanguage);
            progress?.Report(TranslationProgress.Delta("你好"));
            progress?.Report(TranslationProgress.Completed("你好"));
            return Task.CompletedTask;
        }

        public Task StopModelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeOcrService : IOcrService
    {
        public bool IsSupported => true;

        public string UnsupportedMessage => string.Empty;

        public Task<string> RecognizeAsync(byte[] image, CancellationToken cancellationToken) =>
            Task.FromResult("Hello");
    }

    private sealed class FakeHoverService : IHoverService
    {
        public bool IsRunning => false;

        public bool IsSupported => true;

        public string UnsupportedMessage => string.Empty;

        public event EventHandler<HoverSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public void Start()
        {
        }

        public Task StopAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
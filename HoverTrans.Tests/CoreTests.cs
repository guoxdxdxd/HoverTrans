using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HoverTrans.Core.History;
using HoverTrans.Core.Languages;
using HoverTrans.Core.Model;
using HoverTrans.Core.Platform;
using HoverTrans.Core.Translation;
using HoverTrans.Services;
using HoverTrans.Settings;
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
    public void Prompt_asks_for_the_translation_alone_in_the_literal_mode()
    {
        var prompt = TranslationPrompt.Build("Hello", null, "zh-CN");

        Assert.StartsWith("Translate the following text into Simplified Chinese.", prompt);
        Assert.Contains("only output the translated result without any additional explanation", prompt);
        Assert.DoesNotContain("style must strictly conform to", prompt);
        Assert.EndsWith("\n\nHello", prompt);
    }

    [Fact]
    public void Prompt_states_the_style_in_the_free_translation_mode()
    {
        var prompt = TranslationPrompt.Build("That's sick, bro.", null, "zh-CN", TranslationStyle.Natural);

        Assert.StartsWith("Translate the following text into Simplified Chinese.", prompt);
        Assert.Contains("translation style must strictly conform to [", prompt);
        Assert.Contains("slang", prompt);
        Assert.Contains("only output the translated result without any additional explanation", prompt);
    }

    [Fact]
    public void Prompt_names_the_source_language_when_one_is_given()
    {
        var prompt = TranslationPrompt.Build("Hola", "es", "zh-CN");

        Assert.StartsWith("Translate the following Spanish text into Simplified Chinese.", prompt);
    }

    [Fact]
    public void Settings_from_the_llama_cpp_era_still_load()
    {
        // People upgrading have settings.json with fields this version no longer knows (paths, port,
        // idle minutes). Unknown fields are ignored, so nothing is lost and nothing crashes.
        const string json = """
            {
              "ServerPath": "D:\\HoverTrans\\runtime\\llama-server.exe",
              "ModelPath": "D:\\HoverTrans\\models\\Hy-MT2-1.8B-Q4_K_M.gguf",
              "Port": 18080,
              "IdleMinutes": 10,
              "HoverEnabled": true,
              "TargetLanguage": "ja"
            }
            """;

        var settings = AppSettings.Load(new MemorySettingsStore { Json = json });

        Assert.Equal("auto", settings.Backend);
        Assert.True(settings.HoverEnabled);
        Assert.Equal("ja", settings.TargetLanguage);
        Assert.Equal(550, settings.HoverDebounceMilliseconds);
    }

    [AvaloniaFact]
    public async Task Translate_command_streams_the_result_into_the_view_model()
    {
        var store = NewHistoryStore();
        var viewModel = new TranslatePageViewModel(
            new FakeTranslationService(),
            new FakeOcrService(),
            store)
        {
            SourceText = "Hello"
        };

        await viewModel.TranslateCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("你好", viewModel.ResultText);
        Assert.False(viewModel.IsBusy);
        Assert.Equal("翻译完成", viewModel.StatusText);

        // A finished translation is kept for the history page.
        var entry = Assert.Single(store.Load());
        Assert.Equal("Hello", entry.SourceText);
        Assert.Equal("你好", entry.ResultText);
    }

    /// <summary>The 意译 checkbox has to change what the pipeline is asked for, not only what the checkbox shows.</summary>
    [AvaloniaFact]
    public async Task Free_translation_choice_reaches_the_translation_request()
    {
        var translation = new FakeTranslationService();
        var viewModel = new TranslatePageViewModel(translation, new FakeOcrService(), NewHistoryStore())
        {
            SourceText = "That's sick, bro."
        };

        // Default: word for word, so nothing changes for anyone who ignores the checkbox.
        await viewModel.TranslateCommand.ExecuteAsync(null);
        Assert.Equal(TranslationStyle.Literal, translation.LastRequest!.Style);

        viewModel.IsFreeTranslation = true;
        await viewModel.TranslateCommand.ExecuteAsync(null);
        Assert.Equal(TranslationStyle.Natural, translation.LastRequest!.Style);
    }

    [Fact]
    public void History_keeps_the_newest_first_and_replaces_the_same_translation()
    {
        var store = NewHistoryStore();
        store.Append(Entry("one", "一", DateTimeOffset.Now.AddMinutes(-5)));
        store.Append(Entry("two", "二", DateTimeOffset.Now));
        store.Append(Entry("one", "一（改）", DateTimeOffset.Now));

        var entries = store.Load();

        Assert.Equal(2, entries.Count);
        Assert.Equal("one", entries[0].SourceText);
        Assert.Equal("一（改）", entries[0].ResultText);
        Assert.Equal("two", entries[1].SourceText);
    }

    [Fact]
    public void History_survives_a_reload_and_clears()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hovertrans-history-{Guid.NewGuid():N}.json");
        try
        {
            var store = new HistoryStore(path);
            store.Append(Entry("Hello", "你好", DateTimeOffset.Now));

            var reloaded = new HistoryStore(path);
            Assert.Single(reloaded.Load());

            reloaded.Clear();
            Assert.Empty(new HistoryStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static HistoryStore NewHistoryStore() =>
        new(Path.Combine(Path.GetTempPath(), $"hovertrans-history-{Guid.NewGuid():N}.json"));

    private static HistoryEntry Entry(string source, string result, DateTimeOffset at) => new()
    {
        SourceText = source,
        ResultText = result,
        SourceLanguage = "en",
        TargetLanguage = "zh-CN",
        CreatedAt = at
    };

    [AvaloniaFact]
    public void Hover_window_renders_with_a_snapshot()
    {
        var window = new HoverWindow();
        window.Show();
        window.Apply(new HoverSnapshot("Hello", "你好", "翻译完成", false, false, 100, 100));
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);

        // The translation is selectable text, so a part of it can be copied. Not a TextBox: Arc themes no TextBox, and
        // an untemplated one renders nothing at all (that is what made the popup look empty).
        var translation = window.GetVisualDescendants().OfType<SelectableTextBlock>().Single();
        Assert.Equal("你好", translation.Text);
        translation.SelectAll();
        Assert.Equal("你好", translation.SelectedText);

        window.Close();
    }

    [AvaloniaFact]
    public void Hover_window_closes_and_asks_for_the_main_window()
    {
        var window = new HoverWindow();
        window.Show();
        window.Apply(new HoverSnapshot("Hello", "你好", "翻译完成", false, false, 100, 100));
        Dispatcher.UIThread.RunJobs();

        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
        var close = buttons.Single(button => AutomationProperties.GetName(button) == "关闭");
        var open = buttons.Single(button => AutomationProperties.GetName(button) == "打开主窗口");

        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(window.IsVisible);

        var mainWindowAsked = false;
        window.OpenMainRequested += (_, _) => mainWindowAsked = true;
        window.Show();
        open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(mainWindowAsked);
        Assert.False(window.IsVisible);

        window.Close();
    }

    private sealed class FakeTranslationService : ITranslationService
    {
        public string StatusText => "测试模型";

        public bool IsModelLoaded => true;

        /// <summary>The last request the page sent, so tests can check what it asked for.</summary>
        public TranslationRequest? LastRequest { get; private set; }

        public Task TranslateAsync(
            TranslationRequest request,
            IProgress<TranslationProgress>? progress,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            Assert.Equal("zh-CN", request.TargetLanguage);
            progress?.Report(TranslationProgress.Delta("你好"));
            progress?.Report(TranslationProgress.Completed("你好"));
            return Task.CompletedTask;
        }

        public Task UnloadModelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
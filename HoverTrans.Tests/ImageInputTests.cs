using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HoverTrans.Core.History;
using HoverTrans.Core.Platform;
using HoverTrans.Core.Translation;
using HoverTrans.ViewModels;
using Xunit;

namespace HoverTrans.Tests;

/// <summary>An image pasted or dropped on the page must start translating by itself, not wait for a second key.</summary>
public class ImageInputTests
{
    /// <summary>A 1x1 PNG: the smallest picture the view model will accept.</summary>
    private static readonly byte[] Pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [AvaloniaFact]
    public void An_image_translates_as_soon_as_it_arrives()
    {
        var viewModel = new TranslatePageViewModel(new RecordingTranslation(), new ReadsHello(), NewHistoryStore());

        viewModel.SetImage(Pixel);
        Pump(() => viewModel.ResultText.Length > 0);

        Assert.Equal("Hello", viewModel.SourceText);
        Assert.Equal("你好", viewModel.ResultText);
        Assert.Equal("翻译完成", viewModel.StatusText);
        Assert.False(viewModel.IsBusy);

        // The preview is only there while it is being read: the recognized text takes over.
        Assert.False(viewModel.HasImage);
    }

    [AvaloniaFact]
    public void An_empty_picture_is_ignored()
    {
        var viewModel = new TranslatePageViewModel(new RecordingTranslation(), new ReadsHello(), NewHistoryStore());

        viewModel.SetImage([]);
        Pump(() => viewModel.IsBusy);

        Assert.False(viewModel.HasImage);
        Assert.Empty(viewModel.SourceText);
        Assert.Empty(viewModel.ResultText);
    }

    private static void Pump(Func<bool> done, int rounds = 200)
    {
        for (var i = 0; i < rounds && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static HistoryStore NewHistoryStore() =>
        new(Path.Combine(Path.GetTempPath(), $"hovertrans-history-{Guid.NewGuid():N}.json"));

    private sealed class ReadsHello : IOcrService
    {
        public bool IsSupported => true;

        public string UnsupportedMessage => string.Empty;

        public Task<string> RecognizeAsync(byte[] image, CancellationToken cancellationToken) => Task.FromResult("Hello");
    }

    private sealed class RecordingTranslation : ITranslationService
    {
        public string StatusText => "测试模型";

        public bool IsModelRunning => true;

        public Task TranslateAsync(
            TranslationRequest request,
            IProgress<TranslationProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(TranslationProgress.Completed("你好"));
            return Task.CompletedTask;
        }

        public Task StopModelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

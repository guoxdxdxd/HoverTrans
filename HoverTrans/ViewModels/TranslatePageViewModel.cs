using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoverTrans.Core.History;
using HoverTrans.Core.Languages;
using HoverTrans.Core.Platform;
using HoverTrans.Core.Translation;
using HoverTrans.Settings;

namespace HoverTrans.ViewModels;

/// <summary>
/// The translate page: text or an image in, a streamed translation out, one language pair to choose and one action
/// that matters. The model lifecycle and the hover loop live in services; this only drives them.
/// </summary>
public sealed partial class TranslatePageViewModel : ObservableObject, IDisposable
{
    private readonly ITranslationService _translation;
    private readonly IOcrService _ocr;
    private readonly HistoryStore _history;
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _translationCts;
    private byte[]? _imageBytes;
    private TimeSpan? _elapsed;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSource))]
    [NotifyPropertyChangedFor(nameof(CharacterCountText))]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(IsWaitingFirstToken))]
    [NotifyPropertyChangedFor(nameof(IsResultEmpty))]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private string _statusText = "就绪：粘贴或输入要翻译的文字，也可以拖入一张图片。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingFirstToken))]
    [NotifyPropertyChangedFor(nameof(IsResultEmpty))]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private string _sourceLanguageCode = LanguageCatalog.AutoCode;

    [ObservableProperty]
    private string _targetLanguageCode = "zh-CN";

    [ObservableProperty]
    private Bitmap? _imagePreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSource))]
    private bool _hasImage;

    [ObservableProperty]
    private string _imageName = string.Empty;

    [ObservableProperty]
    private string _elapsedText = string.Empty;

    public TranslatePageViewModel(ITranslationService translation, IOcrService ocr, HistoryStore history)
    {
        _translation = translation;
        _ocr = ocr;
        _history = history;

        var settings = AppSettings.Current;
        _sourceLanguageCode = NormalizeSourceCode(settings.SourceLanguage);
        _targetLanguageCode = NormalizeTargetCode(settings.TargetLanguage);
    }

    /// <summary>Source languages: "auto" first, then every language the model handles. The page lists them in XAML so
    /// the select needs no reflection; this is the same list, for code that needs the names.</summary>
    public IReadOnlyList<LanguageOption> SourceLanguages => LanguageCatalog.Sources;

    /// <summary>Target languages.</summary>
    public IReadOnlyList<LanguageOption> TargetLanguages => LanguageCatalog.All;

    /// <summary>Whether there is anything to translate.</summary>
    public bool HasSource => HasImage || !string.IsNullOrWhiteSpace(SourceText);

    /// <summary>Whether a translation is on screen.</summary>
    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);

    /// <summary>True from the moment the model is asked until the first characters arrive: the result shows skeleton lines.</summary>
    public bool IsWaitingFirstToken => IsBusy && !HasResult;

    /// <summary>Whether the result card should explain itself (nothing typed, nothing running, nothing shown).</summary>
    public bool IsResultEmpty => !HasResult && !IsBusy;

    /// <summary>The character count under the source box, empty while there is nothing to count.</summary>
    public string CharacterCountText => SourceText.Length == 0 ? string.Empty : $"{SourceText.Length} 字";

    /// <summary>Whether this build can read text out of images.</summary>
    public bool IsOcrSupported => _ocr.IsSupported;

    /// <summary>What image input does here, or why it cannot be used.</summary>
    public string OcrHint => _ocr.UnsupportedMessage;

    /// <summary>Puts text in the box and translates it at once (the history page's "translate again").</summary>
    public void LoadText(string text)
    {
        SourceText = text;
        ResultText = string.Empty;
        ElapsedText = string.Empty;
        IsError = false;
        StatusText = "已从历史记录载入，正在重新翻译…";
        TranslateCommand.Execute(null);
    }

    /// <summary>Loads an image from the clipboard or a file, shows a preview, and starts translating it.</summary>
    public void SetImage(byte[] bytes, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            return;
        }

        _imageBytes = bytes;
        ImagePreview?.Dispose();
        ImagePreview = new Bitmap(new MemoryStream(bytes));
        HasImage = true;
        ImageName = string.IsNullOrWhiteSpace(name) ? "剪贴板图片" : name;
        SourceText = string.Empty;
        ResultText = string.Empty;
        ElapsedText = string.Empty;
        IsError = false;
        StatusText = "图片已就绪：先识别其中的文字，再翻译。";
        TranslateCommand.NotifyCanExecuteChanged();

        // Pasting or dropping an image is a request to translate it: waiting for a second Ctrl+Enter made the paste
        // look ignored, which is exactly what people reported.
        if (TranslateCommand.CanExecute(null))
        {
            TranslateCommand.Execute(null);
        }
    }

    /// <summary>Loads an image from a dropped file, ignoring anything that is not an image.</summary>
    public async Task SetImageFileAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            SetImage(bytes, System.IO.Path.GetFileName(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            StatusText = "读取图片失败：" + e.Message;
            IsError = true;
        }
    }

    /// <summary>Removes the loaded image.</summary>
    [RelayCommand]
    public void ClearImage()
    {
        _imageBytes = null;
        HasImage = false;
        ImageName = string.Empty;
        ImagePreview?.Dispose();
        ImagePreview = null;
        StatusText = "已移除图片。";
        TranslateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Clears both the source and the result.</summary>
    [RelayCommand]
    private void Clear()
    {
        SourceText = string.Empty;
        ResultText = string.Empty;
        ElapsedText = string.Empty;
        IsError = false;
        StatusText = "就绪：粘贴或输入要翻译的文字，也可以拖入一张图片。";
        ClearImageCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanTranslate))]
    private async Task TranslateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        _translationCts?.Cancel();
        _translationCts?.Dispose();
        _translationCts = new CancellationTokenSource();
        var cancellationToken = _translationCts.Token;

        IsBusy = true;
        IsError = false;
        ResultText = string.Empty;
        ElapsedText = string.Empty;
        StatusText = "准备翻译…";
        _clock.Restart();

        try
        {
            var fromImage = false;
            if (_imageBytes is { Length: > 0 } image)
            {
                StatusText = "正在识别图片文字…";
                if (!_ocr.IsSupported)
                {
                    throw new PlatformNotSupportedException(_ocr.UnsupportedMessage);
                }

                var recognized = await _ocr.RecognizeAsync(image, cancellationToken);
                if (string.IsNullOrWhiteSpace(recognized))
                {
                    StatusText = "图片里没有识别到文字，换一张更清晰的图片试试。";
                    IsError = true;
                    return;
                }

                SourceText = recognized.Trim();
                fromImage = true;
                ClearImageCommand.Execute(null);
                StatusText = "已识别图片文字，正在翻译…";
            }

            if (string.IsNullOrWhiteSpace(SourceText))
            {
                StatusText = "没有可翻译的文字。";
                return;
            }

            var sourceCode = IsAuto(SourceLanguageCode) ? null : SourceLanguageCode;
            var targetCode = TargetLanguageCode;

            SaveLanguageChoice(SourceLanguageCode, targetCode);

            var progress = new TranslationProgressSink(this);
            await _translation.TranslateAsync(
                new TranslationRequest(SourceText.Trim(), sourceCode, targetCode),
                progress,
                cancellationToken);

            // The deltas are posted to the UI thread, so the last of them may still be queued here: take the text the
            // pipeline reported, so the history never misses a translation that has finished.
            ResultText = progress.FinalText;
            _clock.Stop();
            _elapsed = _clock.Elapsed;
            ElapsedText = $"用时 {_elapsed.Value.TotalSeconds:0.0} 秒";
            StatusText = "翻译完成";
            RecordHistory(fromImage);
        }
        catch (OperationCanceledException)
        {
            _clock.Stop();
            StatusText = "已取消";
        }
        catch (Exception ex)
        {
            _clock.Stop();
            IsError = true;
            StatusText = "翻译失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            AppStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Cancel() => _translationCts?.Cancel();

    /// <summary>Swaps the two languages; an auto-detected source becomes the language it detected as the target.</summary>
    [RelayCommand]
    private void SwapLanguages()
    {
        var source = SourceLanguageCode;
        var target = TargetLanguageCode;

        if (IsAuto(source))
        {
            SourceLanguageCode = target;
            TargetLanguageCode = target.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "en" : "zh-CN";
        }
        else
        {
            SourceLanguageCode = target;
            TargetLanguageCode = source;
        }

        SaveLanguageChoice(SourceLanguageCode, TargetLanguageCode);
    }

    /// <summary>Raised when a translation finished, so the shell can refresh the model status.</summary>
    public event EventHandler? AppStateChanged;

    partial void OnSourceTextChanged(string value) => TranslateCommand.NotifyCanExecuteChanged();

    partial void OnSourceLanguageCodeChanged(string value) => SaveLanguageChoice(value, TargetLanguageCode);

    partial void OnTargetLanguageCodeChanged(string value) => SaveLanguageChoice(SourceLanguageCode, value);

    /// <summary>Keeps the finished text while posting every update to the UI thread, in order.</summary>
    private sealed class TranslationProgressSink(TranslatePageViewModel owner) : IProgress<TranslationProgress>
    {
        private readonly object _gate = new();
        private string _final = string.Empty;

        /// <summary>The text of the completion report (thread-safe read).</summary>
        public string FinalText
        {
            get
            {
                lock (_gate)
                {
                    return _final;
                }
            }
        }

        public void Report(TranslationProgress value)
        {
            lock (_gate)
            {
                if (value.Phase == TranslationPhase.Completed)
                {
                    _final = value.Text;
                }
            }

            Dispatcher.UIThread.Post(() => owner.OnProgress(value));
        }
    }

    private void OnProgress(TranslationProgress progress)    {
        switch (progress.Phase)
        {
            case TranslationPhase.StartingModel:
                StatusText = progress.Message ?? "正在启动本地翻译模型…";
                break;
            case TranslationPhase.Translating:
                StatusText = "正在翻译…";
                break;
            case TranslationPhase.Delta:
                ResultText = progress.Text;
                break;
            case TranslationPhase.Completed:
                ResultText = progress.Text;
                StatusText = "翻译完成";
                break;
        }
    }

    private bool CanTranslate() => !IsBusy && HasSource;

    private void RecordHistory(bool fromImage)
    {
        if (string.IsNullOrWhiteSpace(SourceText) || string.IsNullOrWhiteSpace(ResultText))
        {
            return;
        }

        _history.Append(new HistoryEntry
        {
            SourceText = SourceText.Trim(),
            ResultText = ResultText.Trim(),
            SourceLanguage = SourceLanguageCode,
            TargetLanguage = TargetLanguageCode,
            FromImage = fromImage,
            CreatedAt = DateTimeOffset.Now
        });
    }

    private static bool IsAuto(string? code) =>
        string.IsNullOrWhiteSpace(code) || code == LanguageCatalog.AutoCode;

    private static string NormalizeSourceCode(string? code) =>
        IsAuto(code) || LanguageCatalog.Sources.All(language => language.Code != code)
            ? LanguageCatalog.AutoCode
            : code!;

    private static string NormalizeTargetCode(string? code) =>
        LanguageCatalog.All.Any(language => language.Code == code) ? code! : "zh-CN";

    private static void SaveLanguageChoice(string? sourceCode, string targetCode)
    {
        var settings = AppSettings.Current;
        settings.SourceLanguage = IsAuto(sourceCode) ? LanguageCatalog.AutoCode : sourceCode!;
        settings.TargetLanguage = targetCode;
        settings.Save();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _translationCts?.Cancel();
        _translationCts?.Dispose();
        ImagePreview?.Dispose();
    }
}

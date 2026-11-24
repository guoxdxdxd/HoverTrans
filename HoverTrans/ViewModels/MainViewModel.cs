using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoverTrans.Core.Languages;
using HoverTrans.Core.Platform;
using HoverTrans.Core.Translation;
using HoverTrans.Services;
using HoverTrans.Settings;

namespace HoverTrans.ViewModels;

/// <summary>The main window: text/image input, language choice and the translation pipeline.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ITranslationService _translation;
    private readonly IOcrService _ocr;
    private readonly IHoverService _hover;
    private readonly DispatcherTimer _statusTimer;
    private CancellationTokenSource? _translationCts;
    private byte[]? _imageBytes;
    private bool _initialized;
    private bool _disposed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private string _statusText = "就绪：输入文字、粘贴图片，或打开悬停取词。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hoverEnabled;

    [ObservableProperty]
    private int _sourceLanguageIndex;

    [ObservableProperty]
    private int _targetLanguageIndex;

    [ObservableProperty]
    private string _modelStatus = "模型未启动";

    [ObservableProperty]
    private Bitmap? _imagePreview;

    [ObservableProperty]
    private bool _hasImage;

    [ObservableProperty]
    private string _serverPath = string.Empty;

    [ObservableProperty]
    private string _modelPath = string.Empty;

    [ObservableProperty]
    private string _portText = string.Empty;

    [ObservableProperty]
    private string _idleMinutesText = string.Empty;

    public MainViewModel(
        ITranslationService translation,
        IOcrService ocr,
        IHoverService hover)
    {
        _translation = translation;
        _ocr = ocr;
        _hover = hover;

        SourceLanguages = LanguageCatalog.Sources.Select(language => language.DisplayName).ToArray();
        TargetLanguages = LanguageCatalog.All.Select(language => language.DisplayName).ToArray();

        var settings = AppSettings.Current;
        _sourceLanguageIndex = FindSourceIndex(settings.SourceLanguage);
        _targetLanguageIndex = FindTargetIndex(settings.TargetLanguage);
        _serverPath = settings.ServerPath;
        _modelPath = settings.ModelPath;
        _portText = settings.Port.ToString();
        _idleMinutesText = settings.IdleMinutes.ToString();
        _hoverEnabled = settings.HoverEnabled;
        _modelStatus = translation.StatusText;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => ModelStatus = _translation.StatusText;
    }

    public IReadOnlyList<string> SourceLanguages { get; }

    public IReadOnlyList<string> TargetLanguages { get; }

    public string HoverHint => _hover.IsSupported
        ? "把鼠标停在外文上约半秒，自动识别并译为中文。"
        : _hover.UnsupportedMessage;

    public bool IsHoverSupported => _hover.IsSupported;

    public bool IsOcrSupported => _ocr.IsSupported;

    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);

    public string OcrHint => _ocr.IsSupported
        ? "支持 Ctrl+V 粘贴图片，或选择图片文件。"
        : _ocr.UnsupportedMessage;

    /// <summary>Starts the live status timer and restores the hover preference. Call once after the window exists.</summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _statusTimer.Start();
        ModelStatus = _translation.StatusText;
        if (!IsHoverSupported)
        {
            // A greyed-out switch with no explanation reads like a broken toggle: turn a leftover preference off and
            // say why it cannot be used, in the status line the user is already looking at.
            HoverEnabled = false;
            StatusText = _hover.UnsupportedMessage;
        }
        else if (HoverEnabled)
        {
            StartHover();
        }
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
        ResultText = string.Empty;
        StatusText = "准备翻译…";

        try
        {
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
                    return;
                }

                SourceText = recognized.Trim();
                _imageBytes = null;
                StatusText = "已识别图片文字，正在翻译…";
            }

            if (string.IsNullOrWhiteSpace(SourceText))
            {
                StatusText = "没有可翻译的文字。";
                return;
            }

            var sourceCode = SourceLanguageIndex <= 0
                ? null
                : LanguageCatalog.Sources[SourceLanguageIndex].Code;
            var targetCode = LanguageCatalog.All[TargetLanguageIndex].Code;

            SaveLanguageChoice(sourceCode, targetCode);

            var progress = new Progress<TranslationProgress>(OnProgress);
            await _translation.TranslateAsync(
                new TranslationRequest(SourceText.Trim(), sourceCode, targetCode),
                progress,
                cancellationToken);

            StatusText = "翻译完成";
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
        }
        catch (Exception ex)
        {
            StatusText = "翻译失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            ModelStatus = _translation.StatusText;
        }
    }

    [RelayCommand]
    private void Cancel() => _translationCts?.Cancel();

    [RelayCommand]
    private async Task StopModelAsync()
    {
        await _translation.StopModelAsync();
        ModelStatus = _translation.StatusText;
        StatusText = "模型已停止；下一次翻译会自动重新启动。";
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var settings = AppSettings.Current;
        settings.ServerPath = ServerPath.Trim();
        settings.ModelPath = ModelPath.Trim();

        if (int.TryParse(PortText, out var port) && port is >= 1024 and <= 65535)
        {
            settings.Port = port;
        }

        if (int.TryParse(IdleMinutesText, out var idleMinutes))
        {
            settings.IdleMinutes = Math.Clamp(idleMinutes, 1, 120);
        }

        settings.SourceLanguage = SourceLanguageIndex <= 0
            ? LanguageCatalog.AutoCode
            : LanguageCatalog.Sources[SourceLanguageIndex].Code;
        settings.TargetLanguage = LanguageCatalog.All[TargetLanguageIndex].Code;
        settings.Save();

        StatusText = "设置已保存。模型路径、端口或闲置时长的修改在重启应用后生效。";
    }

    /// <summary>Loads an image from the clipboard or a file and shows a preview.</summary>
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
        SourceText = string.Empty;
        ResultText = string.Empty;
        StatusText = name is null
            ? "已粘贴图片，点击「翻译」先识别文字。"
            : $"已载入图片：{name}，点击「翻译」先识别文字。";
        TranslateCommand.NotifyCanExecuteChanged();
    }

    public void ClearImage()
    {
        _imageBytes = null;
        HasImage = false;
        ImagePreview?.Dispose();
        ImagePreview = null;
    }

    partial void OnResultTextChanged(string value) => OnPropertyChanged(nameof(HasResult));

    partial void OnHoverEnabledChanged(bool value)
    {
        var settings = AppSettings.Current;
        settings.HoverEnabled = value;
        settings.Save();

        if (value)
        {
            StartHover();
        }
        else
        {
            _ = _hover.StopAsync();
            StatusText = "悬停取词已关闭。";
        }
    }

    private void StartHover()
    {
        if (!_hover.IsSupported)
        {
            StatusText = _hover.UnsupportedMessage;
            return;
        }

        _hover.Start();
        StatusText = "悬停取词已开启：把鼠标停在外文上。";
    }

    private void OnProgress(TranslationProgress progress)
    {
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

    private bool CanTranslate() =>
        !IsBusy && (_imageBytes is { Length: > 0 } || !string.IsNullOrWhiteSpace(SourceText));

    private static int FindSourceIndex(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code == LanguageCatalog.AutoCode)
        {
            return 0;
        }

        for (var index = 1; index < LanguageCatalog.Sources.Count; index++)
        {
            if (string.Equals(LanguageCatalog.Sources[index].Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private static int FindTargetIndex(string? code)
    {
        for (var index = 0; index < LanguageCatalog.All.Count; index++)
        {
            if (string.Equals(LanguageCatalog.All[index].Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private static void SaveLanguageChoice(string? sourceCode, string targetCode)
    {
        var settings = AppSettings.Current;
        settings.SourceLanguage = sourceCode ?? LanguageCatalog.AutoCode;
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
        _statusTimer.Stop();
        _translationCts?.Cancel();
        _translationCts?.Dispose();
        ImagePreview?.Dispose();
    }
}
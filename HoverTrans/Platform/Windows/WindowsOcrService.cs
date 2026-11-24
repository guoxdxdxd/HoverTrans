#if WINDOWS
using System.Runtime.Versioning;
using HoverTrans.Core.Platform;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace HoverTrans.Platform.Windows;

/// <summary>Windows.Media.Ocr: no extra runtime, uses the OCR language packs installed in Windows.</summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsOcrService : IOcrService
{
    public bool IsSupported => true;

    public string UnsupportedMessage => string.Empty;

    public async Task<string> RecognizeAsync(byte[] image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(image);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync();

        return await RecognizeWithTheBestEngineAsync(bitmap, cancellationToken);
    }

    /// <summary>
    /// Windows has one OCR engine per installed language pack, and an engine only reads what it was trained on: a
    /// Chinese pack reads Chinese and Latin, an English pack reads Latin, neither reads Japanese. Try the person's own
    /// languages first, then everything else installed, and keep the text that came out longest instead of trusting
    /// whichever pack happened to be first: a screenshot of English code or a Japanese page then still gets read.
    /// </summary>
    private static async Task<string> RecognizeWithTheBestEngineAsync(SoftwareBitmap bitmap, CancellationToken cancellationToken)
    {
        var engines = AvailableEngines();
        if (engines.Count == 0)
        {
            throw new InvalidOperationException(
                "Windows 没有可用的 OCR 语言包，请在「设置 → 时间和语言 → 语言」里安装中文或英文的 OCR 组件。");
        }

        var best = string.Empty;
        foreach (var engine in engines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = (await engine.RecognizeAsync(bitmap)).Text?.Trim() ?? string.Empty;
                if (text.Length > best.Length)
                {
                    best = text;
                }
            }
            catch (Exception)
            {
                // One engine refusing this image (encoding, size) must not throw away the others' work.
            }
        }

        return best;
    }

    /// <summary>The person's own OCR languages first, then every other installed pack, without duplicates.</summary>
    private static List<OcrEngine> AvailableEngines()
    {
        var engines = new List<OcrEngine>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(OcrEngine? engine)
        {
            if (engine is not null && seen.Add(engine.RecognizerLanguage.LanguageTag))
            {
                engines.Add(engine);
            }
        }

        try
        {
            Add(OcrEngine.TryCreateFromUserProfileLanguages());
        }
        catch (Exception)
        {
            // No profile languages at all: fall through to the installed packs.
        }

        foreach (var language in OcrEngine.AvailableRecognizerLanguages)
        {
            try
            {
                Add(OcrEngine.TryCreateFromLanguage(language));
            }
            catch (Exception)
            {
                // A pack that cannot be created is simply not used.
            }
        }

        return engines;
    }
}
#endif

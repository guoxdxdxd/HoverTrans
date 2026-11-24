#if WINDOWS
using System.Runtime.Versioning;
using HoverTrans.Core.Platform;
using Windows.Globalization;
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

        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                     ?? throw new InvalidOperationException(
                         "Windows 没有可用的 OCR 语言包，请在「设置 → 时间和语言 → 语言」里安装中文或英文的 OCR 组件。");

        var result = await engine.RecognizeAsync(bitmap);
        return result.Text?.Trim() ?? string.Empty;
    }
}
#endif
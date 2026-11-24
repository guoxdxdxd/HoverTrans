using HoverTrans.Core.Platform;

namespace HoverTrans.Platform;

/// <summary>Used on platforms whose OCR backend has not been implemented yet.</summary>
public sealed class NullOcrService : IOcrService
{
    public bool IsSupported => false;

    public string UnsupportedMessage =>
        "当前平台的图片文字识别尚未实现：Windows 版使用 Windows.Media.Ocr，macOS 版计划使用 Vision。";

    public Task<string> RecognizeAsync(byte[] image, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException(UnsupportedMessage);
}
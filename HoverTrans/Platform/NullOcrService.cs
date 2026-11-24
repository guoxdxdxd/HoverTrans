using HoverTrans.Core.Platform;

namespace HoverTrans.Platform;

/// <summary>Used on platforms whose OCR backend has not been implemented yet.</summary>
public sealed class NullOcrService : IOcrService
{
    public bool IsSupported => false;

    public string UnsupportedMessage => OperatingSystem.IsWindows()
        ? "这个构建没有带 Windows 图片识别（Windows.Media.Ocr）。请运行 net11.0-windows10.0.19041.0 目标：dotnet run --project HoverTrans -f net11.0-windows10.0.19041.0"
        : "当前平台的图片文字识别尚未实现：Windows 版使用 Windows.Media.Ocr，macOS 版计划使用 Vision。";

    public Task<string> RecognizeAsync(byte[] image, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException(UnsupportedMessage);
}
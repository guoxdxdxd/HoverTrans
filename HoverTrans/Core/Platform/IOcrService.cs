namespace HoverTrans.Core.Platform;

/// <summary>Turns an image into text. The Windows build uses Windows.Media.Ocr.</summary>
public interface IOcrService
{
    bool IsSupported { get; }

    string UnsupportedMessage { get; }

    Task<string> RecognizeAsync(byte[] image, CancellationToken cancellationToken);
}
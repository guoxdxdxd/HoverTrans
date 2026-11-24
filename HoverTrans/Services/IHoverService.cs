namespace HoverTrans.Services;

/// <summary>Polls the control under the mouse and translates non-Chinese text in the background.</summary>
public interface IHoverService : IAsyncDisposable
{
    bool IsRunning { get; }

    bool IsSupported { get; }

    string UnsupportedMessage { get; }

    event EventHandler<HoverSnapshot>? SnapshotChanged;

    void Start();

    Task StopAsync();
}
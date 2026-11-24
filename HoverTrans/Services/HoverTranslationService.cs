using HoverTrans.Core.Languages;
using HoverTrans.Core.Platform;
using HoverTrans.Core.Translation;
using HoverTrans.Settings;

namespace HoverTrans.Services;

/// <summary>
/// Polls <see cref="IHoverTextSource"/>, waits for the text to be stable, and translates it to Chinese.
/// Runs on a background task; consumers must marshal <see cref="SnapshotChanged"/> to the UI thread.
/// </summary>
public sealed class HoverTranslationService : IHoverService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DuplicateSuppression = TimeSpan.FromSeconds(30);

    private readonly ITranslationService _translation;
    private readonly IHoverTextSource _source;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _disposed;

    public HoverTranslationService(ITranslationService translation, IHoverTextSource source)
    {
        _translation = translation;
        _source = source;
    }

    public event EventHandler<HoverSnapshot>? SnapshotChanged;

    public bool IsRunning => _loop is { IsCompleted: false };

    public bool IsSupported => _source.IsSupported;

    public string UnsupportedMessage => _source.UnsupportedMessage;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        _loop = null;
        _cts?.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        string? candidate = null;
        DateTimeOffset candidateSince = default;
        string? lastTranslated = null;
        DateTimeOffset lastTranslatedAt = default;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);

                if (!AppSettings.Current.HoverEnabled)
                {
                    continue;
                }

                var hit = _source.GetTextAtCursor();
                if (hit is null || string.IsNullOrWhiteSpace(hit.Text))
                {
                    candidate = null;
                    continue;
                }

                var text = hit.Text.Trim();
                if (text.Length < 2 || text.Length > 3000 || !LanguageDetector.ShouldTranslateToChinese(text))
                {
                    candidate = null;
                    continue;
                }

                if (!string.Equals(text, candidate, StringComparison.Ordinal))
                {
                    candidate = text;
                    candidateSince = DateTimeOffset.UtcNow;
                    continue;
                }

                var debounce = TimeSpan.FromMilliseconds(
                    Math.Clamp(AppSettings.Current.HoverDebounceMilliseconds, 150, 2000));
                if (DateTimeOffset.UtcNow - candidateSince < debounce)
                {
                    continue;
                }

                if (string.Equals(text, lastTranslated, StringComparison.Ordinal)
                    && DateTimeOffset.UtcNow - lastTranslatedAt < DuplicateSuppression)
                {
                    continue;
                }

                lastTranslated = text;
                lastTranslatedAt = DateTimeOffset.UtcNow;

                await TranslateAsync(text, hit.X, hit.Y, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Publish(new HoverSnapshot(
                    string.Empty,
                    null,
                    "悬停翻译出错：" + ex.Message,
                    false,
                    true,
                    0,
                    0));
                await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task TranslateAsync(string text, int x, int y, CancellationToken cancellationToken)
    {
        Publish(new HoverSnapshot(text, null, "正在翻译…", true, false, x, y));

        var latest = string.Empty;
        var progress = new Progress<TranslationProgress>(value =>
        {
            if (value.Phase != TranslationPhase.Delta)
            {
                return;
            }

            latest = value.Text;
            Publish(new HoverSnapshot(text, value.Text, "正在翻译…", true, false, x, y));
        });

        try
        {
            await _translation.TranslateAsync(
                new TranslationRequest(text, LanguageCatalog.AutoCode, "zh-CN"),
                progress,
                cancellationToken).ConfigureAwait(false);

            Publish(new HoverSnapshot(text, latest, "翻译完成", false, false, x, y));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Publish(new HoverSnapshot(text, null, "翻译失败：" + ex.Message, false, true, x, y));
        }
    }

    private void Publish(HoverSnapshot snapshot) => SnapshotChanged?.Invoke(this, snapshot);
}
using System.Text;
using HoverTrans.Core.Model;
using HoverTrans.Core.Translation;

namespace HoverTrans.Services;

/// <summary>
/// The translation pipeline: split the text, load the local model on first use, translate one chunk at
/// a time and stream the growing text back.
/// </summary>
public sealed class TranslationService : ITranslationService
{
    private readonly HunyuanEngine _engine;

    public TranslationService(HunyuanEngine engine) => _engine = engine;

    public string StatusText => _engine.State switch
    {
        EngineState.Ready => $"模型已加载（{HunyuanEngine.ModelId} · {_engine.BackendName} · {_engine.LoadTime.TotalSeconds:0.0} 秒）",
        EngineState.Loading => "模型加载中…（首次翻译要读约 1.3 GB 权重）",
        EngineState.Failed => "模型加载失败：" + (_engine.LastError ?? "未知错误"),
        _ => NotLoadedText()
    };

    public bool IsModelLoaded => _engine.IsLoaded;

    public void SetIdleUnload(TimeSpan? timeout) => _engine.SetIdleUnload(timeout);

    /// <summary>
    /// Why the model is not in memory: released on purpose (say how much that freed) or not loaded yet.
    /// </summary>
    private string NotLoadedText()
    {
        if (_engine.LastReleasedBytes > 0)
        {
            var gigabytes = _engine.LastReleasedBytes / 1024d / 1024 / 1024;
            return $"模型未加载（已释放约 {gigabytes:0.0} GB，下次翻译重新加载）";
        }

        return _engine.IdleUnload is { } idle
            ? $"模型未加载（首次翻译时加载，闲置 {idle.TotalMinutes:0.#} 分钟后释放内存）"
            : "模型未加载（首次翻译时加载，之后常驻）";
    }

    public async Task TranslateAsync(
        TranslationRequest request,
        IProgress<TranslationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("要翻译的文本为空。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TargetLanguage)
            || request.TargetLanguage == "auto")
        {
            throw new ArgumentException("请选择目标语言。", nameof(request));
        }

        if (!_engine.IsLoaded)
        {
            progress?.Report(TranslationProgress.StartingModel());
        }

        await _engine.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(TranslationProgress.Translating());

        var chunks = TextChunker.Split(request.Text);
        var result = new StringBuilder();

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prompt = TranslationPrompt.Build(
                chunk,
                request.SourceLanguage,
                request.TargetLanguage,
                request.Style);

            var chunkText = string.Empty;
            await foreach (var text in _engine.StreamAsync(prompt, cancellationToken).ConfigureAwait(false))
            {
                chunkText = text;
                progress?.Report(TranslationProgress.Delta(Combine(result, chunkText)));
            }

            if (chunkText.Length == 0)
            {
                throw new InvalidOperationException("模型没有返回译文，请检查本地模型是否正常加载。");
            }

            if (result.Length > 0)
            {
                result.Append('\n');
            }

            result.Append(chunkText.Trim());
            progress?.Report(TranslationProgress.Delta(result.ToString()));
        }

        var final = result.ToString().Trim();
        progress?.Report(TranslationProgress.Completed(final));
    }

    public Task UnloadModelAsync(CancellationToken cancellationToken = default) =>
        _engine.UnloadAsync(cancellationToken);

    private static string Combine(StringBuilder previous, string current)
    {
        if (previous.Length == 0)
        {
            return current.TrimEnd();
        }

        return previous.ToString() + "\n" + current.TrimEnd();
    }
}

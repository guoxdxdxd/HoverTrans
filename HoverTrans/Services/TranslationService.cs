using System.Text;
using HoverTrans.Core.Model;
using HoverTrans.Core.Translation;

namespace HoverTrans.Services;

/// <summary>The translation pipeline: start the model on demand, translate, then let it go idle.</summary>
public sealed class TranslationService : ITranslationService
{
    private readonly LlamaServerManager _server;
    private readonly LlamaTranslationClient _client;

    public TranslationService(LlamaServerManager server, LlamaTranslationClient client)
    {
        _server = server;
        _client = client;
    }

    public string StatusText => _server.State switch
    {
        LlamaServerState.Running => "模型运行中",
        LlamaServerState.Starting => "模型启动中…",
        LlamaServerState.Stopping => "模型停止中…",
        LlamaServerState.Failed => "模型异常：" + (_server.LastError ?? "未知错误"),
        _ => "模型未启动（首次翻译时自动启动）"
    };

    public bool IsModelRunning => _server.State == LlamaServerState.Running;

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

        using var lease = _server.EnterRequest();

        if (_server.State != LlamaServerState.Running)
        {
            progress?.Report(TranslationProgress.StartingModel());
        }

        await _server.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(TranslationProgress.Translating());

        var chunks = TextChunker.Split(request.Text);
        var result = new StringBuilder();

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prompt = TranslationPrompt.Build(
                chunk,
                request.SourceLanguage,
                request.TargetLanguage);

            var chunkBuilder = new StringBuilder();
            await foreach (var delta in _client
                               .StreamAsync(_server.BaseUrl, _server.ApiKey, prompt, cancellationToken)
                               .ConfigureAwait(false))
            {
                chunkBuilder.Append(delta);
                progress?.Report(TranslationProgress.Delta(Combine(result, chunkBuilder)));
            }

            if (chunkBuilder.Length == 0)
            {
                throw new InvalidOperationException("模型没有返回译文，请检查模型是否正常加载。");
            }

            if (result.Length > 0)
            {
                result.Append('\n');
            }

            result.Append(chunkBuilder.ToString().Trim());
            progress?.Report(TranslationProgress.Delta(result.ToString()));
        }

        var final = result.ToString().Trim();
        progress?.Report(TranslationProgress.Completed(final));
    }

    public Task StopModelAsync(CancellationToken cancellationToken = default) =>
        _server.StopAsync(cancellationToken);

    private static string Combine(StringBuilder previous, StringBuilder current)
    {
        if (previous.Length == 0)
        {
            return current.ToString().TrimEnd();
        }

        return previous.ToString() + "\n" + current.ToString().TrimEnd();
    }
}
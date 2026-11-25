using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HoverTrans.Core.Platform;
using Sdcb.GgufWeights.Hy_MT3_1_8B_Q4_K_M;
using Sdcb.HyMT2Sharp.Backends;
using Sdcb.HyMT2Sharp.Model;

namespace HoverTrans.Core.Model;

/// <summary>
/// The local translation model, loaded in this process.
/// </summary>
/// <remarks>
/// <para>
/// Sdcb.HyMT2Sharp is a pure-C# implementation of Tencent's hunyuan-dense translation models: the
/// weights come from the <c>Sdcb.GgufWeights.Hy-MT3-1.8B-Q4_K_M</c> package (six assemblies, read as
/// one stream), and the kernels are managed SIMD with optional Vulkan/Metal backends. There is no
/// llama.cpp, no child process, no local HTTP server, no port and nothing that needs idling out.
/// </para>
/// <para>
/// Loading is lazy — the first translation pays a few seconds and several GB of memory, and the app
/// pays nothing when the person never translates. <see cref="HunyuanDenseModel"/> is not thread-safe,
/// so requests queue behind one gate; generation runs on a worker thread and publishes the text so
/// far through a channel, which keeps the UI thread free.
/// </para>
/// </remarks>
public sealed class HunyuanEngine : IAsyncDisposable
{
    private readonly HunyuanEngineOptions _options;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private HunyuanDenseModel? _model;
    private EngineState _state = EngineState.NotLoaded;
    private string? _lastError;
    private string _backendName = "cpu";
    private TimeSpan _loadTime;
    private bool _disposed;

    public HunyuanEngine(HunyuanEngineOptions options) => _options = options;

    /// <summary>Raised when <see cref="State"/> changes; may be raised on a worker thread.</summary>
    public event EventHandler? StateChanged;

    public EngineState State => _state;

    /// <summary>Why loading failed, for the status line.</summary>
    public string? LastError => _lastError;

    /// <summary>The backend actually in use: "cpu", "vulkan" or "metal".</summary>
    public string BackendName => _backendName;

    /// <summary>How long the weights took to load, once loaded.</summary>
    public TimeSpan LoadTime => _loadTime;

    /// <summary>The model package this build ships: <c>Hy-MT3-1.8B-Q4_K_M</c>.</summary>
    public static string ModelId => Weights.ModelId;

    public bool IsLoaded => _state == EngineState.Ready;

    /// <summary>Loads the weights if they are not loaded yet; a cancelled wait leaves the engine as it was.</summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_state == EngineState.Ready)
        {
            return;
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == EngineState.Ready)
            {
                return;
            }

            SetState(EngineState.Loading);
            try
            {
                // Reading 1.3 GB of weights must not happen on the UI thread.
                _model = await Task.Run(Load, cancellationToken).ConfigureAwait(false);
                SetState(EngineState.Ready);
            }
            catch (OperationCanceledException)
            {
                SetState(EngineState.NotLoaded);
                throw;
            }
            catch (Exception ex)
            {
                _lastError = Describe(ex);
                Log("model load failed: " + Describe(ex));
                SetState(EngineState.Failed);
                throw;
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }

    /// <summary>
    /// Translates <paramref name="prompt"/> and yields the translated text so far, so the caller can
    /// show it growing. Every yield replaces the previous one; the last one is the final translation.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        var model = _model ?? throw new InvalidOperationException("模型没有加载成功。");

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        // Cancelling the pump unblocks a producer that is waiting on a full channel when the caller
        // walks away (a cancelled hover translation, a closed window).
        using var pump = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity: 64)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var producer = Task.Run(() => GenerateAsync(model, prompt, channel.Writer, pump.Token), CancellationToken.None);
        try
        {
            await foreach (var text in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return text;
            }
        }
        finally
        {
            // GenerateAsync never throws: it reports through the channel, so this only waits for it.
            pump.Cancel();
            await producer.ConfigureAwait(false);
            _requestGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            // Let a running translation wind down; at exit it is cancelled anyway.
            if (await _requestGate.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                _requestGate.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // Already torn down.
        }

        _model?.Dispose();
        _model = null;
        SetState(EngineState.NotLoaded);
    }

    private HunyuanDenseModel Load()
    {
        var started = Stopwatch.StartNew();
        var preference = _options.BackendPreference;
        Log($"loading {Weights.ModelId} ({Weights.Length:N0} bytes, {Weights.Segments().Length} assemblies), backend={preference}");

        IComputeBackend? backend = null;
        if (preference != "cpu")
        {
            try
            {
                backend = BackendFactory.Create(preference);
            }
            catch (Exception ex)
            {
                // A preference that cannot run is not a reason to fail: fall back to the CPU kernels.
                Log($"backend {preference} unavailable ({ex.GetType().Name}: {ex.Message}); using cpu");
                backend = null;
            }
        }

        try
        {
            _model = new HunyuanDenseModel(
                Weights.Model(),
                _options.Threads,
                KvCacheConfig.Memory,
                backend);
            _backendName = backend?.Name ?? "cpu";
        }
        catch (Exception ex) when (backend is not null)
        {
            // Vulkan in particular can enumerate a device and still fail to host the weights (a driver
            // without a host-visible heap); keep the app usable on the CPU path.
            Log($"backend {backend.Name} failed to host the model ({ex.GetType().Name}: {ex.Message}); retrying on cpu");
            _model = new HunyuanDenseModel(
                Weights.Model(),
                _options.Threads,
                KvCacheConfig.Memory,
                backend: null);
            _backendName = "cpu";
        }

        _loadTime = started.Elapsed;
        Log($"model ready in {_loadTime.TotalSeconds:0.00}s, backend={_backendName}, threads={_model.ThreadCount} ({_model.ThreadAutoHint}), context={_model.Config.ContextLength}");
        return _model!;
    }

    /// <summary>Runs the token loop on a worker thread and publishes the growing text.</summary>
    private async Task GenerateAsync(
        HunyuanDenseModel model,
        string prompt,
        ChannelWriter<string> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var ids = model.Tokenizer.Encode(
                ChatTemplate.RenderHunyuanDense([new ChatMessage("user", prompt)]));
            if (ids.Length == 0)
            {
                throw new InvalidOperationException("提示词没有编码出任何 token。");
            }

            if (ids.Length >= HunyuanEngineOptions.MaxPromptTokens)
            {
                throw new InvalidOperationException(
                    $"提示词太长（{ids.Length} tokens），超过本地模型的 {HunyuanEngineOptions.MaxPromptTokens} token 上下文，请分段翻译。");
            }

            var maxTokens = Math.Clamp(
                _options.MaxTokens,
                16,
                HunyuanEngineOptions.MaxPromptTokens - ids.Length);

            // The KV cache is reused at token granularity across requests: a prompt that starts with the
            // same instruction does not recompute that prefix.
            var alignment = model.AlignPrompt(ids);
            var sampler = new Sampler(new SamplingParams
            {
                // What the llama-server command line used for Hy-MT2: almost greedy, no repetition
                // penalty (a translation repeats words legitimately).
                Temperature = 0.1f,
                TopP = 0.6f,
                TopK = 0,
                MinP = 0f,
                RepeatPenalty = 1f,
                PenaltyWindow = 0
            });
            sampler.FeedHistory(ids);

            var logits = model.Forward(alignment.Suffix);
            var generated = new List<int>(capacity: 64);
            var visible = new VisibleText();
            var published = string.Empty;

            for (var i = 0; i < maxTokens; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var token = sampler.Sample(logits);
                if (model.Tokenizer.IsStop(token))
                {
                    break;
                }

                generated.Add(token);
                var text = visible.Update(model.Tokenizer.DecodeVisible(generated));
                if (!string.Equals(text, published, StringComparison.Ordinal))
                {
                    published = text;
                    await writer.WriteAsync(text, cancellationToken).ConfigureAwait(false);
                }

                logits = model.Forward([token]);
            }

            writer.TryComplete();
        }
        catch (OperationCanceledException)
        {
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
        finally
        {
            // Return the KV cache to the state a fresh request expects. The buffers stay allocated
            // (KvCacheConfig.Memory), so the next translation does not re-rent ~250 MB of native memory.
            model.EndRequest();
        }
    }

    private void SetState(EngineState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Describe(Exception ex) => ex is AggregateException { InnerException: { } inner }
        ? inner.Message
        : ex.Message;

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            File.AppendAllText(
                AppPaths.EngineLog,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break translation.
        }
    }
}

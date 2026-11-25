using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using HoverTrans.Core.Platform;
using Sdcb.GgufWeights.Hy_MT3_1_8B_Q4_K_M;
using Sdcb.HyMT2Sharp.Backends;
using Sdcb.HyMT2Sharp.Model;

namespace HoverTrans.Core.Model;

/// <summary>
/// The local translation model, loaded in this process and released again when it is not needed.
/// </summary>
/// <remarks>
/// <para>
/// Sdcb.HyMT2Sharp is a pure-C# implementation of Tencent's hunyuan-dense translation models: the
/// weights come from the <c>Sdcb.GgufWeights.Hy-MT3-1.8B-Q4_K_M</c> package (six assemblies, read as
/// one stream), and the kernels are managed SIMD with optional Vulkan/Metal backends. There is no
/// llama.cpp, no child process, no local HTTP server and no port.
/// </para>
/// <para>
/// Loading is lazy — the first translation pays a few seconds and ~3 GB of private memory — and the
/// weights can be released again: after <see cref="HunyuanEngineOptions.IdleUnload"/> without a
/// translation (or on request) the model is disposed and its native arenas go back to the OS. The
/// next translation loads it again from the assemblies, which stay on disk, so a reload is fast when
/// the file cache is still warm.
/// </para>
/// <para>
/// <see cref="HunyuanDenseModel"/> is not thread-safe, so requests queue behind one gate; generation
/// runs on a worker thread and publishes the text so far through a channel, which keeps the UI thread
/// free. Requests hold a lease, so the idle release can never pull the model out from under one.
/// </para>
/// </remarks>
public sealed class HunyuanEngine : IAsyncDisposable
{
    private readonly HunyuanEngineOptions _options;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private HunyuanDenseModel? _model;
    private CancellationTokenSource? _idleCts;
    private Task? _idleLoop;
    private EngineState _state = EngineState.NotLoaded;
    private string? _lastError;
    private string _backendName = "cpu";
    private TimeSpan _loadTime;
    private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
    private long _activeRequests;
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

    /// <summary>How long the weights stay in memory between translations, or null when they never leave.</summary>
    public TimeSpan? IdleUnload => _options.IdleUnloadValue;

    /// <summary>Whether a translation is running right now (published for the status line).</summary>
    public bool IsBusy => Interlocked.Read(ref _activeRequests) > 0;

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
                _lastError = null;
                Touch();
                SetState(EngineState.Ready);
                StartIdleLoop();
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

        // A lease for the whole translation: the idle release only fires when this drops back to zero,
        // so the model cannot be disposed between loading it and taking the request gate.
        Interlocked.Increment(ref _activeRequests);
        Touch();
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
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

            try
            {
                var model = _model ?? throw new InvalidOperationException("模型没有加载成功。");
                var producer = Task.Run(
                    () => GenerateAsync(model, prompt, channel.Writer, pump.Token),
                    CancellationToken.None);
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
                }
            }
            finally
            {
                _requestGate.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeRequests);
            Touch();
        }
    }

    /// <summary>
    /// Releases the weights now and hands their memory back to the OS. The next translation loads them
    /// again, so this is only skipped while a translation is running.
    /// </summary>
    public async Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Interlocked.Read(ref _activeRequests) > 0)
            {
                // A request is waiting for the gate we just took; it would find nothing to run on.
                return;
            }

            if (_model is null)
            {
                // Nothing loaded: clear a previous failure so the next translation tries again.
                if (_state == EngineState.Failed)
                {
                    _lastError = null;
                    SetState(EngineState.NotLoaded);
                }

                return;
            }

            var idle = DateTimeOffset.UtcNow - _lastActivity;
            var privateBefore = Process.GetCurrentProcess().PrivateMemorySize64;
            _model.Dispose();
            _model = null;
            SetState(EngineState.NotLoaded);

            // The native arenas are gone; this only asks Windows to drop the now-free pages from the
            // working set so the number the person sees drops at once instead of under memory pressure.
            TrimWorkingSet();

            var privateAfter = Process.GetCurrentProcess().PrivateMemorySize64;
            Log($"model released after {idle.TotalMinutes:0.#} min idle, private memory {(privateBefore - privateAfter) / 1024 / 1024} MB returned");
        }
        finally
        {
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
        _idleCts?.Cancel();
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

        if (_idleLoop is not null)
        {
            try
            {
                await _idleLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }

            _idleLoop = null;
        }

        _idleCts?.Dispose();
        _idleCts = null;
        _model?.Dispose();
        _model = null;
        SetState(EngineState.NotLoaded);
    }

    private void Touch() => _lastActivity = DateTimeOffset.UtcNow;

    private void StartIdleLoop()
    {
        if (_options.IdleUnloadValue is null || _idleLoop is { IsCompleted: false })
        {
            return;
        }

        _idleCts?.Dispose();
        _idleCts = new CancellationTokenSource();
        _idleLoop = Task.Run(() => IdleLoopAsync(_idleCts.Token));
    }

    private async Task IdleLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_options.IdleCheckInterval, cancellationToken).ConfigureAwait(false);

                if (_options.IdleUnloadValue is not { } timeout
                    || _state != EngineState.Ready
                    || IsBusy
                    || DateTimeOffset.UtcNow - _lastActivity < timeout)
                {
                    continue;
                }

                await UnloadAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            Log("idle release failed: " + Describe(ex));
        }
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

    /// <summary>
    /// Drops the freed pages from the working set. The private bytes are what the process really holds
    /// (they fall to ~100 MB when the model is disposed); the working set would otherwise keep counting
    /// free pages until Windows needs them.
    /// </summary>
    private static void TrimWorkingSet()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }
        catch
        {
            // Cosmetic only.
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

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

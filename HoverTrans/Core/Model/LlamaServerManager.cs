using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using HoverTrans.Core.Platform;

namespace HoverTrans.Core.Model;

/// <summary>
/// Owns the llama-server process: starts it on the first translation, waits for /health,
/// and stops it after <see cref="LlamaServerOptions.IdleTimeoutValue"/> without requests.
/// </summary>
public sealed class LlamaServerManager : IAsyncDisposable
{
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(5);

    private readonly LlamaServerOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _logLock = new();
    private Process? _process;
    private CancellationTokenSource? _idleCts;
    private Task? _idleTask;
    private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
    private int _activeRequests;
    private LlamaServerState _state = LlamaServerState.Stopped;
    private string? _lastError;
    private string? _apiKey;
    private bool _disposed;

    public LlamaServerManager(LlamaServerOptions options)
    {
        _options = options;
    }

    public event EventHandler? StateChanged;

    public LlamaServerState State => _state;

    public string? LastError => _lastError;

    /// <summary>
    /// The key llama-server was started with, or null when it is not running. It is generated per start so another
    /// local process cannot use the model for free; the app sends it on every request.
    /// </summary>
    public string? ApiKey => _apiKey;

    public LlamaServerOptions Options => _options;

    public string BaseUrl => string.Create(
        CultureInfo.InvariantCulture,
        $"http://127.0.0.1:{_options.Port}");

    /// <summary>Marks a request as in flight so the idle timer cannot stop the model mid-translation.</summary>
    public IDisposable EnterRequest()
    {
        Interlocked.Increment(ref _activeRequests);
        Touch();
        return new RequestLease(this);
    }

    public void Touch() => _lastActivity = DateTimeOffset.UtcNow;

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (State == LlamaServerState.Running && IsProcessAlive())
        {
            Touch();
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == LlamaServerState.Running && IsProcessAlive())
            {
                Touch();
                return;
            }

            if (_process is not null)
            {
                await StopCoreAsync().ConfigureAwait(false);
            }

            ValidateFiles();
            EnsurePortFree();
            SetState(LlamaServerState.Starting);

            try
            {
                StartProcess();
                await WaitForHealthAsync(cancellationToken).ConfigureAwait(false);
                Touch();
                SetState(LlamaServerState.Running);
                StartIdleMonitor();
            }
            catch (OperationCanceledException)
            {
                await StopCoreAsync().ConfigureAwait(false);
                SetState(LlamaServerState.Stopped);
                throw;
            }
            catch (Exception ex)
            {
                await StopCoreAsync().ConfigureAwait(false);
                _lastError = ex.Message;
                SetState(LlamaServerState.Failed);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
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
            await StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // Shutting down must not throw.
        }

        _gate.Dispose();
    }

    private void ValidateFiles()
    {
        if (!File.Exists(_options.ServerPath))
        {
            throw new FileNotFoundException(
                $"未找到 llama-server：{_options.ServerPath}。请先运行 scripts/setup-model.ps1 下载运行时，或在设置里修改路径。",
                _options.ServerPath);
        }

        if (!File.Exists(_options.ModelPath))
        {
            throw new FileNotFoundException(
                $"未找到 Hy-MT2 模型：{_options.ModelPath}。请先运行 scripts/setup-model.ps1 下载模型（约 1.1 GB），或在设置里修改路径。",
                _options.ModelPath);
        }
    }

    /// <summary>
    /// Fails early with a clear message when something already listens on the configured port — almost always a
    /// llama-server left behind by a previous run. Without this the new process dies a second later and the UI only
    /// reports "启动后退出（退出码 1）", which says nothing about the port.
    /// </summary>
    private void EnsurePortFree()
    {
        if (!IsPortListening())
        {
            return;
        }

        throw new InvalidOperationException(
            $"端口 {_options.Port} 已被占用：很可能是上一次运行留下的 llama-server 还在运行，或别的程序占用了它。先结束那个进程，或在设置里换一个端口。");
    }

    private bool IsPortListening()
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(IPAddress.Loopback, _options.Port);
            return connect.Wait(TimeSpan.FromMilliseconds(300)) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private void StartProcess()
    {
        // A key per start: the server only listens on the loopback interface, but without one any other local
        // process could drive the model. It stays in memory and is sent on every request and health check.
        _apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

        var workingDirectory = Path.GetDirectoryName(_options.ServerPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ServerPath,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Environment.CurrentDirectory
                : workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(_options.ModelPath);
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(_options.Port.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--api-key");
        startInfo.ArgumentList.Add(_apiKey);
        startInfo.ArgumentList.Add("--ctx-size");
        startInfo.ArgumentList.Add(_options.ContextSize.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--parallel");
        startInfo.ArgumentList.Add("1");

        if (_options.GpuLayers >= 0)
        {
            startInfo.ArgumentList.Add("--n-gpu-layers");
            startInfo.ArgumentList.Add(_options.GpuLayers.ToString(CultureInfo.InvariantCulture));
        }

        if (_options.Threads > 0)
        {
            startInfo.ArgumentList.Add("--threads");
            startInfo.ArgumentList.Add(_options.Threads.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var argument in SplitArguments(_options.ExtraArguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => LogLine(e.Data);
        process.ErrorDataReceived += (_, e) => LogLine(e.Data);
        process.Exited += (_, _) =>
        {
            // A stop we asked for sets Stopping first and logs its own line, and it disposes the process, so the exit
            // code is gone by then (that used to be logged as a misleading -1). Only a crash deserves a line here.
            if (_state != LlamaServerState.Running)
            {
                return;
            }

            LogLine($"llama-server exited unexpectedly with code {SafeExitCode(process)}");
            SetState(LlamaServerState.Stopped);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 llama-server。");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
        LogLine($"llama-server started: {_options.ServerPath} --port {_options.Port}");
    }

    private async Task WaitForHealthAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _options.StartupTimeoutValue;
        var healthUrl = BaseUrl + "/health";

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_process is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"llama-server 启动后退出（退出码 {SafeExitCode(_process)}）。日志：{AppPaths.LlamaLog}");
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, healthUrl);
                if (_apiKey is { Length: > 0 } key)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                }

                using var response = await HealthClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Still loading the model, or the socket is not open yet.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The per-request timeout elapsed; try again.
            }

            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"等待 llama-server 就绪超时（{_options.StartupTimeoutValue.TotalSeconds:0} 秒）。日志：{AppPaths.LlamaLog}");
    }

    private void StartIdleMonitor()
    {
        _idleCts?.Cancel();
        _idleCts?.Dispose();
        _idleCts = new CancellationTokenSource();
        _idleTask = Task.Run(() => IdleLoopAsync(_idleCts.Token));
    }

    private async Task IdleLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(IdleCheckInterval, cancellationToken).ConfigureAwait(false);

                if (State != LlamaServerState.Running || Volatile.Read(ref _activeRequests) > 0)
                {
                    continue;
                }

                if (DateTimeOffset.UtcNow - _lastActivity < _options.IdleTimeoutValue)
                {
                    continue;
                }

                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (State == LlamaServerState.Running
                        && Volatile.Read(ref _activeRequests) == 0
                        && DateTimeOffset.UtcNow - _lastActivity >= _options.IdleTimeoutValue)
                    {
                        var idle = DateTimeOffset.UtcNow - _lastActivity;
                        LogLine($"idle for {idle.TotalMinutes:0.#} minutes (timeout {_options.IdleTimeoutValue.TotalMinutes:0.#}), stopping llama-server");
                        await StopCoreAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            LogLine("idle monitor failed: " + ex);
        }
    }

    private async Task StopCoreAsync()
    {
        _idleCts?.Cancel();

        var process = _process;
        _process = null;
        _apiKey = null;
        _state = LlamaServerState.Stopping;

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync()
                        .WaitAsync(TimeSpan.FromSeconds(10))
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                // The process may already be gone; nothing else to do.
            }
            finally
            {
                process.Dispose();
            }

            // Only a stop that actually had a process is worth a line: this used to be logged on every app exit,
            // even when the model was never started.
            LogLine("llama-server stopped");
        }

        SetState(LlamaServerState.Stopped);
    }

    private bool IsProcessAlive()
    {
        try
        {
            return _process is { HasExited: false };
        }
        catch
        {
            return false;
        }
    }

    private void SetState(LlamaServerState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    private static void LogLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            File.AppendAllText(
                AppPaths.LlamaLog,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break translation.
        }
    }

    private static IReadOnlyList<string> SplitArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return [];
        }

        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in arguments)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private sealed class RequestLease(LlamaServerManager owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref owner._activeRequests);
                owner.Touch();
            }
        }
    }
}
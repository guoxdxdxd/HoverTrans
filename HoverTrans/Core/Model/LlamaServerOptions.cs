namespace HoverTrans.Core.Model;

/// <summary>Everything needed to launch llama-server with Hy-MT2.</summary>
public sealed record LlamaServerOptions(
    string ServerPath,
    string ModelPath,
    int Port = 18080,
    int ContextSize = 4096,
    int GpuLayers = 99,
    int Threads = 0,
    TimeSpan? StartupTimeout = null,
    TimeSpan? IdleTimeout = null,
    string ExtraArguments = "")
{
    public TimeSpan StartupTimeoutValue => StartupTimeout ?? TimeSpan.FromSeconds(120);

    public TimeSpan IdleTimeoutValue => IdleTimeout ?? TimeSpan.FromMinutes(10);
}
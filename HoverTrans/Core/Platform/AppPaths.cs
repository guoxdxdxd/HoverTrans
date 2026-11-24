namespace HoverTrans.Core.Platform;

/// <summary>Where the app keeps downloaded runtimes, models and logs.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify),
        "HoverTrans");

    public static string Models => Path.Combine(Root, "models");

    public static string Runtime => Path.Combine(Root, "runtime");

    public static string Logs => Path.Combine(Root, "logs");

    public static string LlamaLog => Path.Combine(Logs, "llama-server.log");

    public static string DefaultServerPath => Path.Combine(
        Runtime,
        OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server");

    public static string DefaultModelPath => Path.Combine(Models, "Hy-MT2-1.8B-Q4_K_M.gguf");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Models);
        Directory.CreateDirectory(Runtime);
        Directory.CreateDirectory(Logs);
    }
}
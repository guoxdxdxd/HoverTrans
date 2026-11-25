namespace HoverTrans.Core.Platform;

/// <summary>
/// Where the app keeps its settings, history and logs. The model no longer lives here: it ships inside
/// the app (the <c>Sdcb.GgufWeights.Hy-MT3-1.8B-Q4_K_M</c> assemblies) instead of being downloaded.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify),
        "HoverTrans");

    public static string Logs => Path.Combine(Root, "logs");

    /// <summary>What the in-process model did: load time, backend, fallbacks and failures. No text.</summary>
    public static string EngineLog => Path.Combine(Logs, "engine.log");

    public static void EnsureCreated() => Directory.CreateDirectory(Logs);
}

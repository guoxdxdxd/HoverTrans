using HoverTrans.Core.Platform;

namespace HoverTrans.Platform;

/// <summary>Used on platforms whose accessibility backend has not been implemented yet.</summary>
public sealed class NullHoverTextSource : IHoverTextSource
{
    public bool IsSupported => false;

    /// <summary>
    /// On Windows this build is the wrong target: the hint explains how to start the one that has the platform layer,
    /// because a disabled 悬停取词 switch with only a macOS message reads like a bug (it happened).
    /// </summary>
    public string UnsupportedMessage => OperatingSystem.IsWindows()
        ? "这个构建没有带 Windows 取词（UI Automation）。请运行 net11.0-windows10.0.19041.0 目标：dotnet run --project HoverTrans -f net11.0-windows10.0.19041.0"
        : "悬停取词目前只支持 Windows：macOS 版需要辅助功能（Accessibility）权限，正在适配。";

    public HoverText? GetTextAtCursor() => null;
}
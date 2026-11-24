using HoverTrans.Core.Platform;

namespace HoverTrans.Platform;

/// <summary>Used on platforms whose accessibility backend has not been implemented yet.</summary>
public sealed class NullHoverTextSource : IHoverTextSource
{
    public bool IsSupported => false;

    public string UnsupportedMessage =>
        "悬停取词目前只支持 Windows：macOS 版需要辅助功能（Accessibility）权限，正在适配。";

    public HoverText? GetTextAtCursor() => null;
}
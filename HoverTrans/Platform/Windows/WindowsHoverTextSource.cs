#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using HoverTrans.Core.Platform;

namespace HoverTrans.Platform.Windows;

/// <summary>
/// UI Automation based hover text reader. UI Automation is the same accessibility tree screen readers use,
/// so it covers browsers, Office, WinUI/WPF/WinForms and most Electron apps without per-app code.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsHoverTextSource : IHoverTextSource, IDisposable
{
    private readonly UIA3Automation _automation = new();
    private bool _disposed;

    public bool IsSupported => true;

    public string UnsupportedMessage => string.Empty;

    public HoverText? GetTextAtCursor()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!GetCursorPos(out var point))
        {
            return null;
        }

        try
        {
            // Skip our own windows before touching UI Automation: cheaper and avoids feedback loops.
            var hwnd = WindowFromPoint(point);
            if (hwnd != IntPtr.Zero)
            {
                _ = GetWindowThreadProcessId(hwnd, out var processId);
                if (processId == (uint)Environment.ProcessId)
                {
                    return null;
                }
            }

            var element = _automation.FromPoint(new System.Drawing.Point(point.X, point.Y));
            if (element is null)
            {
                return null;
            }

            if (element.Properties.IsPassword.ValueOrDefault)
            {
                return null;
            }

            var text = ExtractText(element);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            text = Normalize(text);
            if (text.Length < 2 || text.Length > 2000)
            {
                return null;
            }

            var bounds = element.BoundingRectangle;
            return new HoverText(text, point.X, point.Y, bounds.Width, bounds.Height);
        }
        catch
        {
            // UI Automation providers can disappear mid-call (tabs closing, Electron re-rendering).
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _automation.Dispose();
    }

    private static string? ExtractText(AutomationElement element)
    {
        // Name is cheap and usually the visible text for labels, buttons, menu items and browser text nodes.
        var name = SafeRead(() => element.Name);
        if (!string.IsNullOrWhiteSpace(name) && name.Length <= 800)
        {
            return name;
        }

        var documentText = SafeRead(() => element.Patterns.Text.PatternOrDefault?.DocumentRange.GetText(-1));
        if (!string.IsNullOrWhiteSpace(documentText))
        {
            return documentText;
        }

        var value = SafeRead(() => element.Patterns.Value.PatternOrDefault?.Value);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var legacy = SafeRead(() => element.Patterns.LegacyIAccessible.PatternOrDefault?.Value);
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            return legacy;
        }

        return name;
    }

    private static T? SafeRead<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return default;
        }
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString().Trim();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
#endif
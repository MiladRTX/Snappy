using System.Runtime.InteropServices;
using Snappy.Geometry;
using Snappy.Interop;
using static Snappy.Interop.NativeMethods;

namespace Snappy.Capture;

/// <summary>A top-level window and its visible bounds in physical pixels.</summary>
public sealed record WindowInfo(nint Handle, string Title, PhysicalRect Bounds);

public static unsafe class WindowEnumerator
{
    /// <summary>Visible top-level windows, topmost first.</summary>
    public static IReadOnlyList<WindowInfo> GetCapturableWindows(IReadOnlyList<MonitorInfo> monitors)
    {
        var handles = new List<nint>();
        var state = GCHandle.Alloc(handles);
        try
        {
            EnumWindows(&CollectWindow, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        // Snappy's own windows count too (its Settings window can be captured); the overlays
        // don't exist yet when this runs, and the toast is closed before every capture.
        var windows = new List<WindowInfo>();
        foreach (var hwnd in handles)
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsCloaked(hwnd)) continue;

            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TRANSPARENT) != 0) continue; // click-through overlays
            if (ReadString(hwnd, &GetClassName) is "Progman" or "WorkerW") continue; // the desktop itself

            string title = ReadString(hwnd, &GetWindowText);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0 && title.Length == 0) continue;
            if (!TryGetFrameBounds(hwnd, out var bounds)) continue;

            // A maximized window's frame hangs a few pixels off its monitor; keep what is on screen.
            if (IsZoomed(hwnd) && monitors.FirstOrDefault(m => m.Bounds.Contains(Center(bounds))) is { } monitor)
                bounds = bounds.Intersect(monitor.Bounds);
            if (bounds.IsEmpty) continue;

            windows.Add(new WindowInfo(hwnd, title, bounds));
        }
        return windows;
    }

    /// <summary>
    /// The window as drawn on screen. Unlike GetWindowRect this leaves out the invisible resize
    /// borders (about 7 px on each side in Windows 10/11), so captures have no empty margin.
    /// </summary>
    static bool TryGetFrameBounds(nint hwnd, out PhysicalRect bounds)
    {
        RECT rect;
        bool ok = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &rect, (uint)sizeof(RECT)) == 0;
        bounds = ok ? DisplayEnumerator.ToRect(rect) : default;
        return ok && !bounds.IsEmpty;
    }

    /// <summary>Cloaked windows are "visible" but not shown: other virtual desktops, suspended UWP apps.</summary>
    static bool IsCloaked(nint hwnd)
    {
        int cloaked = 0;
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    static string ReadString(nint hwnd, delegate*<nint, char*, int, int> read)
    {
        char* buffer = stackalloc char[256];
        int length = read(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    static PhysicalPoint Center(PhysicalRect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    [UnmanagedCallersOnly]
    static int CollectWindow(nint hwnd, nint state)
    {
        ((List<nint>)GCHandle.FromIntPtr(state).Target!).Add(hwnd);
        return 1;
    }
}

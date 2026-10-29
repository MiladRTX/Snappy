using System.Runtime.InteropServices;
using Snappy.Geometry;
using Snappy.Interop;
using static Snappy.Interop.NativeMethods;

namespace Snappy.Capture;

public static unsafe class DisplayEnumerator
{
    /// <summary>All active displays, left to right, with physical-pixel bounds.</summary>
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var handles = new List<nint>();
        var state = GCHandle.Alloc(handles);
        try
        {
            EnumDisplayMonitors(0, null, &CollectMonitor, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        var monitors = new List<MonitorInfo>(handles.Count);
        foreach (var handle in handles)
        {
            var info = new MONITORINFOEX { cbSize = sizeof(MONITORINFOEX) };
            if (!GetMonitorInfo(handle, &info)) continue;

            double scale = GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;
            monitors.Add(new MonitorInfo(
                handle,
                new string(info.szDevice),
                ToRect(info.rcMonitor),
                ToRect(info.rcWork),
                scale,
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
        }

        return monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
    }

    /// <summary>The smallest rectangle containing every display (the virtual desktop).</summary>
    public static PhysicalRect VirtualBounds(IEnumerable<MonitorInfo> monitors) =>
        monitors.Aggregate(default(PhysicalRect), (bounds, monitor) => bounds.Union(monitor.Bounds));

    internal static PhysicalRect ToRect(RECT r) => PhysicalRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);

    [UnmanagedCallersOnly]
    static int CollectMonitor(nint monitor, nint hdc, RECT* clip, nint state)
    {
        ((List<nint>)GCHandle.FromIntPtr(state).Target!).Add(monitor);
        return 1;
    }
}

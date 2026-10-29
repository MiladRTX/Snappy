using Snappy.Geometry;

namespace Snappy.Interop;

internal static class NativeCursor
{
    /// <summary>The cursor position in physical virtual-desktop pixels.</summary>
    public static PhysicalPoint Position =>
        NativeMethods.GetCursorPos(out var point) ? new PhysicalPoint(point.X, point.Y) : default;
}

using System.Windows;

namespace Snappy.Geometry;

/// <summary>A point in physical screen pixels, in virtual-desktop coordinates (may be negative).</summary>
public readonly record struct PhysicalPoint(int X, int Y);

/// <summary>
/// An integer rectangle in physical screen pixels, in virtual-desktop coordinates.
/// <see cref="Right"/> and <see cref="Bottom"/> are exclusive.
/// </summary>
public readonly record struct PhysicalRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public PhysicalPoint Origin => new(X, Y);
    public string SizeText => $"{Width} × {Height}";

    public static PhysicalRect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    /// <summary>
    /// The rectangle a drag from <paramref name="a"/> to <paramref name="b"/> selects, in any
    /// direction. Both end pixels are included, so a drag can reach the last row and column of a
    /// monitor (the cursor can never go past them).
    /// </summary>
    public static PhysicalRect FromPoints(PhysicalPoint a, PhysicalPoint b) =>
        FromEdges(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);

    public bool Contains(PhysicalPoint p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    public PhysicalRect Intersect(PhysicalRect other)
    {
        int left = Math.Max(X, other.X), top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right), bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? FromEdges(left, top, right, bottom) : default;
    }

    public PhysicalRect Union(PhysicalRect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return FromEdges(Math.Min(X, other.X), Math.Min(Y, other.Y), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    /// <summary>Converts to WPF device-independent units, relative to <paramref name="origin"/>, at a DPI scale.</summary>
    public Rect ToDip(PhysicalPoint origin, double scale) =>
        new((X - origin.X) / scale, (Y - origin.Y) / scale, Width / scale, Height / scale);
}

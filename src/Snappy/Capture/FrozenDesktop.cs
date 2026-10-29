using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snappy.Geometry;

namespace Snappy.Capture;

/// <summary>
/// A snapshot of the whole virtual desktop taken at one instant, plus the displays and windows
/// that were on screen. Every selection is cut from this one image.
/// </summary>
public sealed class FrozenDesktop(BitmapSource image, PhysicalRect bounds, IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<WindowInfo> windows)
{
    /// <summary>The desktop at physical resolution. Pixel (0,0) is <see cref="Bounds"/>' top-left corner.</summary>
    public BitmapSource Image { get; } = image;
    public PhysicalRect Bounds { get; } = bounds;
    public IReadOnlyList<MonitorInfo> Monitors { get; } = monitors;
    public IReadOnlyList<WindowInfo> Windows { get; } = windows;

    public static FrozenDesktop Capture(bool includeWindows = true)
    {
        var monitors = DisplayEnumerator.GetMonitors();
        var bounds = DisplayEnumerator.VirtualBounds(monitors);
        if (bounds.IsEmpty) throw new InvalidOperationException("No displays were found.");

        var image = ScreenCapturer.Capture(bounds);
        var windows = includeWindows ? WindowEnumerator.GetCapturableWindows(monitors) : [];
        return new FrozenDesktop(image, bounds, monitors, windows);
    }

    public MonitorInfo? MonitorAt(PhysicalPoint p) => Monitors.FirstOrDefault(m => m.Bounds.Contains(p));

    /// <summary>The topmost window under <paramref name="p"/>.</summary>
    public WindowInfo? WindowAt(PhysicalPoint p) => Windows.FirstOrDefault(w => w.Bounds.Contains(p));

    public Color? PixelAt(PhysicalPoint p)
    {
        if (!Bounds.Contains(p)) return null;
        var pixel = new byte[4];
        Image.CopyPixels(new Int32Rect(p.X - Bounds.X, p.Y - Bounds.Y, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }

    /// <summary>A live view of part of the snapshot, for display (no pixel copy).</summary>
    public BitmapSource View(PhysicalRect rect)
    {
        var view = new CroppedBitmap(Image, ToImageRect(Bounds, rect) ?? throw OutsideDesktop(rect));
        view.Freeze();
        return view;
    }

    /// <summary>A standalone copy of part of the snapshot, at full physical resolution.</summary>
    public BitmapSource Crop(PhysicalRect rect)
    {
        var source = ToImageRect(Bounds, rect) ?? throw OutsideDesktop(rect);
        int stride = source.Width * 4;
        var pixels = new byte[stride * source.Height];
        Image.CopyPixels(source, pixels, stride, 0);
        var copy = BitmapSource.Create(source.Width, source.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        copy.Freeze();
        return copy;
    }

    /// <summary>
    /// Maps a desktop rectangle to pixel coordinates in the snapshot image, clipped to the
    /// desktop. Null when nothing of it is on the desktop.
    /// </summary>
    public static Int32Rect? ToImageRect(PhysicalRect desktopBounds, PhysicalRect rect)
    {
        var visible = rect.Intersect(desktopBounds);
        if (visible.IsEmpty) return null;
        return new Int32Rect(visible.X - desktopBounds.X, visible.Y - desktopBounds.Y, visible.Width, visible.Height);
    }

    static ArgumentException OutsideDesktop(PhysicalRect rect) =>
        new($"{rect} is outside the captured desktop.", nameof(rect));
}

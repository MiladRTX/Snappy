using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snappy.Geometry;
using Snappy.Interop;
using static Snappy.Interop.NativeMethods;

namespace Snappy.Capture;

/// <summary>
/// Renders one window by itself, as it would look with nothing in front of it. The screen only
/// holds the pixels of whatever is on top, so a covered window can't be cut from a screenshot.
/// </summary>
public static unsafe class WindowCapturer
{
    /// <summary>
    /// The window's own pixels inside <paramref name="area"/> (physical pixels, within its frame),
    /// or null when Windows can't render it on its own: a window Windows stretches to fit the
    /// display's scaling (rendered alone it would come out at its unscaled size), or one that
    /// renders as nothing but black this way, as some games and video players do.
    /// </summary>
    /// <param name="displayScale">Scaling of the display the window is on (1.25 = 125%).</param>
    public static BitmapSource? TryCapture(nint hwnd, PhysicalRect area, double displayScale)
    {
        if (GetDpiForWindow(hwnd) != (uint)Math.Round(96 * displayScale)) return null;
        if (!GetWindowRect(hwnd, out var rect)) return null;
        var window = PhysicalRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var crop = area.Intersect(window);
        if (crop.IsEmpty) return null;

        nint screenDc = GetDC(0);
        nint memoryDc = 0, dib = 0, previous = 0;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = window.Width,
                biHeight = -window.Height, // top-down rows, matching WPF
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            };
            dib = CreateDIBSection(screenDc, &header, DIB_RGB_COLORS, out nint bits, 0, 0);
            if (dib == 0) return null;

            // PrintWindow draws the whole window, invisible resize borders included, from its top-left.
            previous = SelectObject(memoryDc, dib);
            if (!PrintWindow(hwnd, memoryDc, PW_RENDERFULLCONTENT)) return null;
            GdiFlush();

            int windowStride = window.Width * 4, stride = crop.Width * 4;
            var pixels = new byte[stride * crop.Height];
            var first = (byte*)bits + (crop.Y - window.Y) * windowStride + (crop.X - window.X) * 4;
            fixed (byte* target = pixels)
            {
                for (int y = 0; y < crop.Height; y++)
                    Buffer.MemoryCopy(first + y * windowStride, target + y * stride, stride, stride);
                if (IsAllBlack((uint*)target, crop.Width * crop.Height)) return null;
            }

            var image = BitmapSource.Create(crop.Width, crop.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
            image.Freeze();
            return image;
        }
        finally
        {
            if (previous != 0) SelectObject(memoryDc, previous);
            if (dib != 0) DeleteObject(dib);
            if (memoryDc != 0) DeleteDC(memoryDc);
            ReleaseDC(0, screenDc);
        }
    }

    static bool IsAllBlack(uint* pixels, int count)
    {
        for (int i = 0; i < count; i++)
            if ((pixels[i] & 0x00FFFFFF) != 0) return false; // the top byte is unused (Bgr32)
        return true;
    }
}

using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snappy.Geometry;
using Snappy.Interop;
using static Snappy.Interop.NativeMethods;

namespace Snappy.Capture;

public static unsafe class ScreenCapturer
{
    /// <summary>
    /// Copies an area of the composed desktop, one bitmap pixel per screen pixel.
    /// The calling thread must be Per-Monitor V2 DPI aware (the app manifest makes it so),
    /// otherwise Windows hands back a rescaled image.
    /// </summary>
    public static BitmapSource Capture(PhysicalRect area)
    {
        if (area.IsEmpty) throw new ArgumentException("Nothing to capture: the area is empty.", nameof(area));

        nint screenDc = GetDC(0);
        nint memoryDc = 0, dib = 0, previous = 0;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = area.Width,
                biHeight = -area.Height, // negative = top-down rows, matching WPF
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            };
            dib = CreateDIBSection(screenDc, &header, DIB_RGB_COLORS, out nint bits, 0, 0);
            if (dib == 0) throw new Win32Exception();

            previous = SelectObject(memoryDc, dib);
            // CAPTUREBLT includes layered (semi-transparent) windows such as menus and tooltips.
            if (!BitBlt(memoryDc, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y, SRCCOPY | CAPTUREBLT))
                throw new Win32Exception();
            GdiFlush();

            int stride = area.Width * 4;
            var image = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Bgr32, null, bits, stride * area.Height, stride);
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
}

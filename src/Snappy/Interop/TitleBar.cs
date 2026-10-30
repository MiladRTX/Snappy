using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static Snappy.Interop.NativeMethods;

namespace Snappy.Interop;

/// <summary>Matches a window's system title bar to the app's palette.</summary>
internal static unsafe class TitleBar
{
    const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const uint DWMWA_CAPTION_COLOR = 35; // Windows 11; Windows 10 ignores it and keeps the system color

    /// <summary>Light or dark caption text and buttons, on a title bar painted <paramref name="background"/>.</summary>
    public static void Match(Window window, bool dark, Color background)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, &value, sizeof(int));
        int colorRef = background.R | background.G << 8 | background.B << 16; // COLORREF is 0x00BBGGRR
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, &colorRef, sizeof(int));
    }
}

using System.Windows;
using System.Windows.Interop;
using static Snappy.Interop.NativeMethods;

namespace Snappy.Interop;

/// <summary>Matches a window's system title bar to the app's light or dark palette.</summary>
internal static unsafe class TitleBar
{
    const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void SetDark(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, &value, sizeof(int));
    }
}

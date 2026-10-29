using System.Windows;
using System.Windows.Interop;
using Snappy.Geometry;
using static Snappy.Interop.NativeMethods;

namespace Snappy.Interop;

/// <summary>
/// Places WPF windows at exact physical-pixel rectangles. WPF positions windows in DIPs and, on
/// mixed-DPI setups, resizes a window when it crosses onto a monitor with another scale; pinning
/// overrides every move/resize so the window always covers exactly the requested pixels.
/// </summary>
internal static unsafe class WindowPlacement
{
    public static void Pin(Window window, PhysicalRect bounds, bool topmost, bool cloakUntilRendered = false, bool noActivate = false)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (noActivate)
            {
                // Never take focus and stay out of Alt+Tab.
                var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)((long)exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            }
            // Hide the window from the screen until WPF has drawn its first frame (no black flash).
            if (cloakUntilRendered) SetCloak(hwnd, true);

            HwndSource.FromHwnd(hwnd)!.AddHook((nint _, int msg, nint _, nint lParam, ref bool _) =>
            {
                if (msg == WM_WINDOWPOSCHANGING)
                {
                    var pos = (WINDOWPOS*)lParam;
                    pos->x = bounds.X;
                    pos->y = bounds.Y;
                    pos->cx = bounds.Width;
                    pos->cy = bounds.Height;
                    pos->flags &= ~(SWP_NOMOVE | SWP_NOSIZE);
                }
                return 0;
            });

            SetWindowPos(hwnd, topmost ? HWND_TOPMOST : 0, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                SWP_NOACTIVATE | (topmost ? 0 : SWP_NOZORDER));
        };
    }

    public static void Uncloak(Window window) => SetCloak(new WindowInteropHelper(window).Handle, false);

    static void SetCloak(nint hwnd, bool cloaked)
    {
        int value = cloaked ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, &value, sizeof(int));
    }
}

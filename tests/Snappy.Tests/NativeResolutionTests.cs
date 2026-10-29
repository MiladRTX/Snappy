using System.Runtime.InteropServices;
using Snappy.Capture;

namespace Snappy.Tests;

/// <summary>
/// Runs a real capture of this machine's displays and checks it against each display's actual
/// video mode (EnumDisplaySettings reports the true resolution regardless of DPI scaling).
/// </summary>
public partial class NativeResolutionTests
{
    const int ENUM_CURRENT_SETTINGS = -1;
    const nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    [Fact]
    public void Captures_every_display_at_its_physical_resolution()
    {
        // The test host has no DPI-aware manifest; opt this thread in the way Snappy's manifest does.
        nint previous = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        try
        {
            var desktop = FrozenDesktop.Capture(includeWindows: false);

            Assert.NotEmpty(desktop.Monitors);
            Assert.Equal(desktop.Bounds.Width, desktop.Image.PixelWidth);
            Assert.Equal(desktop.Bounds.Height, desktop.Image.PixelHeight);

            foreach (var monitor in desktop.Monitors)
            {
                var (width, height) = CurrentMode(monitor.DeviceName);
                Assert.Equal(width, monitor.Bounds.Width);
                Assert.Equal(height, monitor.Bounds.Height);

                var crop = desktop.Crop(monitor.Bounds);
                Assert.Equal(width, crop.PixelWidth);
                Assert.Equal(height, crop.PixelHeight);
            }
        }
        finally
        {
            SetThreadDpiAwarenessContext(previous);
        }
    }

    static (int Width, int Height) CurrentMode(string deviceName)
    {
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        Assert.True(EnumDisplaySettingsW(deviceName, ENUM_CURRENT_SETTINGS, ref mode), $"No display mode for {deviceName}");
        return ((int)mode.dmPelsWidth, (int)mode.dmPelsHeight);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumDisplaySettingsW(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll")]
    static extern nint SetThreadDpiAwarenessContext(nint dpiContext);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
}

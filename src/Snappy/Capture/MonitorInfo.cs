using Snappy.Geometry;

namespace Snappy.Capture;

/// <summary>A display, with its bounds in physical pixels and its Windows scale factor (1.25 = 125%).</summary>
public sealed record MonitorInfo(nint Handle, string DeviceName, PhysicalRect Bounds, PhysicalRect WorkArea, double Scale, bool IsPrimary)
{
    const string DevicePrefix = @"\\.\DISPLAY";

    /// <summary>"Display 1" for "\\.\DISPLAY1".</summary>
    public string DisplayName => DeviceName.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase)
        ? "Display " + DeviceName[DevicePrefix.Length..]
        : DeviceName;
}

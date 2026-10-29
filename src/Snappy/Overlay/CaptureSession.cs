using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snappy.Capture;
using Snappy.Geometry;
using Snappy.Interop;

namespace Snappy.Overlay;

/// <summary>What the floating card next to the cursor shows.</summary>
internal sealed record PointerInfo(string Primary, string Secondary, bool ShowLoupe, Color? Color);

/// <summary>
/// One capture: freezes the desktop, shows an overlay on every display and turns the user's
/// click or drag into a cropped image. All selection state is kept in physical pixels; each
/// overlay converts it to its own DPI only for drawing.
/// </summary>
internal sealed class CaptureSession
{
    /// <summary>Selections smaller than this (in pixels) are treated as accidental clicks.</summary>
    const int MinimumSize = 3;

    readonly List<OverlayWindow> _overlays = [];
    readonly TaskCompletionSource<BitmapSource?> _result = new();
    PhysicalPoint? _dragStart;
    PhysicalRect? _pressedTarget;
    PhysicalRect? _highlight;
    bool _finished;

    CaptureSession(FrozenDesktop desktop, CaptureMode mode)
    {
        Desktop = desktop;
        Mode = mode;
    }

    public FrozenDesktop Desktop { get; }
    public CaptureMode Mode { get; private set; }

    /// <summary>Freezes the screen and lets the user pick an area. Null when cancelled.</summary>
    public static Task<BitmapSource?> RunAsync(CaptureMode mode)
    {
        var session = new CaptureSession(FrozenDesktop.Capture(), mode);
        session.Open();
        return session._result.Task;
    }

    void Open()
    {
        var cursor = NativeCursor.Position;
        var active = Desktop.MonitorAt(cursor) ?? Desktop.Monitors.FirstOrDefault(m => m.IsPrimary) ?? Desktop.Monitors[0];
        foreach (var monitor in Desktop.Monitors)
        {
            var overlay = new OverlayWindow(this, monitor, hostsToolbar: monitor == active);
            _overlays.Add(overlay);
            overlay.Show();
        }
        _overlays.First(o => o.Monitor == active).Activate();
        PointerMoved(cursor);
    }

    public void SetMode(CaptureMode mode)
    {
        if (_finished || _dragStart is not null || mode == Mode) return;
        Mode = mode;
        _pressedTarget = null;
        SetHighlight(null, animate: false);
        foreach (var overlay in _overlays) overlay.OnModeChanged(mode);
        PointerMoved(NativeCursor.Position);
    }

    public void PointerMoved(PhysicalPoint p)
    {
        if (_finished) return;
        switch (Mode)
        {
            case CaptureMode.Region:
                SetHighlight(_dragStart is { } start ? PhysicalRect.FromPoints(start, p) : null, animate: false);
                break;
            case CaptureMode.Window:
                SetHighlight(Desktop.WindowAt(p)?.Bounds.Intersect(Desktop.Bounds), animate: true);
                break;
            case CaptureMode.Screen:
                SetHighlight(Desktop.MonitorAt(p)?.Bounds, animate: true);
                break;
        }

        var info = Describe(p);
        foreach (var overlay in _overlays) overlay.ShowPointer(p, info);
    }

    public void PointerPressed(PhysicalPoint p)
    {
        if (_finished) return;
        if (Mode == CaptureMode.Region)
        {
            _dragStart = p;
            foreach (var overlay in _overlays) overlay.SetToolbarHidden(true);
            PointerMoved(p);
        }
        else
        {
            _pressedTarget = _highlight;
        }
    }

    /// <summary>Captures happen on release, so the click never reaches the app underneath.</summary>
    public void PointerReleased(PhysicalPoint p)
    {
        if (_finished) return;
        if (Mode == CaptureMode.Region)
        {
            if (_dragStart is not { } start) return;
            _dragStart = null;
            var selection = PhysicalRect.FromPoints(start, p);
            if (selection.Width >= MinimumSize && selection.Height >= MinimumSize)
            {
                Complete(selection);
                return;
            }
            foreach (var overlay in _overlays) overlay.SetToolbarHidden(false);
            PointerMoved(p);
        }
        else
        {
            if (_pressedTarget is { } target && target == _highlight) Complete(target);
            _pressedTarget = null;
        }
    }

    /// <summary>Enter: all displays in Screen mode, the hovered window in Window mode.</summary>
    public void Confirm()
    {
        if (Mode == CaptureMode.Screen) Complete(Desktop.Bounds);
        else if (Mode == CaptureMode.Window && _highlight is { } window) Complete(window);
    }

    public void Cancel() => Finish(null);

    void Complete(PhysicalRect rect)
    {
        var visible = rect.Intersect(Desktop.Bounds);
        Finish(visible.IsEmpty ? null : Desktop.Crop(visible));
    }

    void Finish(BitmapSource? image)
    {
        if (_finished) return;
        _finished = true;
        foreach (var overlay in _overlays) overlay.Close();
        _result.TrySetResult(image);
    }

    void SetHighlight(PhysicalRect? rect, bool animate)
    {
        if (rect is { IsEmpty: true }) rect = null;
        if (rect == _highlight) return;
        _highlight = rect;
        foreach (var overlay in _overlays) overlay.ShowHighlight(rect, animate);
    }

    PointerInfo Describe(PhysicalPoint p)
    {
        switch (Mode)
        {
            case CaptureMode.Region:
                var color = Desktop.PixelAt(p);
                var hex = color is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "";
                return _dragStart is not null && _highlight is { } selection
                    ? new PointerInfo(selection.SizeText, hex, ShowLoupe: true, color)
                    : new PointerInfo($"{p.X}, {p.Y}", hex, ShowLoupe: true, color);

            case CaptureMode.Window:
                return Desktop.WindowAt(p) is { } window
                    ? new PointerInfo(string.IsNullOrWhiteSpace(window.Title) ? "Untitled window" : window.Title, _highlight?.SizeText ?? "", false, null)
                    : new PointerInfo("No window here", "Move over a window", false, null);

            default:
                return Desktop.MonitorAt(p) is { } monitor
                    ? new PointerInfo(
                        monitor.IsPrimary ? $"{monitor.DisplayName} · main" : monitor.DisplayName,
                        $"{monitor.Bounds.SizeText} · {Math.Round(monitor.Scale * 100)}% scale", false, null)
                    : new PointerInfo("No display here", "", false, null);
        }
    }
}

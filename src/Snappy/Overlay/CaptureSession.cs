using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Snappy.Capture;
using Snappy.Geometry;
using Snappy.Interop;
using Snappy.Ui;

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

    /// <summary>How long a click waits for a covered window to render before using the screen instead.</summary>
    static readonly TimeSpan WindowRenderTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the pointer rests on a covered window before it's rendered for the preview: the
    /// highlight's move time, so the picture doesn't change mid-move, and windows the pointer only
    /// passes over aren't rendered at all (each render makes Windows redraw that window).
    /// </summary>
    static readonly TimeSpan HoverSettleTime = TimeSpan.FromMilliseconds(160);

    readonly List<OverlayWindow> _overlays = [];
    readonly TaskCompletionSource<BitmapSource?> _result = new();
    readonly Dictionary<nint, Task<BitmapSource?>> _windowImages = [];

    // Pointer moves are applied at most once per display refresh. Gaming mice report up to 1000
    // moves a second; redrawing the card and magnifier for each one asks the render thread for far
    // more frames than the display shows, until it falls behind and the whole overlay stalls.
    readonly TimeSpan _pointerInterval = TimeSpan.FromSeconds(1.0 / Motion.FrameRate);
    readonly Stopwatch _sincePointerUpdate = Stopwatch.StartNew();
    readonly DispatcherTimer _pendingPointerUpdate;

    PhysicalPoint? _dragStart;
    PhysicalRect? _pressedTarget;
    PhysicalRect? _highlight;
    WindowInfo? _hoveredWindow;
    bool _finished;

    CaptureSession(FrozenDesktop desktop, CaptureMode mode)
    {
        Desktop = desktop;
        Mode = mode;
        _pendingPointerUpdate = new DispatcherTimer(DispatcherPriority.Input) { Interval = _pointerInterval };
        _pendingPointerUpdate.Tick += (_, _) => UpdatePointer();
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
        HoverWindow(null);
        foreach (var overlay in _overlays) overlay.OnModeChanged(mode);
        PointerMoved(NativeCursor.Position);
    }

    /// <summary>The mouse moved; the overlays catch up within one display refresh.</summary>
    public void PointerMovedSoon()
    {
        if (_finished) return;
        if (_sincePointerUpdate.Elapsed >= _pointerInterval) UpdatePointer();
        else if (!_pendingPointerUpdate.IsEnabled) _pendingPointerUpdate.Start(); // the last move still lands
    }

    void UpdatePointer()
    {
        _pendingPointerUpdate.Stop();
        _sincePointerUpdate.Restart();
        PointerMoved(NativeCursor.Position);
    }

    /// <summary>Applies a waiting pointer move now, so a click acts on what's under the cursor.</summary>
    void CatchUpPointer()
    {
        if (_pendingPointerUpdate.IsEnabled) UpdatePointer();
    }

    void PointerMoved(PhysicalPoint p)
    {
        if (_finished) return;
        switch (Mode)
        {
            case CaptureMode.Region:
                SetHighlight(_dragStart is { } start ? PhysicalRect.FromPoints(start, p) : null, animate: false);
                break;
            case CaptureMode.Window:
                var window = Desktop.WindowAt(p);
                SetHighlight(window?.Bounds.Intersect(Desktop.Bounds), animate: true);
                HoverWindow(window);
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
        CatchUpPointer();
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
        CatchUpPointer();
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
            if (_pressedTarget is { } target && target == _highlight)
            {
                if (Mode == CaptureMode.Window && _hoveredWindow is { } window) CompleteWindow(window);
                else Complete(target);
            }
            _pressedTarget = null;
        }
    }

    /// <summary>Enter: all displays in Screen mode, the hovered window in Window mode.</summary>
    public void Confirm()
    {
        if (Mode == CaptureMode.Screen) Complete(Desktop.Bounds);
        else if (Mode == CaptureMode.Window && _hoveredWindow is { } window) CompleteWindow(window);
    }

    public void Cancel() => Finish(null);

    void Complete(PhysicalRect rect)
    {
        var visible = rect.Intersect(Desktop.Bounds);
        Finish(visible.IsEmpty ? null : Desktop.Crop(visible));
    }

    /// <summary>
    /// A window with something in front of it is rendered by itself, so the capture shows the
    /// window and not what covered it. Uncovered windows come from the frozen desktop like
    /// everything else, which is also the fallback when the window can't be rendered.
    /// </summary>
    async void CompleteWindow(WindowInfo window)
    {
        var visible = window.Bounds.Intersect(Desktop.Bounds);
        if (!window.IsCovered || visible.IsEmpty)
        {
            Complete(visible);
            return;
        }
        if (_finished) return;
        _finished = true;
        CloseOverlays();

        var render = WindowImage(window);
        var image = await Task.WhenAny(render, Task.Delay(WindowRenderTimeout)) == render ? render.Result : null;
        _result.TrySetResult(image ?? Desktop.Crop(visible));
    }

    void Finish(BitmapSource? image)
    {
        if (_finished) return;
        _finished = true;
        CloseOverlays();
        _result.TrySetResult(image);
    }

    void CloseOverlays()
    {
        _pendingPointerUpdate.Stop();
        foreach (var overlay in _overlays) overlay.Close();
    }

    /// <summary>In Window mode, shows a covered window rendered by itself, the way it will be captured.</summary>
    async void HoverWindow(WindowInfo? window)
    {
        if (window == _hoveredWindow) return;
        _hoveredWindow = window;
        foreach (var overlay in _overlays) overlay.HideWindowImage();
        if (window is not { IsCovered: true }) return;

        await Task.Delay(HoverSettleTime);
        if (_finished || window != _hoveredWindow) return;
        var image = await WindowImage(window);
        if (_finished || window != _hoveredWindow || image is null) return;
        var visible = window.Bounds.Intersect(Desktop.Bounds);
        foreach (var overlay in _overlays) overlay.ShowWindowImage(visible, image);
    }

    /// <summary>The window's visible part rendered by itself, made once per capture on a worker thread.</summary>
    Task<BitmapSource?> WindowImage(WindowInfo window)
    {
        if (_windowImages.TryGetValue(window.Handle, out var render)) return render;

        var visible = window.Bounds.Intersect(Desktop.Bounds);
        var center = new PhysicalPoint(visible.X + visible.Width / 2, visible.Y + visible.Height / 2);
        double scale = Desktop.MonitorAt(center)?.Scale ?? 1;
        render = Task.Run(() =>
        {
            try
            {
                return WindowCapturer.TryCapture(window.Handle, visible, scale);
            }
            catch (Exception)
            {
                return null; // the window closed meanwhile, or Windows refused; the screen still works
            }
        });
        _windowImages[window.Handle] = render;
        return render;
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

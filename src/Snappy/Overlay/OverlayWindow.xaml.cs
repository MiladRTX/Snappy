using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Snappy.Capture;
using Snappy.Geometry;
using Snappy.Interop;
using Snappy.Ui;

namespace Snappy.Overlay;

/// <summary>
/// Full-screen overlay for one display. Shows that display's slice of the frozen desktop at 1:1
/// and draws the dim layer, the highlight, the cursor card and (on one display) the toolbar.
/// </summary>
internal partial class OverlayWindow : Window
{
    const double SegmentWidth = 120;
    const double InactiveSegmentOpacity = 0.65;
    const double MinimumGlassTint = 0.55;
    const double HoveredSegmentOpacity = 0.85;
    const double ToolbarRadius = 16;
    const double DimOpacity = 0.5;
    const int LoupeRadius = 7; // the magnifier shows 15 x 15 pixels

    static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(
        nameof(Highlight), typeof(Rect), typeof(OverlayWindow),
        new PropertyMetadata(new Rect(), (d, _) => ((OverlayWindow)d).RenderHighlight()));

    readonly CaptureSession _session;
    readonly bool _hostsToolbar;
    readonly RectangleGeometry _dimOuter = new();
    readonly RectangleGeometry _dimHole = new();
    PhysicalRect? _highlight;

    public OverlayWindow(CaptureSession session, MonitorInfo monitor, bool hostsToolbar)
    {
        InitializeComponent();
        _session = session;
        _hostsToolbar = hostsToolbar;
        Monitor = monitor;

        Frozen.Source = session.Desktop.View(monitor.Bounds);
        LoupeBrush.ImageSource = session.Desktop.Image;
        FrostBrush.ImageSource = Frozen.Source;
        HintFrostBrush.ImageSource = Frozen.Source;
        Dim.Data = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { _dimOuter, _dimHole } };
        OnModeChanged(session.Mode, animate: false);
        WindowPlacement.Pin(this, monitor.Bounds, topmost: true, cloakUntilRendered: true);

        // Laid out (still transparent) from the start, so the glass can be aimed before it slides in.
        if (hostsToolbar) Toolbar.Visibility = Visibility.Visible;
        Frost.SizeChanged += (_, e) => Frost.Clip = new RectangleGeometry(new Rect(e.NewSize), ToolbarRadius, ToolbarRadius);
        HintFrost.SizeChanged += (_, e) =>
            HintFrost.Clip = new RectangleGeometry(new Rect(e.NewSize), e.NewSize.Height / 2, e.NewSize.Height / 2);
        // The hint pill changes width with the mode; re-aim once layout has settled.
        FrostImage.SizeChanged += (_, _) => Dispatcher.InvokeAsync(AimGlass, DispatcherPriority.Loaded);
        HintFrostImage.SizeChanged += (_, _) => Dispatcher.InvokeAsync(AimGlass, DispatcherPriority.Loaded);
        foreach (var (button, _) in Segments)
        {
            button.MouseEnter += (_, _) => FadeSegment(button, HoveredSegmentOpacity);
            button.MouseLeave += (_, _) => FadeSegment(button, InactiveSegmentOpacity);
        }

        SizeChanged += (_, _) =>
        {
            _dimOuter.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
            ShowHighlight(_highlight, animate: false);
        };
        DpiChanged += (_, _) => ShowHighlight(_highlight, animate: false);
        ContentRendered += (_, _) => PlayIntro();
        Closed += (_, _) => _session.Cancel(); // Alt+F4 on any overlay cancels the capture

        MouseLeftButtonDown += (_, e) =>
        {
            if (IsFromToolbar(e)) return;
            CaptureMouse(); // keeps mouse events coming while a drag crosses onto another display
            _session.PointerPressed(NativeCursor.Position);
        };
        MouseMove += (_, _) => _session.PointerMoved(NativeCursor.Position);
        MouseLeftButtonUp += (_, e) =>
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (!IsFromToolbar(e)) _session.PointerReleased(NativeCursor.Position);
        };
        MouseRightButtonUp += (_, _) => _session.Cancel();
        PreviewKeyDown += OnKeyDown;
    }

    public MonitorInfo Monitor { get; }

    /// <summary>The highlighted area in this window's DIPs. Animated when hovering between targets.</summary>
    Rect Highlight
    {
        get => (Rect)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Toolbar segments and their hints, in <see cref="CaptureMode"/> order.</summary>
    (RadioButton Button, FrameworkElement Hint)[] Segments =>
        [(RegionButton, RegionHint), (WindowButton, WindowHint), (ScreenButton, ScreenHint)];

    public void OnModeChanged(CaptureMode mode, bool animate = true)
    {
        var segments = Segments;
        for (int i = 0; i < segments.Length; i++)
        {
            var (button, hint) = segments[i];
            bool active = i == (int)mode;
            button.IsChecked = active;
            Motion.Apply(button, OpacityProperty, active ? 1 : InactiveSegmentOpacity, 220, animate);
            hint.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }
        if (animate) segments[(int)mode].Hint.BeginAnimation(OpacityProperty, Motion.Enter(0, 1, 260));
        Motion.Apply(PillShift, TranslateTransform.XProperty, (int)mode * SegmentWidth, 280, animate);

        Cursor = mode == CaptureMode.Region ? Cursors.Cross : Cursors.Arrow;
    }

    /// <summary>Shows the part of <paramref name="rect"/> that lies on this display.</summary>
    public void ShowHighlight(PhysicalRect? rect, bool animate)
    {
        _highlight = rect;
        var visible = rect?.Intersect(Monitor.Bounds) ?? default;
        var target = visible.IsEmpty ? new Rect() : visible.ToDip(Monitor.Bounds.Origin, Scale);
        var current = Highlight;

        if (animate && HasArea(current) && HasArea(target))
        {
            BeginAnimation(HighlightProperty, Motion.Move(current, target, 180));
        }
        else
        {
            BeginAnimation(HighlightProperty, null);
            Highlight = target;
        }
    }

    public void ShowPointer(PhysicalPoint p, PointerInfo info)
    {
        // Off this display, or over the toolbar where the card would only cover the controls.
        if (!Monitor.Bounds.Contains(p) || (_hostsToolbar && Toolbar.IsMouseOver))
        {
            Card.Visibility = Visibility.Collapsed;
            return;
        }

        CardPrimary.Text = info.Primary;
        CardSecondary.Text = info.Secondary;
        CardSecondary.Visibility = info.Secondary.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Swatch.Visibility = info.Color is null ? Visibility.Collapsed : Visibility.Visible;
        if (info.Color is { } color) Swatch.Background = new SolidColorBrush(color);
        Loupe.Visibility = info.ShowLoupe ? Visibility.Visible : Visibility.Collapsed;
        if (info.ShowLoupe)
        {
            var desktop = _session.Desktop.Bounds;
            int size = LoupeRadius * 2 + 1;
            LoupeBrush.Viewbox = new Rect(p.X - desktop.X - LoupeRadius, p.Y - desktop.Y - LoupeRadius, size, size);
        }

        Card.Visibility = Visibility.Visible;
        Card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var card = Card.DesiredSize;
        double scale = Scale;
        double x = (p.X - Monitor.Bounds.X) / scale, y = (p.Y - Monitor.Bounds.Y) / scale;

        // Sit below-right of the cursor, flipping to stay on the display.
        double left = x + 20, top = y + 24;
        if (left + card.Width > ActualWidth - 8) left = x - 16 - card.Width;
        if (top + card.Height > ActualHeight - 8) top = y - 16 - card.Height;
        Canvas.SetLeft(Card, Math.Max(8, left));
        Canvas.SetTop(Card, Math.Max(8, top));
    }

    public void SetToolbarHidden(bool hidden)
    {
        if (!_hostsToolbar) return;
        Toolbar.IsHitTestVisible = !hidden;
        Toolbar.BeginAnimation(OpacityProperty, hidden ? Motion.Exit(0, 120) : Motion.Enter(1, 220));
    }

    void PlayIntro()
    {
        WindowPlacement.Uncloak(this);
        // The frozen image matches the real screen, so this reads as the screen itself dimming.
        Dim.BeginAnimation(OpacityProperty, Motion.Enter(0, DimOpacity, 240));
        if (!_hostsToolbar) return;
        AimGlass();
        Toolbar.BeginAnimation(OpacityProperty, Motion.Enter(0, 1, 260));
        ToolbarShift.BeginAnimation(TranslateTransform.YProperty, Motion.Enter(-16, 0, 480));
    }

    /// <summary>Points each glass panel's blur at the part of the frozen screen behind it.</summary>
    void AimGlass()
    {
        AimFrost(FrostImage, FrostBrush, ToolbarTint);
        AimFrost(HintFrostImage, HintFrostBrush, HintTint);
    }

    void AimFrost(FrameworkElement area, ImageBrush brush, UIElement tint)
    {
        // Where the toolbar comes to rest, not where the slide-in starts.
        var topLeft = area.TranslatePoint(new Point(0, 0), this);
        topLeft.Y -= ToolbarShift.Y;
        double scale = Scale;
        var behind = new Rect(topLeft.X * scale, topLeft.Y * scale, area.ActualWidth * scale, area.ActualHeight * scale);
        brush.Viewbox = behind;

        // Adaptive tint, like macOS vibrancy: thin over content that already contrasts with the
        // text, so the glass stays see-through, and thicker over content that doesn't (bright
        // windows in the dark theme, dark ones in the light theme), so the text stays readable.
        double luminance = _session.Desktop.MeanLuminance(new PhysicalRect(
            Monitor.Bounds.X + (int)behind.X, Monitor.Bounds.Y + (int)behind.Y, (int)behind.Width, (int)behind.Height));
        double glare = Theme.IsDark ? luminance : 1 - luminance;
        tint.Opacity = MinimumGlassTint + (1 - MinimumGlassTint) * glare;
    }

    void FadeSegment(RadioButton button, double opacity)
    {
        if (button.IsChecked != true) button.BeginAnimation(OpacityProperty, Motion.Enter(opacity, 150));
    }

    void RenderHighlight()
    {
        var r = Highlight;
        bool visible = HasArea(r);
        _dimHole.Rect = visible ? r : new Rect();
        SelectionBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        // Exactly 1 (or 2) physical pixels thick. Drawn outside the area so the border never
        // covers captured pixels, except for whole displays where outside is off-screen.
        double pixel = 1 / Scale;
        double thickness = (_session.Mode == CaptureMode.Region ? 1 : 2) * pixel;
        double outset = _session.Mode == CaptureMode.Screen ? 0 : thickness;
        SelectionBorder.BorderThickness = new Thickness(thickness);
        Canvas.SetLeft(SelectionBorder, r.X - outset);
        Canvas.SetTop(SelectionBorder, r.Y - outset);
        SelectionBorder.Width = r.Width + 2 * outset;
        SelectionBorder.Height = r.Height + 2 * outset;
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: _session.Cancel(); break;
            case Key.R: _session.SetMode(CaptureMode.Region); break;
            case Key.W: _session.SetMode(CaptureMode.Window); break;
            case Key.F or Key.S: _session.SetMode(CaptureMode.Screen); break;
            case Key.Enter: _session.Confirm(); break;
            default: return;
        }
        e.Handled = true;
    }

    void OnModeClick(object sender, RoutedEventArgs e) => _session.SetMode((CaptureMode)((FrameworkElement)sender).Tag);

    void OnCloseClick(object sender, RoutedEventArgs e) => _session.Cancel();

    bool IsFromToolbar(MouseEventArgs e) => e.OriginalSource is Visual source && Toolbar.IsAncestorOf(source);

    static bool HasArea(Rect r) => r.Width > 0 && r.Height > 0;
}

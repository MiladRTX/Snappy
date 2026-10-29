using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    const double HoveredSegmentOpacity = 0.85;
    const double DimOpacity = 0.5;
    const int LoupeRadius = 7; // the magnifier shows 15 x 15 pixels

    static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(
        nameof(Highlight), typeof(Rect), typeof(OverlayWindow),
        new PropertyMetadata(new Rect(), (d, _) => ((OverlayWindow)d).RenderHighlight()));

    readonly CaptureSession _session;
    readonly bool _hostsToolbar;
    PhysicalRect? _highlight;

    public OverlayWindow(CaptureSession session, MonitorInfo monitor, bool hostsToolbar)
    {
        InitializeComponent();
        _session = session;
        _hostsToolbar = hostsToolbar;
        Monitor = monitor;

        Frozen.Source = session.Desktop.View(monitor.Bounds);
        LoupeBrush.ImageSource = session.Desktop.Image;
        LitBrush.ImageSource = Frozen.Source;
        OnModeChanged(session.Mode, animate: false);
        WindowPlacement.Pin(this, monitor.Bounds, topmost: true, cloakUntilRendered: true);

        foreach (var button in Segments)
        {
            button.MouseEnter += (_, _) => FadeSegment(button, HoveredSegmentOpacity);
            button.MouseLeave += (_, _) => FadeSegment(button, InactiveSegmentOpacity);
        }

        SizeChanged += (_, _) => ShowHighlight(_highlight, animate: false);
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

    /// <summary>Toolbar segments, in <see cref="CaptureMode"/> order.</summary>
    RadioButton[] Segments => [RegionButton, WindowButton, ScreenButton];

    public void OnModeChanged(CaptureMode mode, bool animate = true)
    {
        var segments = Segments;
        for (int i = 0; i < segments.Length; i++)
        {
            bool active = i == (int)mode;
            segments[i].IsChecked = active;
            Motion.Apply(segments[i], OpacityProperty, active ? 1 : InactiveSegmentOpacity, 160, animate);
        }
        Motion.Apply(PillShift, TranslateTransform.XProperty, (int)mode * SegmentWidth, 220, animate);

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
            BeginAnimation(HighlightProperty, Motion.Enter(current, target, 150));
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
        Toolbar.Visibility = Visibility.Visible;
        Toolbar.BeginAnimation(OpacityProperty, Motion.Enter(0, 1, 260));
        ToolbarShift.BeginAnimation(TranslateTransform.YProperty, Motion.Enter(-16, 0, 480));
    }

    void FadeSegment(RadioButton button, double opacity)
    {
        if (button.IsChecked != true) button.BeginAnimation(OpacityProperty, Motion.Enter(opacity, 150));
    }

    void RenderHighlight()
    {
        var r = Highlight;
        bool visible = HasArea(r);
        Lit.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SelectionBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        // The undimmed area shows the frozen pixels under it, one image pixel per screen pixel.
        double scale = Scale;
        Canvas.SetLeft(Lit, r.X);
        Canvas.SetTop(Lit, r.Y);
        Lit.Width = r.Width;
        Lit.Height = r.Height;
        LitBrush.Viewbox = new Rect(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale);

        // Exactly 1 (or 2) physical pixels thick. Drawn outside the area so the border never
        // covers captured pixels, except for whole displays where outside is off-screen.
        double pixel = 1 / scale;
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

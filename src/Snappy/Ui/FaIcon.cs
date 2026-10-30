using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Snappy.Ui;

/// <summary>
/// One Font Awesome icon: its outline and the size of the canvas it was drawn on (the SVG
/// viewBox). The icons are defined in Themes/Icons.xaml.
/// </summary>
public sealed class FaGlyph
{
    // Spelled out: inside Snappy.*, a bare "Geometry" means the Snappy.Geometry namespace.
    public System.Windows.Media.Geometry Data { get; set; } = System.Windows.Media.Geometry.Empty;
    public double Width { get; set; } = 512;
    public double Height { get; set; } = 512;

    GuidelineSet? _edges;

    /// <summary>
    /// The x of every vertical and the y of every horizontal straight edge, so the renderer can
    /// put each one on a pixel boundary (crisp strokes instead of a gray half-covered column).
    /// </summary>
    internal GuidelineSet Edges => _edges ??= FindEdges(Data);

    static GuidelineSet FindEdges(System.Windows.Media.Geometry data)
    {
        const double minLength = 16; // in viewBox units; shorter flats are the ends of curves
        var xs = new SortedSet<double>();
        var ys = new SortedSet<double>();
        foreach (var figure in PathGeometry.CreateFromGeometry(data).Figures)
        {
            var from = figure.StartPoint;
            foreach (var segment in figure.Segments)
            {
                IEnumerable<Point> points = segment switch
                {
                    LineSegment line => [line.Point],
                    PolyLineSegment poly => poly.Points,
                    _ => [],
                };
                foreach (var to in points)
                {
                    if (Math.Abs(to.X - from.X) < 0.01 && Math.Abs(to.Y - from.Y) >= minLength) xs.Add(Math.Round(to.X, 2));
                    if (Math.Abs(to.Y - from.Y) < 0.01 && Math.Abs(to.X - from.X) >= minLength) ys.Add(Math.Round(to.Y, 2));
                    from = to;
                }
                from = segment switch
                {
                    ArcSegment arc => arc.Point,
                    BezierSegment bezier => bezier.Point3,
                    QuadraticBezierSegment quadratic => quadratic.Point2,
                    PolyBezierSegment polyBezier when polyBezier.Points.Count > 0 => polyBezier.Points[^1],
                    PolyQuadraticBezierSegment polyQuadratic when polyQuadratic.Points.Count > 0 => polyQuadratic.Points[^1],
                    _ => from,
                };
            }
        }
        var edges = new GuidelineSet([.. xs], [.. ys]);
        edges.Freeze();
        return edges;
    }
}

/// <summary>
/// Draws a <see cref="FaGlyph"/> in the surrounding text color, scaled to fit while keeping the
/// icon's own proportions and padding, so icons of different widths line up like font glyphs.
/// </summary>
public sealed class FaIcon : FrameworkElement
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(FaGlyph), typeof(FaIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Inherited like text color, so hover and theme changes reach the icon too.</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(FaIcon),
        new FrameworkPropertyMetadata(SystemColors.ControlTextBrush,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public FaGlyph? Glyph
    {
        get => (FaGlyph?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// 16 x 16 unless Width and Height say otherwise. Asking for more than Width allows makes WPF
    /// lay the icon out at 16 anyway and clip it to Width, cutting off its right and bottom edges.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(16, availableSize.Width), Math.Min(16, availableSize.Height));

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Glyph is not { } glyph || glyph.Width <= 0 || glyph.Height <= 0) return;
        var shape = glyph.Data.Bounds;
        if (shape.IsEmpty) return;

        // Placed in device pixels with the shape's outer edges on whole pixels. Left where the
        // plain scale puts them, an edge can straddle two pixels and render as a half-covered gray
        // row, which looks like the icon was cut off. Hosts use layout rounding, so (0,0) is on a
        // pixel already.
        var dpi = VisualTreeHelper.GetDpi(this);
        double width = ActualWidth * dpi.DpiScaleX, height = ActualHeight * dpi.DpiScaleY;
        double scale = Math.Min(width / glyph.Width, height / glyph.Height);
        double left = Math.Round((width - glyph.Width * scale) / 2 + shape.X * scale);
        double top = Math.Round((height - glyph.Height * scale) / 2 + shape.Y * scale);
        double scaleX = Math.Max(1, Math.Round(shape.Width * scale)) / shape.Width;
        double scaleY = Math.Max(1, Math.Round(shape.Height * scale)) / shape.Height;

        drawingContext.PushTransform(new MatrixTransform(
            scaleX / dpi.DpiScaleX, 0, 0, scaleY / dpi.DpiScaleY,
            (left - shape.X * scaleX) / dpi.DpiScaleX, (top - shape.Y * scaleY) / dpi.DpiScaleY));
        drawingContext.PushGuidelineSet(glyph.Edges);
        drawingContext.DrawGeometry(Foreground, null, glyph.Data);
        drawingContext.Pop();
        drawingContext.Pop();
    }

    /// <summary>Moving to a monitor with different scaling moves the pixel grid.</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();
}

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

    /// <summary>16 x 16 unless Width and Height say otherwise.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(16, 16);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Glyph is not { } glyph || glyph.Width <= 0 || glyph.Height <= 0) return;

        double scale = Math.Min(ActualWidth / glyph.Width, ActualHeight / glyph.Height);
        double x = (ActualWidth - glyph.Width * scale) / 2;
        double y = (ActualHeight - glyph.Height * scale) / 2;
        drawingContext.PushTransform(new MatrixTransform(scale, 0, 0, scale, x, y));
        drawingContext.DrawGeometry(Foreground, null, glyph.Data);
        drawingContext.Pop();
    }
}

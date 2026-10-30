using System.Windows;
using Snappy.Overlay;

namespace Snappy.Tests;

public class HighlightSnappingTests
{
    [Theory]
    [InlineData(1.25)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Every_edge_lands_on_a_screen_pixel_near_where_it_was(double scale)
    {
        var animated = new Rect(10.3, 20.1, 100.5, 50.2); // a highlight halfway through a move

        var snapped = OverlayWindow.SnapToPixels(animated, scale);

        foreach (var (edge, original) in new[]
        {
            (snapped.Left, animated.Left), (snapped.Top, animated.Top),
            (snapped.Right, animated.Right), (snapped.Bottom, animated.Bottom),
        })
        {
            double pixels = edge * scale;
            Assert.Equal(Math.Round(pixels), pixels, 9);
            Assert.True(Math.Abs(pixels - original * scale) <= 0.5, $"{edge} moved more than half a pixel from {original}");
        }
    }

    [Fact]
    public void Nothing_to_show_stays_empty()
    {
        Assert.True(OverlayWindow.SnapToPixels(new Rect(), 1.25).IsEmpty);
        Assert.True(OverlayWindow.SnapToPixels(new Rect(10, 10, 0.3, 0.3), 1.0).IsEmpty); // inside one pixel
    }
}

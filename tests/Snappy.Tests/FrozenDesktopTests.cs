using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snappy.Capture;
using Snappy.Geometry;

namespace Snappy.Tests;

public class FrozenDesktopTests
{
    static readonly PhysicalRect Desktop = new(-1920, 0, 4480, 1440);

    [Fact]
    public void Secondary_display_maps_to_the_left_of_the_image()
    {
        Assert.Equal(new Int32Rect(0, 8, 1920, 1080), FrozenDesktop.ToImageRect(Desktop, new(-1920, 8, 1920, 1080)));
    }

    [Fact]
    public void Primary_display_maps_after_the_secondary()
    {
        Assert.Equal(new Int32Rect(1920, 0, 2560, 1440), FrozenDesktop.ToImageRect(Desktop, new(0, 0, 2560, 1440)));
    }

    [Fact]
    public void Selection_hanging_off_the_desktop_is_clipped()
    {
        Assert.Equal(new Int32Rect(4320, 1400, 160, 40), FrozenDesktop.ToImageRect(Desktop, new(2400, 1400, 500, 500)));
    }

    [Fact]
    public void Selection_outside_the_desktop_maps_to_nothing()
    {
        Assert.Null(FrozenDesktop.ToImageRect(Desktop, new(-5000, 0, 100, 100)));
    }

    [Fact]
    public void Crop_returns_exactly_the_selected_pixels()
    {
        // A 4x2 "desktop" at x = -2 whose pixels encode their own coordinates.
        var bounds = new PhysicalRect(-2, 0, 4, 2);
        var pixels = new byte[4 * 2 * 4];
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 4; x++)
        {
            int i = (y * 4 + x) * 4;
            pixels[i] = (byte)x;      // blue
            pixels[i + 1] = (byte)y;  // green
            pixels[i + 2] = 200;      // red
        }
        var image = BitmapSource.Create(4, 2, 96, 96, PixelFormats.Bgr32, null, pixels, 16);
        var desktop = new FrozenDesktop(image, bounds, [], []);

        var crop = desktop.Crop(new PhysicalRect(-1, 1, 2, 1));

        Assert.Equal(2, crop.PixelWidth);
        Assert.Equal(1, crop.PixelHeight);
        Assert.Equal(96, crop.DpiX);
        var result = new byte[8];
        crop.CopyPixels(result, 8, 0);
        Assert.Equal([1, 1, 200], result[0..3]);
        Assert.Equal([2, 1, 200], result[4..7]);
    }

    [Fact]
    public void PixelAt_reads_the_color_under_a_desktop_point()
    {
        var pixels = new byte[] { 10, 20, 30, 0, 40, 50, 60, 0 };
        var image = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgr32, null, pixels, 8);
        var desktop = new FrozenDesktop(image, new PhysicalRect(-1, 0, 2, 1), [], []);

        Assert.Equal(Color.FromRgb(60, 50, 40), desktop.PixelAt(new(0, 0)));
        Assert.Null(desktop.PixelAt(new(1, 0)));
    }
}

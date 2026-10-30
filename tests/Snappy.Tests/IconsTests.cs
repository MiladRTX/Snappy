using System.IO.Packaging;
using System.Windows;
using Snappy.Ui;

namespace Snappy.Tests;

public class IconsTests
{
    static readonly string[] UsedIcons =
    [
        "Icon.Region", "Icon.Window", "Icon.Display", "Icon.AllDisplays", "Icon.Folder", "Icon.Settings",
        "Icon.Quit", "Icon.Warning", "Icon.Close", "Icon.Keyboard", "Icon.Info", "Icon.File", "Icon.GitHub",
    ];

    static ResourceDictionary LoadIcons()
    {
        _ = PackUriHelper.UriSchemePack; // registers the pack:// scheme outside a running app
        return (ResourceDictionary)Application.LoadComponent(new Uri("/Snappy;component/Themes/Icons.xaml", UriKind.Relative));
    }

    [Fact]
    public void Every_icon_the_app_uses_is_defined()
    {
        var icons = LoadIcons();

        foreach (var key in UsedIcons) Assert.IsType<Glyph>(icons[key]);
    }

    [Fact]
    public void Straight_edges_are_found_for_pixel_snapping()
    {
        var window = Assert.IsType<Glyph>(LoadIcons()["Icon.Window"]);

        // The frame's outer and inner sides, including the inner one "Z" closes; not the rounded
        // corners or the two round dots.
        Assert.Equal([20d, 44, 212, 236], window.Edges.GuidelinesX);
        Assert.Equal([36d, 60, 196, 220], window.Edges.GuidelinesY);
    }

    [Fact]
    public void Every_icon_parses_and_fits_its_viewbox()
    {
        foreach (var key in LoadIcons().Keys)
        {
            var glyph = Assert.IsType<Glyph>(LoadIcons()[key]);
            var bounds = glyph.Data.Bounds;

            Assert.False(bounds.IsEmpty, $"{key} has no shape");
            var box = new Rect(0, 0, glyph.Width, glyph.Height);
            Assert.True(box.Contains(bounds), $"{key} spills outside its {glyph.Width} x {glyph.Height} box: {bounds}");
        }
    }
}

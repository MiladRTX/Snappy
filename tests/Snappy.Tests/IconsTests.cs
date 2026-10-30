using System.IO.Packaging;
using System.Windows;
using Snappy.Ui;

namespace Snappy.Tests;

public class IconsTests
{
    static readonly string[] UsedIcons =
    [
        "Fa.CropSimple", "Fa.WindowMaximize", "Fa.Display", "Fa.Expand", "Fa.FolderOpen",
        "Fa.Gear", "Fa.PowerOff", "Fa.CircleExclamation", "Fa.Xmark",
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

        foreach (var key in UsedIcons) Assert.IsType<FaGlyph>(icons[key]);
    }

    [Fact]
    public void Straight_edges_are_found_for_pixel_snapping()
    {
        var window = Assert.IsType<FaGlyph>(LoadIcons()["Fa.WindowMaximize"]);

        // The outline's sides, top and bottom, and the slot in the title bar; not the rounded corners.
        Assert.Equal([0d, 512], window.Edges.GuidelinesX);
        Assert.Equal([64d, 128, 176, 448], window.Edges.GuidelinesY);
    }

    [Fact]
    public void Every_icon_parses_and_fits_its_viewbox()
    {
        foreach (var key in LoadIcons().Keys)
        {
            var glyph = Assert.IsType<FaGlyph>(LoadIcons()[key]);
            var bounds = glyph.Data.Bounds;

            Assert.False(bounds.IsEmpty, $"{key} has no shape");
            // Font Awesome 7 lets some icons overhang their box slightly (the gear's teeth).
            double overhang = glyph.Height / 16;
            var box = new Rect(-overhang, -overhang, glyph.Width + 2 * overhang, glyph.Height + 2 * overhang);
            Assert.True(box.Contains(bounds), $"{key} spills outside its {glyph.Width} x {glyph.Height} box: {bounds}");
        }
    }
}

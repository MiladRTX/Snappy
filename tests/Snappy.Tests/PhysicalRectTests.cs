using Snappy.Geometry;

namespace Snappy.Tests;

public class PhysicalRectTests
{
    // The layout these tests are modelled on: 2560x1440 @125% primary, 1920x1080 @100% to its left.
    static readonly PhysicalRect Primary = new(0, 0, 2560, 1440);
    static readonly PhysicalRect Secondary = new(-1920, 8, 1920, 1080);

    [Theory]
    [InlineData(10, 20, 110, 70)] // down-right
    [InlineData(110, 70, 10, 20)] // up-left
    [InlineData(110, 20, 10, 70)] // down-left
    [InlineData(10, 70, 110, 20)] // up-right
    public void FromPoints_normalizes_any_drag_direction(int x1, int y1, int x2, int y2)
    {
        var rect = PhysicalRect.FromPoints(new(x1, y1), new(x2, y2));

        Assert.Equal(new PhysicalRect(10, 20, 101, 51), rect);
    }

    [Fact]
    public void Dragging_corner_to_corner_covers_the_whole_monitor()
    {
        // The cursor can only reach pixel 2559, so the end pixel must be included.
        var rect = PhysicalRect.FromPoints(new(0, 0), new(2559, 1439));

        Assert.Equal(Primary, rect);
    }

    [Fact]
    public void Intersect_works_with_negative_coordinates()
    {
        var selection = PhysicalRect.FromEdges(-100, 500, 300, 900);

        Assert.Equal(PhysicalRect.FromEdges(-100, 500, 0, 900), selection.Intersect(Secondary));
        Assert.Equal(PhysicalRect.FromEdges(0, 500, 300, 900), selection.Intersect(Primary));
    }

    [Fact]
    public void Intersect_of_disjoint_rects_is_empty()
    {
        Assert.True(new PhysicalRect(0, 0, 10, 10).Intersect(new PhysicalRect(10, 0, 10, 10)).IsEmpty);
    }

    [Fact]
    public void Union_spans_the_virtual_desktop()
    {
        Assert.Equal(new PhysicalRect(-1920, 0, 4480, 1440), Primary.Union(Secondary));
        Assert.Equal(Primary, default(PhysicalRect).Union(Primary));
    }

    [Fact]
    public void Contains_excludes_right_and_bottom_edges()
    {
        Assert.True(Primary.Contains(new(2559, 1439)));
        Assert.False(Primary.Contains(new(2560, 0)));
        Assert.False(Primary.Contains(new(0, 1440)));
        Assert.True(Secondary.Contains(new(-1920, 8)));
        Assert.False(Secondary.Contains(new(-1920, 7)));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void ToDip_converts_back_to_the_same_physical_pixels(double scale)
    {
        var rect = new PhysicalRect(-1000, 100, 640, 480);

        var dip = rect.ToDip(Secondary.Origin, scale);

        Assert.Equal(920, dip.X * scale, precision: 6);
        Assert.Equal(92, dip.Y * scale, precision: 6);
        Assert.Equal(640, dip.Width * scale, precision: 6);
        Assert.Equal(480, dip.Height * scale, precision: 6);
    }
}

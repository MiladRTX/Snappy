using Snappy.Capture;
using Snappy.Geometry;

namespace Snappy.Tests;

public class WindowEnumeratorTests
{
    [Fact]
    public void A_window_is_covered_when_one_above_it_overlaps_it()
    {
        var windows = WindowEnumerator.InZOrder(
        [
            (1, "Settings", new PhysicalRect(700, 360, 800, 600)),   // topmost
            (2, "Discord", new PhysicalRect(1000, 0, 1280, 1380)),   // partly under Settings
            (3, "Notepad", new PhysicalRect(0, 0, 600, 300)),        // clear of both
            (4, "Desktop app", new PhysicalRect(0, 0, 2560, 1440)),  // under everything
        ]);

        Assert.Equal([false, true, false, true], windows.Select(w => w.IsCovered));
    }

    [Fact]
    public void Windows_that_only_touch_edges_do_not_cover_each_other()
    {
        var windows = WindowEnumerator.InZOrder(
        [
            (1, "Left", new PhysicalRect(0, 0, 500, 500)),
            (2, "Right", new PhysicalRect(500, 0, 500, 500)),
        ]);

        Assert.False(windows[1].IsCovered);
    }
}

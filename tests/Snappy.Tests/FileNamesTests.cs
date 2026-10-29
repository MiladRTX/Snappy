using Snappy.Output;

namespace Snappy.Tests;

public class FileNamesTests
{
    static readonly DateTime Time = new(2026, 10, 29, 14, 32, 5);

    [Fact]
    public void Default_pattern_produces_a_sortable_timestamp()
    {
        Assert.Equal("Snappy_2026-10-29_14-32-05", FileNames.Format("Snappy_{yyyy-MM-dd_HH-mm-ss}", Time));
    }

    [Fact]
    public void Characters_windows_forbids_are_replaced()
    {
        Assert.Equal("shot 14_32", FileNames.Format("shot {HH:mm}", Time));
        Assert.Equal("a_b_c", FileNames.Format("a/b?c", Time));
    }

    [Fact]
    public void Pattern_without_tokens_is_kept()
    {
        Assert.Equal("capture", FileNames.Format("capture", Time));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void Empty_names_fall_back_to_Snappy(string pattern)
    {
        Assert.Equal("Snappy", FileNames.Format(pattern, Time));
    }

    [Fact]
    public void UniquePath_counts_up_past_existing_files()
    {
        var taken = new HashSet<string> { @"C:\shots\a.png", @"C:\shots\a (2).png" };

        Assert.Equal(@"C:\shots\a (3).png", FileNames.UniquePath(@"C:\shots", "a", ".png", taken.Contains));
        Assert.Equal(@"C:\shots\b.png", FileNames.UniquePath(@"C:\shots", "b", ".png", taken.Contains));
    }
}

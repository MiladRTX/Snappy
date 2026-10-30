using Snappy.Settings;

namespace Snappy.Tests;

public class StartWithWindowsTests
{
    const string This = @"C:\Apps\Snappy\Snappy.exe";
    static readonly byte[] Enabled = [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    static readonly byte[] TurnedOff = [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    static StartupState Evaluate(string? command, byte[]? approval = null, bool exists = true) =>
        StartWithWindows.Evaluate(command, approval, This, _ => exists);

    [Fact]
    public void No_entry_is_off() => Assert.Equal(StartupState.Off, Evaluate(null));

    [Theory]
    [InlineData(@"""C:\Apps\Snappy\Snappy.exe"" --startup")]
    [InlineData(@"""C:\Apps\Snappy\Snappy.exe""")]      // written by Snappy 1.0.2 and earlier
    [InlineData(@"""c:\apps\snappy\SNAPPY.EXE"" --startup")] // Windows paths ignore case
    public void An_entry_for_this_copy_is_on(string command) => Assert.Equal(StartupState.On, Evaluate(command));

    [Fact]
    public void An_entry_for_another_copy_says_so() =>
        Assert.Equal(StartupState.OtherCopy, Evaluate(@"""C:\Users\me\Downloads\Snappy.exe"" --startup"));

    [Fact]
    public void An_entry_whose_exe_is_gone_says_so() =>
        Assert.Equal(StartupState.MissingCopy, Evaluate(@"""C:\Old\Snappy.exe""", exists: false));

    [Fact]
    public void Windows_startup_apps_list_can_turn_it_off()
    {
        Assert.Equal(StartupState.TurnedOffInWindows, Evaluate($@"""{This}"" --startup", TurnedOff));
        Assert.Equal(StartupState.On, Evaluate($@"""{This}"" --startup", Enabled));
    }

    [Theory]
    [InlineData(@"""C:\Program Files\Snappy\Snappy.exe"" --startup", @"C:\Program Files\Snappy\Snappy.exe")]
    [InlineData(@"C:\Tools\Snappy.exe --startup", @"C:\Tools\Snappy.exe")]
    [InlineData(@"  ""C:\Tools\Snappy.exe""  ", @"C:\Tools\Snappy.exe")]
    [InlineData(@"""""", null)]
    public void Finds_the_program_in_a_run_command(string command, string? expected) =>
        Assert.Equal(expected, StartWithWindows.ExePath(command));
}

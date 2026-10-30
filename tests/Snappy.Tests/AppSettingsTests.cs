using System.IO;
using Snappy.Hotkeys;
using Snappy.Settings;

namespace Snappy.Tests;

public sealed class AppSettingsTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "SnappyTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Defaults_save_and_copy_with_insert()
    {
        var settings = new AppSettings();

        Assert.Equal("Insert", settings.Hotkey);
        Assert.True(settings.SaveToFile);
        Assert.True(settings.CopyToClipboard);
        Assert.True(settings.ShowNotification);
        Assert.EndsWith(Path.Combine("Pictures", "Snappy"), settings.SaveFolder);
    }

    [Fact]
    public void Parse_accepts_comments_trailing_commas_and_missing_fields()
    {
        var settings = AppSettings.Parse("""
            {
              // capture with Alt+Q instead
              "hotkey": "Alt+Q",
              "CopyToClipboard": false,
            }
            """);

        Assert.Equal("Alt+Q", settings.Hotkey);
        Assert.False(settings.CopyToClipboard);
        Assert.True(settings.SaveToFile);
    }

    [Fact]
    public void Parse_replaces_blank_paths_with_defaults()
    {
        var settings = AppSettings.Parse("""{ "saveFolder": null, "fileNamePattern": "  " }""");
        var defaults = new AppSettings();

        Assert.Equal(defaults.SaveFolder, settings.SaveFolder);
        Assert.Equal(defaults.FileNamePattern, settings.FileNamePattern);
    }

    [Fact]
    public void A_blank_shortcut_means_off_but_a_missing_one_gets_the_default()
    {
        Assert.Equal("", AppSettings.Parse("""{ "hotkey": "" }""").Hotkey);
        Assert.Equal("", AppSettings.Parse("""{ "hotkey": null }""").Hotkey);
        Assert.Equal("Insert", AppSettings.Parse("{}").Hotkey);
    }

    [Fact]
    public void Only_the_region_shortcut_is_on_by_default()
    {
        var settings = new AppSettings();

        Assert.Equal("Insert", settings.HotkeyFor(HotkeyAction.CaptureRegion));
        Assert.Equal("", settings.HotkeyFor(HotkeyAction.CaptureWindow));
        Assert.Equal("", settings.HotkeyFor(HotkeyAction.CaptureScreen));
        Assert.Equal("", settings.HotkeyFor(HotkeyAction.CaptureAllDisplays));
    }

    [Theory]
    [InlineData(HotkeyAction.CaptureRegion, "\"hotkey\"")]
    [InlineData(HotkeyAction.CaptureWindow, "\"windowHotkey\"")]
    [InlineData(HotkeyAction.CaptureScreen, "\"screenHotkey\"")]
    [InlineData(HotkeyAction.CaptureAllDisplays, "\"allDisplaysHotkey\"")]
    public void Each_shortcut_is_stored_under_its_own_name(HotkeyAction action, string jsonName)
    {
        var settings = new AppSettings();

        settings.SetHotkey(action, "Ctrl+Alt+F7");

        Assert.Equal("Ctrl+Alt+F7", settings.HotkeyFor(action));
        Assert.Contains($"{jsonName}: \"Ctrl+Alt+F7\"", settings.ToJson());
    }

    [Theory]
    [InlineData("dark", "Dark")]
    [InlineData(" LIGHT ", "Light")]
    [InlineData("System", "System")]
    [InlineData("purple", "System")]
    [InlineData("", "System")]
    public void Theme_is_normalized(string stored, string expected)
    {
        Assert.Equal(expected, AppSettings.Parse($$"""{ "theme": "{{stored}}" }""").Theme);
    }

    [Fact]
    public void Clone_is_independent()
    {
        var original = new AppSettings { WindowHotkey = "Ctrl+Alt+W" };

        var copy = original.Clone();
        copy.WindowHotkey = "Ctrl+Alt+Q";

        Assert.Equal("Ctrl+Alt+W", original.WindowHotkey);
    }

    [Fact]
    public void Load_creates_the_file_with_defaults_on_first_run()
    {
        var path = Path.Combine(_folder, "settings.json");

        var settings = AppSettings.Load(path, out var error);

        Assert.Null(error);
        Assert.True(File.Exists(path));
        Assert.Contains("\"hotkey\": \"Insert\"", File.ReadAllText(path));
        Assert.Equal("Insert", settings.Hotkey);
    }

    [Fact]
    public void Load_with_broken_json_returns_defaults_and_an_error()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, "{ \"hotkey\": ");

        var settings = AppSettings.Load(path, out var error);

        Assert.NotNull(error);
        Assert.Equal("Insert", settings.Hotkey);
    }

    [Fact]
    public void Json_round_trips()
    {
        var original = new AppSettings { Hotkey = "Win+F9", SaveToFile = false, FileNamePattern = "x_{HHmm}" };

        var copy = AppSettings.Parse(original.ToJson());

        Assert.Equal(original.Hotkey, copy.Hotkey);
        Assert.Equal(original.SaveToFile, copy.SaveToFile);
        Assert.Equal(original.FileNamePattern, copy.FileNamePattern);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}

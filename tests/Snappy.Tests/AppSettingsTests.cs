using System.IO;
using Snappy.Settings;

namespace Snappy.Tests;

public sealed class AppSettingsTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "SnappyTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Defaults_save_and_copy_with_ctrl_shift_s()
    {
        var settings = new AppSettings();

        Assert.Equal("Ctrl+Shift+S", settings.Hotkey);
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
    public void Parse_replaces_blank_values_with_defaults()
    {
        var settings = AppSettings.Parse("""{ "hotkey": "", "saveFolder": null, "fileNamePattern": "  " }""");
        var defaults = new AppSettings();

        Assert.Equal(defaults.Hotkey, settings.Hotkey);
        Assert.Equal(defaults.SaveFolder, settings.SaveFolder);
        Assert.Equal(defaults.FileNamePattern, settings.FileNamePattern);
    }

    [Fact]
    public void Load_creates_the_file_with_defaults_on_first_run()
    {
        var path = Path.Combine(_folder, "settings.json");

        var settings = AppSettings.Load(path, out var error);

        Assert.Null(error);
        Assert.True(File.Exists(path));
        Assert.Contains("\"hotkey\": \"Ctrl+Shift+S\"", File.ReadAllText(path));
        Assert.Equal("Ctrl+Shift+S", settings.Hotkey);
    }

    [Fact]
    public void Load_with_broken_json_returns_defaults_and_an_error()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, "{ \"hotkey\": ");

        var settings = AppSettings.Load(path, out var error);

        Assert.NotNull(error);
        Assert.Equal("Ctrl+Shift+S", settings.Hotkey);
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

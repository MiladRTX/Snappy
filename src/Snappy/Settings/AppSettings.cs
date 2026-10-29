using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Snappy.Hotkeys;

namespace Snappy.Settings;

/// <summary>User settings, stored as JSON in %AppData%\Snappy\settings.json.</summary>
public sealed class AppSettings
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // A file people edit by hand: write "Ctrl+Shift+S", not "Ctrl+Shift+S".
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static readonly string[] Themes = ["System", "Light", "Dark"];

    /// <summary>Global shortcut for a region capture, e.g. "Ctrl+Shift+S". Empty turns it off.</summary>
    public string Hotkey { get; set; } = "Ctrl+Shift+S";

    /// <summary>Global shortcut for a window capture. Off (empty) unless set.</summary>
    public string WindowHotkey { get; set; } = "";

    /// <summary>Global shortcut for a display capture. Off (empty) unless set.</summary>
    public string ScreenHotkey { get; set; } = "";

    /// <summary>Global shortcut that captures all displays at once. Off (empty) unless set.</summary>
    public string AllDisplaysHotkey { get; set; } = "";

    /// <summary>Where captures are saved. Environment variables such as %USERPROFILE% are expanded.</summary>
    public string SaveFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Snappy");

    /// <summary>File name without extension; <c>{...}</c> holds a date/time format.</summary>
    public string FileNamePattern { get; set; } = "Snappy_{yyyy-MM-dd_HH-mm-ss}";

    public bool SaveToFile { get; set; } = true;
    public bool CopyToClipboard { get; set; } = true;
    public bool ShowNotification { get; set; } = true;

    /// <summary>"System" (follow Windows), "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snappy", "settings.json");

    public string HotkeyFor(HotkeyAction action) => action switch
    {
        HotkeyAction.CaptureRegion => Hotkey,
        HotkeyAction.CaptureWindow => WindowHotkey,
        HotkeyAction.CaptureScreen => ScreenHotkey,
        _ => AllDisplaysHotkey,
    };

    public void SetHotkey(HotkeyAction action, string gesture)
    {
        switch (action)
        {
            case HotkeyAction.CaptureRegion: Hotkey = gesture; break;
            case HotkeyAction.CaptureWindow: WindowHotkey = gesture; break;
            case HotkeyAction.CaptureScreen: ScreenHotkey = gesture; break;
            default: AllDisplaysHotkey = gesture; break;
        }
    }

    public AppSettings Clone() => Parse(ToJson());

    public static AppSettings Parse(string json) =>
        (JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings()).Normalized();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Reads the settings file, creating it with defaults on first run. When it can't be read,
    /// returns defaults and explains why in <paramref name="error"/>.
    /// </summary>
    public static AppSettings Load(string path, out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(path)) return Parse(File.ReadAllText(path));

            var defaults = new AppSettings();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, defaults.ToJson());
            return defaults;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
            return new AppSettings();
        }
    }

    /// <summary>
    /// Missing or blank paths fall back to the defaults; a blank shortcut stays blank (it means
    /// "off"); an unknown theme becomes "System".
    /// </summary>
    AppSettings Normalized()
    {
        var defaults = new AppSettings();
        Hotkey ??= "";
        WindowHotkey ??= "";
        ScreenHotkey ??= "";
        AllDisplaysHotkey ??= "";
        if (string.IsNullOrWhiteSpace(SaveFolder)) SaveFolder = defaults.SaveFolder;
        if (string.IsNullOrWhiteSpace(FileNamePattern)) FileNamePattern = defaults.FileNamePattern;
        Theme = Themes.FirstOrDefault(t => t.Equals(Theme?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "System";
        return this;
    }
}

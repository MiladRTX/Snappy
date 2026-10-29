using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

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

    /// <summary>Global shortcut that starts a capture, e.g. "Ctrl+Shift+S".</summary>
    public string Hotkey { get; set; } = "Ctrl+Shift+S";

    /// <summary>Where captures are saved. Environment variables such as %USERPROFILE% are expanded.</summary>
    public string SaveFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Snappy");

    /// <summary>File name without extension; <c>{...}</c> holds a date/time format.</summary>
    public string FileNamePattern { get; set; } = "Snappy_{yyyy-MM-dd_HH-mm-ss}";

    public bool SaveToFile { get; set; } = true;
    public bool CopyToClipboard { get; set; } = true;
    public bool ShowNotification { get; set; } = true;

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snappy", "settings.json");

    public static AppSettings Parse(string json) =>
        (JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings()).WithDefaultsForBlanks();

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

    AppSettings WithDefaultsForBlanks()
    {
        var defaults = new AppSettings();
        if (string.IsNullOrWhiteSpace(Hotkey)) Hotkey = defaults.Hotkey;
        if (string.IsNullOrWhiteSpace(SaveFolder)) SaveFolder = defaults.SaveFolder;
        if (string.IsNullOrWhiteSpace(FileNamePattern)) FileNamePattern = defaults.FileNamePattern;
        return this;
    }
}

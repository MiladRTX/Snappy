using System.Windows;
using Microsoft.Win32;

namespace Snappy.Ui;

/// <summary>
/// Applies the light or dark palette: Windows' app theme, or a fixed choice from settings.
/// When following Windows, it switches live as the system theme changes.
/// </summary>
internal static class Theme
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    static ResourceDictionary? s_resources;
    static ResourceDictionary? s_palette;
    static string s_preference = "System";

    /// <summary>Whether the dark palette is showing.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Raised after the palette switches between light and dark.</summary>
    public static event Action? Changed;

    public static void Initialize(ResourceDictionary resources, string preference)
    {
        s_resources = resources;
        s_preference = preference;
        Apply();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && s_preference == "System")
                Application.Current?.Dispatcher.InvokeAsync(Apply);
        };
    }

    /// <summary>"System", "Light" or "Dark".</summary>
    public static void Use(string preference)
    {
        s_preference = preference;
        Apply();
    }

    static void Apply()
    {
        if (s_resources is null) return;
        bool dark = s_preference switch
        {
            "Dark" => true,
            "Light" => false,
            _ => SystemPrefersDark(),
        };
        if (s_palette is not null && dark == IsDark) return;

        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml"),
        };
        if (s_palette is not null) s_resources.MergedDictionaries.Remove(s_palette);
        s_resources.MergedDictionaries.Insert(0, palette);
        s_palette = palette;
        IsDark = dark;
        Changed?.Invoke();
    }

    static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

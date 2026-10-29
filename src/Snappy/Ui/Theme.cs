using System.Windows;
using Microsoft.Win32;

namespace Snappy.Ui;

/// <summary>Picks the light or dark palette to match Windows' app theme, and follows changes live.</summary>
internal static class Theme
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    static ResourceDictionary? s_palette;

    /// <summary>Whether the dark palette is active.</summary>
    public static bool IsDark { get; private set; }

    public static void FollowSystem(ResourceDictionary resources)
    {
        Apply(resources);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
                Application.Current?.Dispatcher.InvokeAsync(() => Apply(resources));
        };
    }

    static void Apply(ResourceDictionary resources)
    {
        IsDark = SystemPrefersDark();
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(IsDark ? "Dark" : "Light")}.xaml"),
        };
        if (s_palette is not null) resources.MergedDictionaries.Remove(s_palette);
        resources.MergedDictionaries.Insert(0, palette);
        s_palette = palette;
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

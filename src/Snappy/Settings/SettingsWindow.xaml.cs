using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Snappy.Hotkeys;
using Snappy.Interop;
using Snappy.Output;
using Snappy.Ui;

namespace Snappy.Settings;

/// <summary>
/// The Snappy settings app: general options, capture output, and global shortcuts. Every change
/// is saved and applied at once; edits made to settings.json by hand show up here too.
/// </summary>
internal partial class SettingsWindow : Window
{
    readonly App _app;
    readonly (HotkeyAction Action, HotkeyBox Box, TextBlock Status)[] _shortcuts;
    AppSettings _settings;

    public SettingsWindow(App app)
    {
        InitializeComponent();
        _app = app;
        _settings = app.Settings;
        _shortcuts =
        [
            (HotkeyAction.CaptureRegion, RegionHotkey, RegionStatus),
            (HotkeyAction.CaptureWindow, WindowHotkey, WindowStatus),
            (HotkeyAction.CaptureScreen, ScreenHotkey, ScreenStatus),
            (HotkeyAction.CaptureAllDisplays, AllDisplaysHotkey, AllDisplaysStatus),
        ];

        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = AboutVersionText.Text = $"Version {version?.ToString(3)}";

        foreach (var (action, box, _) in _shortcuts)
        {
            box.GestureChanged += gesture =>
            {
                _settings.SetHotkey(action, gesture);
                Save();
            };
            // While a shortcut is being recorded, its keys must reach the box, not start a capture.
            box.RecordingStarted += _app.SuspendHotkeys;
            box.RecordingEnded += _app.ResumeHotkeys;
        }

        _app.HotkeysChanged += ShowHotkeyStatus;
        _app.SettingsReloaded += OnSettingsReloaded;
        Theme.Changed += MatchTitleBar;
        SourceInitialized += (_, _) => MatchTitleBar();
        Closed += (_, _) =>
        {
            _app.HotkeysChanged -= ShowHotkeyStatus;
            _app.SettingsReloaded -= OnSettingsReloaded;
            Theme.Changed -= MatchTitleBar;
            _app.ResumeHotkeys();
        };

        ShowSettings();
    }

    void ShowSettings()
    {
        ShowStartup();
        NotifySwitch.IsChecked = _settings.ShowNotification;
        SystemTheme.IsChecked = _settings.Theme == "System";
        LightTheme.IsChecked = _settings.Theme == "Light";
        DarkTheme.IsChecked = _settings.Theme == "Dark";

        CopySwitch.IsChecked = _settings.CopyToClipboard;
        SaveSwitch.IsChecked = _settings.SaveToFile;
        FolderText.Text = _settings.SaveFolder;
        FolderText.ToolTip = _settings.SaveFolder;
        PatternBox.Text = _settings.FileNamePattern;
        ShowSaveOptions();

        foreach (var (action, box, _) in _shortcuts) box.Gesture = _settings.HotkeyFor(action);
        ShowHotkeyStatus();
    }

    void Save() => _app.UpdateSettings(_settings);

    void OnSettingsReloaded()
    {
        _settings = _app.Settings;
        ShowSettings();
    }

    // General

    void OnStartupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            StartWithWindows.Set(StartupSwitch.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            ToastWindow.ShowMessage("Couldn't change startup", ex.Message, ToastKind.Error);
        }
        ShowStartup();
    }

    /// <summary>The switch is on only when this copy will really start; otherwise the note says why not.</summary>
    void ShowStartup()
    {
        var state = StartWithWindows.State;
        StartupSwitch.IsChecked = state == StartupState.On;
        StartupNote.Text = state switch
        {
            StartupState.TurnedOffInWindows => "Turned off in Windows Settings › Apps › Startup. Switch it on here to turn it back on.",
            StartupState.OtherCopy => $"Windows starts another copy: {StartWithWindows.RegisteredExe}. Switch on to start this one instead.",
            StartupState.MissingCopy => $"Windows would start {StartWithWindows.RegisteredExe}, which no longer exists. Switch on to start this copy.",
            _ => "",
        };
        StartupNote.Visibility = StartupNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnNotifyClick(object sender, RoutedEventArgs e)
    {
        _settings.ShowNotification = NotifySwitch.IsChecked == true;
        Save();
    }

    void OnThemeClick(object sender, RoutedEventArgs e)
    {
        _settings.Theme = (string)((FrameworkElement)sender).Tag;
        Save();
    }

    void MatchTitleBar() => TitleBar.Match(this, Theme.IsDark, ((SolidColorBrush)FindResource("Snappy.Background")).Color);

    // Capture

    void OnCopyClick(object sender, RoutedEventArgs e)
    {
        _settings.CopyToClipboard = CopySwitch.IsChecked == true;
        Save();
    }

    void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _settings.SaveToFile = SaveSwitch.IsChecked == true;
        ShowSaveOptions();
        Save();
    }

    void ShowSaveOptions() => FolderRow.IsEnabled = FileNameRow.IsEnabled = _settings.SaveToFile;

    void OnChangeFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where Snappy saves captures",
            InitialDirectory = Environment.ExpandEnvironmentVariables(_settings.SaveFolder),
        };
        if (dialog.ShowDialog(this) != true) return;
        _settings.SaveFolder = dialog.FolderName;
        FolderText.Text = dialog.FolderName;
        FolderText.ToolTip = dialog.FolderName;
        Save();
    }

    void OnOpenFolderClick(object sender, RoutedEventArgs e) =>
        Shell.OpenFolder(Environment.ExpandEnvironmentVariables(_settings.SaveFolder));

    void OnPatternChanged(object sender, TextChangedEventArgs e)
    {
        var pattern = string.IsNullOrWhiteSpace(PatternBox.Text) ? new AppSettings().FileNamePattern : PatternBox.Text;
        PatternPreview.Text = $"Next: {FileNames.Format(pattern, DateTime.Now)}.png";
    }

    void OnPatternLostFocus(object sender, KeyboardFocusChangedEventArgs e) => SavePattern();

    void OnPatternKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SavePattern();
    }

    void SavePattern()
    {
        var pattern = string.IsNullOrWhiteSpace(PatternBox.Text) ? new AppSettings().FileNamePattern : PatternBox.Text.Trim();
        if (pattern == _settings.FileNamePattern) return;
        _settings.FileNamePattern = pattern;
        PatternBox.Text = pattern;
        Save();
    }

    // Shortcuts

    void ShowHotkeyStatus()
    {
        foreach (var (action, _, status) in _shortcuts)
        {
            var current = _app.StatusOf(action);
            string? message = current.State switch
            {
                HotkeyState.Invalid => "Not a shortcut Snappy understands.",
                HotkeyState.InUse => "Another app is already using this shortcut.",
                HotkeyState.Duplicate => $"Already used for {HotkeyActions.Label(current.ConflictsWith!.Value).ToLowerInvariant()}.",
                _ => null,
            };
            status.Text = message ?? "";
            status.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
            status.SetResourceReference(TextBlock.ForegroundProperty, "Snappy.Destructive");
        }
    }

    // Navigation and About

    void OnPageChecked(object sender, RoutedEventArgs e)
    {
        if (GeneralPage is null) return; // fires once while the window is still loading
        foreach (var page in new[] { GeneralPage, CapturePage, ShortcutsPage, AboutPage })
            page.Visibility = page == ((FrameworkElement)sender).Tag ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnGitHubClick(object sender, RoutedEventArgs e) => Shell.Open("https://github.com/MiladRTX/Snappy");

    void OnSettingsFileClick(object sender, RoutedEventArgs e) => Shell.OpenTextFile(AppSettings.DefaultPath);

    protected override void OnClosing(CancelEventArgs e)
    {
        SavePattern(); // keep a file name the user typed but didn't confirm
        base.OnClosing(e);
    }
}

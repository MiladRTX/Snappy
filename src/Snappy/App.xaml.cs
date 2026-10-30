using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Snappy.Capture;
using Snappy.Hotkeys;
using Snappy.Interop;
using Snappy.Output;
using Snappy.Overlay;
using Snappy.Settings;
using Snappy.Tray;
using Snappy.Ui;

namespace Snappy;

/// <summary>
/// Tray-resident app: owns the tray icon, the global shortcuts, the settings (file and window),
/// and runs one capture at a time.
/// </summary>
public partial class App : Application
{
    const string InstanceName = @"Local\Snappy.SingleInstance";
    // Launching Snappy again opens the settings window; "Snappy.exe --capture" starts a capture.
    const string OpenSignalName = @"Local\Snappy.Open";
    const string CaptureSignalName = @"Local\Snappy.Capture";
    // Lets the tray menu disappear before the screen is frozen.
    const int TrayDelayMs = 150;
    // Lets a closing toast disappear before the screen is frozen.
    const int ToastDelayMs = 120;

    readonly DispatcherTimer _settingsReload = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly Dictionary<HotkeyAction, HotkeyStatus> _hotkeyStatus = [];
    readonly List<(EventWaitHandle Signal, RegisteredWaitHandle Wait)> _signals = [];
    Mutex? _instance;
    bool _ownsInstance;
    TrayIcon? _tray;
    GlobalHotkeys? _hotkeys;
    FileSystemWatcher? _settingsWatcher;
    SettingsWindow? _settingsWindow;
    AppSettings _settings = new();
    string? _lastSavedJson;
    bool _capturing;

    public App()
    {
        // Before any animation exists: animate at the display's refresh rate, not WPF's 60 fps.
        Motion.MatchDisplayRefreshRate();
    }

    /// <summary>A copy of the current settings.</summary>
    internal AppSettings Settings => _settings.Clone();

    /// <summary>Raised after the global shortcuts were registered again.</summary>
    internal event Action? HotkeysChanged;

    /// <summary>Raised after settings.json was edited outside Snappy and reloaded.</summary>
    internal event Action? SettingsReloaded;

    internal HotkeyStatus StatusOf(HotkeyAction action) =>
        _hotkeyStatus.TryGetValue(action, out var status) ? status : new HotkeyStatus(HotkeyState.Off);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool HasArgument(string name) => e.Args.Any(arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
        bool atSignIn = HasArgument(StartWithWindows.StartupArgument);

        _instance = new Mutex(initiallyOwned: true, InstanceName, out _ownsInstance);
        if (!_ownsInstance)
        {
            if (atSignIn)
            {
                Shutdown(); // already running; sign-in shouldn't pop up the settings window
                return;
            }
            bool capture = HasArgument("--capture");
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY); // so the settings window can come to the front
            if (EventWaitHandle.TryOpenExisting(capture ? CaptureSignalName : OpenSignalName, out var signal))
            {
                signal.Set();
                signal.Dispose();
            }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            ReportError("Something went wrong", args.Exception);
            args.Handled = true;
        };

        _settings = AppSettings.Load(AppSettings.DefaultPath, out var loadError);
        Theme.Initialize(Resources, _settings.Theme);
        if (loadError is not null) ShowLoadError(loadError);

        _tray = new TrayIcon();
        _tray.ActionRequested += action => _ = RunAsync(action, TrayDelayMs);
        _tray.OpenFolderRequested += () => Shell.OpenFolder(Environment.ExpandEnvironmentVariables(_settings.SaveFolder));
        _tray.OpenSettingsRequested += OpenSettings;
        _tray.ExitRequested += () => Shutdown();

        _hotkeys = new GlobalHotkeys();
        _hotkeys.Pressed += action => _ = RunAsync(action);
        bool hotkeysOk = ApplyHotkeys(announceProblems: true);

        ListenForSignal(OpenSignalName, OpenSettings);
        ListenForSignal(CaptureSignalName, () => _ = RunAsync(HotkeyAction.CaptureRegion));
        WatchSettings();

        try
        {
            StartWithWindows.Maintain();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Startup keeps whatever entry it has; nothing else depends on this.
        }

        // Started by Windows at sign-in: stay quiet in the tray. Problems (above) still show.
        if (hotkeysOk && !atSignIn)
        {
            var region = _hotkeys.GestureFor(HotkeyAction.CaptureRegion);
            ToastWindow.ShowMessage("Snappy is running", region is null
                ? "Click the tray icon to capture. Right-click it for settings."
                : $"Press {region} to capture. Right-click the tray icon for settings.");
        }
    }

    Task RunAsync(HotkeyAction action, int delayMs = 0) => action switch
    {
        HotkeyAction.CaptureRegion => CaptureAsync(CaptureMode.Region, delayMs),
        HotkeyAction.CaptureWindow => CaptureAsync(CaptureMode.Window, delayMs),
        HotkeyAction.CaptureScreen => CaptureAsync(CaptureMode.Screen, delayMs),
        _ => CaptureAllDisplaysAsync(delayMs),
    };

    async Task CaptureAsync(CaptureMode mode, int delayMs = 0)
    {
        if (_capturing) return;
        _capturing = true;
        try
        {
            await ClearScreenAsync(delayMs);
            var image = await CaptureSession.RunAsync(mode);
            if (image is not null) await DeliverAsync(image);
        }
        catch (Exception ex)
        {
            ReportError("Capture failed", ex);
        }
        finally
        {
            _capturing = false;
        }
    }

    async Task CaptureAllDisplaysAsync(int delayMs)
    {
        if (_capturing) return;
        _capturing = true;
        try
        {
            await ClearScreenAsync(delayMs);
            await DeliverAsync(FrozenDesktop.Capture(includeWindows: false).Image);
        }
        catch (Exception ex)
        {
            ReportError("Capture failed", ex);
        }
        finally
        {
            _capturing = false;
        }
    }

    /// <summary>Keeps Snappy's own toast and menu out of the screenshot.</summary>
    static async Task ClearScreenAsync(int delayMs)
    {
        if (ToastWindow.CloseCurrent()) delayMs = Math.Max(delayMs, ToastDelayMs);
        if (delayMs > 0) await Task.Delay(delayMs);
    }

    async Task DeliverAsync(BitmapSource image)
    {
        var result = await CaptureOutput.DeliverAsync(image, _settings);
        if (_settings.ShowNotification || result.Error is not null) ToastWindow.ShowCapture(result);
    }

    /// <summary>
    /// Registers every configured shortcut and records why any of them isn't active.
    /// Returns false when at least one configured shortcut doesn't work.
    /// </summary>
    bool ApplyHotkeys(bool announceProblems)
    {
        _hotkeys!.UnregisterAll();
        var taken = new Dictionary<HotkeyGesture, HotkeyAction>();
        var problems = new List<string>();

        foreach (var action in HotkeyActions.All)
        {
            var text = _settings.HotkeyFor(action);
            HotkeyStatus status;
            if (string.IsNullOrWhiteSpace(text))
                status = new(HotkeyState.Off);
            else if (!HotkeyGesture.TryParse(text, out var gesture))
                status = new(HotkeyState.Invalid);
            else if (taken.TryGetValue(gesture, out var owner))
                status = new(HotkeyState.Duplicate, owner);
            else if (!_hotkeys.Register(action, gesture))
                status = new(HotkeyState.InUse);
            else
            {
                taken[gesture] = action;
                status = new(HotkeyState.Active);
            }

            _hotkeyStatus[action] = status;
            if (status.State is HotkeyState.Invalid or HotkeyState.InUse or HotkeyState.Duplicate)
                problems.Add($"{HotkeyActions.Label(action)} ({text.Trim()})");
        }

        _tray!.SetHotkeys(action => _hotkeys.GestureFor(action)?.ToString());
        HotkeysChanged?.Invoke();
        if (announceProblems && problems.Count > 0)
            ToastWindow.ShowMessage("Some shortcuts aren't working",
                $"{string.Join(", ", problems)}. Open Settings from the tray icon to fix them.", ToastKind.Error);
        return problems.Count == 0;
    }

    /// <summary>Pauses global shortcuts, e.g. while one is being recorded in the settings window.</summary>
    internal void SuspendHotkeys() => _hotkeys?.UnregisterAll();

    internal void ResumeHotkeys() => ApplyHotkeys(announceProblems: false);

    /// <summary>Saves settings from the settings window and applies them at once.</summary>
    internal void UpdateSettings(AppSettings updated)
    {
        var settings = updated.Clone();
        _lastSavedJson = settings.ToJson(); // so the file watcher can tell this write from a hand edit
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppSettings.DefaultPath)!);
            File.WriteAllText(AppSettings.DefaultPath, _lastSavedJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportError("Couldn't save settings", ex);
        }
        Apply(settings, announceProblems: false);
    }

    void Apply(AppSettings settings, bool announceProblems)
    {
        bool themeChanged = settings.Theme != _settings.Theme;
        _settings = settings;
        ApplyHotkeys(announceProblems);
        if (themeChanged) Theme.Use(settings.Theme);
    }

    void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            var window = new SettingsWindow(this);
            window.Closed += (_, _) => _settingsWindow = null;
            try
            {
                window.Show();
            }
            catch
            {
                window.Close(); // don't keep a half-opened window around; the next try starts fresh
                throw;
            }
            _settingsWindow = window;
        }
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    void ListenForSignal(string name, Action onSignal)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        // Errors in InvokeAsync work stay inside its task unless caught here.
        var wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => Dispatcher.InvokeAsync(() =>
        {
            try
            {
                onSignal();
            }
            catch (Exception ex)
            {
                ReportError("Something went wrong", ex);
            }
        }), null, Timeout.Infinite, executeOnlyOnce: false);
        _signals.Add((signal, wait));
    }

    void WatchSettings()
    {
        var path = AppSettings.DefaultPath;
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);

        _settingsReload.Tick += (_, _) =>
        {
            _settingsReload.Stop();
            ReloadSettings();
        };
        // Editors save in bursts (truncate, write, rename); reload once things settle.
        void Schedule() => Dispatcher.InvokeAsync(() =>
        {
            _settingsReload.Stop();
            _settingsReload.Start();
        });
        _settingsWatcher = new FileSystemWatcher(folder, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _settingsWatcher.Changed += (_, _) => Schedule();
        _settingsWatcher.Created += (_, _) => Schedule();
        _settingsWatcher.Renamed += (_, _) => Schedule();
        _settingsWatcher.EnableRaisingEvents = true;
    }

    /// <summary>Picks up hand edits to settings.json; ignores the writes Snappy made itself.</summary>
    void ReloadSettings()
    {
        try
        {
            if (File.ReadAllText(AppSettings.DefaultPath) == _lastSavedJson) return;
        }
        catch (IOException)
        {
            return; // still being written; the watcher fires again when it's done
        }

        var settings = AppSettings.Load(AppSettings.DefaultPath, out var error);
        if (error is not null)
        {
            ShowLoadError(error);
            return;
        }
        Apply(settings, announceProblems: true);
        SettingsReloaded?.Invoke();
        if (_hotkeyStatus.Values.All(s => s.State is HotkeyState.Active or HotkeyState.Off))
            ToastWindow.ShowMessage("Settings updated", "Your changes are active.");
    }

    static void ShowLoadError(string error) =>
        ToastWindow.ShowMessage("Couldn't read settings", $"{error} Using the defaults for now.", ToastKind.Error);

    static void ReportError(string title, Exception exception)
    {
        Log.Error(title, exception);
        ToastWindow.ShowMessage(title, exception.Message, ToastKind.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var (signal, wait) in _signals)
        {
            wait.Unregister(null);
            signal.Dispose();
        }
        _settingsWatcher?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

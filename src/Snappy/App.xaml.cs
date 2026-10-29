using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Snappy.Capture;
using Snappy.Hotkeys;
using Snappy.Output;
using Snappy.Overlay;
using Snappy.Settings;
using Snappy.Tray;
using Snappy.Ui;

namespace Snappy;

/// <summary>
/// Tray-resident app: owns the tray icon, the global hotkey and the settings file, and runs
/// one capture at a time.
/// </summary>
public partial class App : Application
{
    const string InstanceName = @"Local\Snappy.SingleInstance";
    const string ActivateSignalName = @"Local\Snappy.Activate";
    // Lets the tray menu disappear before the screen is frozen.
    const int TrayDelayMs = 150;
    // Lets a closing toast disappear before the screen is frozen.
    const int ToastDelayMs = 120;

    readonly DispatcherTimer _settingsReload = new() { Interval = TimeSpan.FromMilliseconds(300) };
    Mutex? _instance;
    bool _ownsInstance;
    EventWaitHandle? _activateSignal;
    RegisteredWaitHandle? _activateWait;
    TrayIcon? _tray;
    GlobalHotkey? _hotkey;
    FileSystemWatcher? _settingsWatcher;
    AppSettings _settings = new();
    bool _capturing;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new Mutex(initiallyOwned: true, InstanceName, out _ownsInstance);
        if (!_ownsInstance)
        {
            // Launching Snappy again (e.g. from Start) asks the running instance to capture.
            if (EventWaitHandle.TryOpenExisting(ActivateSignalName, out var signal))
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
        Theme.FollowSystem(Resources);
        _settings = LoadSettings(out _);

        _tray = new TrayIcon();
        _tray.CaptureRequested += mode => _ = CaptureAsync(mode, TrayDelayMs);
        _tray.CaptureAllRequested += () => _ = CaptureAllDisplaysAsync(TrayDelayMs);
        _tray.OpenFolderRequested += () => Shell.OpenFolder(Environment.ExpandEnvironmentVariables(_settings.SaveFolder));
        _tray.OpenSettingsRequested += () => Shell.OpenTextFile(AppSettings.DefaultPath);
        _tray.ExitRequested += () => Shutdown();

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += () => _ = CaptureAsync(CaptureMode.Region);
        bool hotkeyReady = ApplyHotkey();

        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateSignalName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activateSignal,
            (_, _) => Dispatcher.InvokeAsync(() => _ = CaptureAsync(CaptureMode.Region)),
            null, Timeout.Infinite, executeOnlyOnce: false);

        WatchSettings();

        if (hotkeyReady)
            ToastWindow.ShowMessage("Snappy is running", $"Press {_hotkey.Gesture} to capture, or right-click the tray icon for more.");
    }

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

    bool ApplyHotkey()
    {
        if (!HotkeyGesture.TryParse(_settings.Hotkey, out var gesture))
        {
            _hotkey!.Unregister();
            _tray!.SetHotkey(null);
            ToastWindow.ShowMessage("Hotkey not recognized",
                $"\"{_settings.Hotkey}\" isn't a valid hotkey. Try something like Ctrl+Shift+S in Settings.", ToastKind.Error);
            return false;
        }
        if (!_hotkey!.Register(gesture))
        {
            _tray!.SetHotkey(null);
            ToastWindow.ShowMessage("Hotkey unavailable",
                $"{gesture} is already used by another app. Choose a different one in Settings.", ToastKind.Error);
            return false;
        }
        _tray!.SetHotkey(gesture.ToString());
        return true;
    }

    AppSettings LoadSettings(out bool ok)
    {
        var settings = AppSettings.Load(AppSettings.DefaultPath, out var error);
        ok = error is null;
        if (!ok) ToastWindow.ShowMessage("Couldn't read settings", $"{error} Using the defaults for now.", ToastKind.Error);
        return settings;
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

    void ReloadSettings()
    {
        var previousHotkey = _settings.Hotkey;
        _settings = LoadSettings(out bool ok);
        if (!ok) return;

        if (string.Equals(previousHotkey, _settings.Hotkey, StringComparison.OrdinalIgnoreCase))
            ToastWindow.ShowMessage("Settings updated", "Your changes are active.");
        else if (ApplyHotkey())
            ToastWindow.ShowMessage("Settings updated", $"Capture hotkey is now {_hotkey!.Gesture}.");
    }

    static void ReportError(string title, Exception exception)
    {
        Log.Error(title, exception);
        ToastWindow.ShowMessage(title, exception.Message, ToastKind.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWait?.Unregister(null);
        _activateSignal?.Dispose();
        _settingsWatcher?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

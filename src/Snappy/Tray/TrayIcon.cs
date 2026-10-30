using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Snappy.Hotkeys;
using Snappy.Interop;
using Forms = System.Windows.Forms;

namespace Snappy.Tray;

/// <summary>Notification-area icon: left-click captures a region, right-click opens the menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly ContextMenu _menu;
    readonly Dictionary<HotkeyAction, MenuItem> _captureItems = [];

    public TrayIcon()
    {
        _menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        _menu.SetResourceReference(FrameworkElement.StyleProperty, "Snappy.Menu");
        AddCaptureItem(HotkeyAction.CaptureRegion, "Icon.Region");
        AddCaptureItem(HotkeyAction.CaptureWindow, "Icon.Window");
        AddCaptureItem(HotkeyAction.CaptureScreen, "Icon.Display");
        AddCaptureItem(HotkeyAction.CaptureAllDisplays, "Icon.AllDisplays");
        AddSeparator();
        AddItem("Open captures folder", "Icon.Folder", () => OpenFolderRequested?.Invoke());
        AddItem("Settings", "Icon.Settings", () => OpenSettingsRequested?.Invoke());
        AddSeparator();
        AddItem("Quit Snappy", "Icon.Quit", () => ExitRequested?.Invoke());

        _icon = new Forms.NotifyIcon { Icon = LoadIcon(), Text = "Snappy", Visible = true };
        _icon.MouseUp += OnIconMouseUp;
    }

    public event Action<HotkeyAction>? ActionRequested;
    public event Action? OpenFolderRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ExitRequested;

    /// <summary>Shows each capture item's shortcut, and the region shortcut in the tooltip.</summary>
    public void SetHotkeys(Func<HotkeyAction, string?> gestureFor)
    {
        foreach (var (action, item) in _captureItems) item.InputGestureText = gestureFor(action) ?? "";
        var region = gestureFor(HotkeyAction.CaptureRegion);
        _icon.Text = region is null ? "Snappy" : $"Snappy: {region} to capture";
    }

    void AddCaptureItem(HotkeyAction action, string iconKey) =>
        _captureItems[action] = AddItem(HotkeyActions.Label(action), iconKey, () => ActionRequested?.Invoke(action));

    /// <summary>Adds a menu item; <paramref name="iconKey"/> names an icon in Themes/Icons.xaml.</summary>
    MenuItem AddItem(string header, string iconKey, Action onClick)
    {
        var item = new MenuItem { Header = header, Tag = Application.Current.FindResource(iconKey) };
        item.SetResourceReference(FrameworkElement.StyleProperty, "Snappy.MenuItem");
        item.Click += (_, _) => onClick();
        _menu.Items.Add(item);
        return item;
    }

    void AddSeparator()
    {
        var separator = new Separator();
        separator.SetResourceReference(FrameworkElement.StyleProperty, "Snappy.MenuSeparator");
        _menu.Items.Add(separator);
    }

    void OnIconMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left) ActionRequested?.Invoke(HotkeyAction.CaptureRegion);
        else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
    }

    void ShowMenu()
    {
        _menu.IsOpen = true;
        // Without a foreground window the menu never sees the outside click that should close it.
        if (PresentationSource.FromVisual(_menu) is HwndSource source) NativeMethods.SetForegroundWindow(source.Handle);
    }

    static System.Drawing.Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/snappy.ico"))!;
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _menu.IsOpen = false;
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}

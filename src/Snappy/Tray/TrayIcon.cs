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
    Func<HotkeyAction, string?> _gestureFor = _ => null;
    ContextMenu? _menu;

    public TrayIcon()
    {
        _icon = new Forms.NotifyIcon { Icon = LoadIcon(), Text = "Snappy", Visible = true };
        _icon.MouseUp += OnIconMouseUp;
    }

    public event Action<HotkeyAction>? ActionRequested;
    public event Action? OpenFolderRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ExitRequested;

    /// <summary>Where the menu gets each capture item's shortcut; the region shortcut also goes in the tooltip.</summary>
    public void SetHotkeys(Func<HotkeyAction, string?> gestureFor)
    {
        _gestureFor = gestureFor;
        var region = gestureFor(HotkeyAction.CaptureRegion);
        _icon.Text = region is null ? "Snappy" : $"Snappy: {region} to capture";
    }

    /// <summary>
    /// Built fresh each time it opens. The menu belongs to no window, and WPF passes a switch
    /// between the light and dark palettes only to windows, so a menu kept around would keep the
    /// colors it started with. A new one always has the current colors and shortcuts.
    /// </summary>
    ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        menu.SetResourceReference(FrameworkElement.StyleProperty, "Snappy.Menu");
        AddCaptureItem(menu, HotkeyAction.CaptureRegion, "Icon.Region");
        AddCaptureItem(menu, HotkeyAction.CaptureWindow, "Icon.Window");
        AddCaptureItem(menu, HotkeyAction.CaptureScreen, "Icon.Display");
        AddCaptureItem(menu, HotkeyAction.CaptureAllDisplays, "Icon.AllDisplays");
        AddSeparator(menu);
        AddItem(menu, "Open captures folder", "Icon.Folder", () => OpenFolderRequested?.Invoke());
        AddItem(menu, "Settings", "Icon.Settings", () => OpenSettingsRequested?.Invoke());
        AddSeparator(menu);
        AddItem(menu, "Quit Snappy", "Icon.Quit", () => ExitRequested?.Invoke());
        return menu;
    }

    void AddCaptureItem(ContextMenu menu, HotkeyAction action, string iconKey) =>
        AddItem(menu, HotkeyActions.Label(action), iconKey, () => ActionRequested?.Invoke(action)).InputGestureText = _gestureFor(action) ?? "";

    /// <summary>Adds a menu item; <paramref name="iconKey"/> names an icon in Themes/Icons.xaml.</summary>
    static MenuItem AddItem(ContextMenu menu, string header, string iconKey, Action onClick)
    {
        var item = new MenuItem { Header = header, Tag = Application.Current.FindResource(iconKey) };
        item.SetResourceReference(FrameworkElement.StyleProperty, "Snappy.MenuItem");
        item.Click += (_, _) => onClick();
        menu.Items.Add(item);
        return item;
    }

    static void AddSeparator(ContextMenu menu)
    {
        var separator = new Separator();
        separator.SetResourceReference(FrameworkElement.StyleProperty, "Snappy.MenuSeparator");
        menu.Items.Add(separator);
    }

    void OnIconMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left) ActionRequested?.Invoke(HotkeyAction.CaptureRegion);
        else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
    }

    void ShowMenu()
    {
        if (_menu is { IsOpen: true }) return;
        _menu = BuildMenu();
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
        if (_menu is not null) _menu.IsOpen = false;
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}

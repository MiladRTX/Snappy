using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Snappy.Interop;
using Snappy.Overlay;
using Forms = System.Windows.Forms;

namespace Snappy.Tray;

/// <summary>Notification-area icon: left-click captures a region, right-click opens the menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly ContextMenu _menu;
    readonly MenuItem _regionItem;

    public TrayIcon()
    {
        _menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        _menu.SetResourceReference(FrameworkElement.StyleProperty, "Snappy.Menu");
        _regionItem = AddItem("Capture region", "", () => CaptureRequested?.Invoke(CaptureMode.Region));
        AddItem("Capture window", "", () => CaptureRequested?.Invoke(CaptureMode.Window));
        AddItem("Capture display", "", () => CaptureRequested?.Invoke(CaptureMode.Screen));
        AddItem("Capture all displays", "", () => CaptureAllRequested?.Invoke());
        AddSeparator();
        AddItem("Open captures folder", "", () => OpenFolderRequested?.Invoke());
        AddItem("Settings", "", () => OpenSettingsRequested?.Invoke());
        AddSeparator();
        AddItem("Quit Snappy", "", () => ExitRequested?.Invoke());

        _icon = new Forms.NotifyIcon { Icon = LoadIcon(), Text = "Snappy", Visible = true };
        _icon.MouseUp += OnIconMouseUp;
    }

    public event Action<CaptureMode>? CaptureRequested;
    public event Action? CaptureAllRequested;
    public event Action? OpenFolderRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ExitRequested;

    public void SetHotkey(string? gesture)
    {
        _regionItem.InputGestureText = gesture ?? "";
        _icon.Text = gesture is null ? "Snappy" : $"Snappy: {gesture} to capture";
    }

    MenuItem AddItem(string header, string glyph, Action onClick)
    {
        var item = new MenuItem { Header = header, Tag = glyph };
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
        if (e.Button == Forms.MouseButtons.Left) CaptureRequested?.Invoke(CaptureMode.Region);
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

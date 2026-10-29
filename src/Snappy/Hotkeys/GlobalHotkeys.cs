using System.Windows.Interop;
using static Snappy.Interop.NativeMethods;

namespace Snappy.Hotkeys;

/// <summary>System-wide shortcuts, one per <see cref="HotkeyAction"/>, delivered to a hidden message-only window.</summary>
internal sealed class GlobalHotkeys : IDisposable
{
    const int BaseId = 0x5A1;
    readonly HwndSource _sink;
    readonly Dictionary<HotkeyAction, HotkeyGesture> _registered = [];

    public GlobalHotkeys()
    {
        _sink = new HwndSource(new HwndSourceParameters("Snappy hotkey sink")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        });
        _sink.AddHook(WndProc);
    }

    public event Action<HotkeyAction>? Pressed;

    /// <summary>The shortcut currently registered for an action, if any.</summary>
    public HotkeyGesture? GestureFor(HotkeyAction action) => _registered.TryGetValue(action, out var gesture) ? gesture : null;

    /// <summary>Binds an action to a shortcut. False when another app already owns the shortcut.</summary>
    public bool Register(HotkeyAction action, HotkeyGesture gesture)
    {
        Unregister(action);
        if (!RegisterHotKey(_sink.Handle, BaseId + (int)action, gesture.NativeModifiers | MOD_NOREPEAT, gesture.VirtualKey))
            return false;
        _registered[action] = gesture;
        return true;
    }

    public void Unregister(HotkeyAction action)
    {
        if (_registered.Remove(action)) UnregisterHotKey(_sink.Handle, BaseId + (int)action);
    }

    public void UnregisterAll()
    {
        foreach (var action in _registered.Keys.ToList()) Unregister(action);
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && Enum.IsDefined((HotkeyAction)(wParam - BaseId)))
        {
            handled = true;
            Pressed?.Invoke((HotkeyAction)(wParam - BaseId));
        }
        return 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _sink.RemoveHook(WndProc);
        _sink.Dispose();
    }
}

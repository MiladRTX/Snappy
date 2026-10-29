using System.Windows.Interop;
using Snappy.Interop;
using static Snappy.Interop.NativeMethods;

namespace Snappy.Hotkeys;

/// <summary>A system-wide shortcut, delivered to a hidden message-only window.</summary>
internal sealed class GlobalHotkey : IDisposable
{
    const int HotkeyId = 0x5A1;
    readonly HwndSource _sink;
    bool _registered;

    public GlobalHotkey()
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

    public event Action? Pressed;

    /// <summary>The registered gesture, or null when none is active.</summary>
    public HotkeyGesture? Gesture { get; private set; }

    /// <summary>Replaces the current hotkey. False when another app already owns the gesture.</summary>
    public bool Register(HotkeyGesture gesture)
    {
        Unregister();
        _registered = RegisterHotKey(_sink.Handle, HotkeyId, gesture.NativeModifiers | MOD_NOREPEAT, gesture.VirtualKey);
        Gesture = _registered ? gesture : null;
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_sink.Handle, HotkeyId);
        _registered = false;
        Gesture = null;
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return 0;
    }

    public void Dispose()
    {
        Unregister();
        _sink.RemoveHook(WndProc);
        _sink.Dispose();
    }
}

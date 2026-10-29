using System.Windows.Input;

namespace Snappy.Hotkeys;

/// <summary>A global shortcut such as Ctrl+Shift+S.</summary>
public readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    /// <summary>RegisterHotKey's MOD_* flags; WPF's ModifierKeys uses the same bit values.</summary>
    public uint NativeModifiers => (uint)Modifiers;

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    /// <summary>
    /// Whether this is safe to take over system-wide: it needs a modifier, unless it's a key
    /// nobody types text with (F1-F24, PrintScreen, Pause). A bare "S" would break typing everywhere.
    /// </summary>
    public bool IsSuitableForGlobal =>
        Modifiers != ModifierKeys.None || Key is >= Key.F1 and <= Key.F24 or Key.PrintScreen or Key.Pause;

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = ModifierKeys.None;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default: return false;
            }
        }

        if (!TryParseKey(parts[^1], out var key)) return false;
        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (Key - Key.D0))).ToString(),
            Key.PrintScreen => "PrintScreen",
            _ => Key.ToString(),
        });
        return string.Join("+", parts);
    }

    static bool TryParseKey(string text, out Key key)
    {
        key = Key.None;
        if (text.Length == 1 && char.IsAsciiDigit(text[0]))
        {
            key = Key.D0 + (text[0] - '0');
            return true;
        }
        if (text.Equals("PrtSc", StringComparison.OrdinalIgnoreCase))
        {
            key = Key.PrintScreen;
            return true;
        }
        // Enum.TryParse would also accept raw numbers like "42".
        if (text.Length == 0 || char.IsAsciiDigit(text[0]) || !Enum.TryParse(text, ignoreCase: true, out key)) return false;

        return key is not (Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System);
    }
}

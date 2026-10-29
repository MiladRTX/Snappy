using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Snappy.Hotkeys;

namespace Snappy.Settings;

/// <summary>
/// Records a global shortcut: click (or Enter), then press the keys. Esc cancels, Backspace or
/// the clear button removes the shortcut. Shortcuts without a modifier are refused, except
/// function keys, since a bare letter would break typing in every app.
/// </summary>
internal partial class HotkeyBox : UserControl
{
    string _gesture = "";
    bool _recording;
    string? _note;

    public HotkeyBox()
    {
        InitializeComponent();
        Frame.MouseLeftButtonUp += (_, e) =>
        {
            if (!(e.OriginalSource is Visual source && ClearButton.IsAncestorOf(source))) StartRecording();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += (_, e) =>
        {
            if (!_recording) return;
            e.Handled = true;
            Render();
        };
        LostKeyboardFocus += (_, _) => StopRecording();
        Render();
    }

    /// <summary>The shortcut as text, e.g. "Ctrl+Shift+S"; empty when none is set.</summary>
    public string Gesture
    {
        get => _gesture;
        set
        {
            _gesture = value ?? "";
            Render();
        }
    }

    /// <summary>The user recorded or removed a shortcut.</summary>
    public event Action<string>? GestureChanged;

    /// <summary>Recording began; global shortcuts should pause so their keys reach this box.</summary>
    public event Action? RecordingStarted;

    public event Action? RecordingEnded;

    void StartRecording()
    {
        if (_recording) return;
        _recording = true;
        _note = null;
        Keyboard.Focus(this);
        RecordingStarted?.Invoke();
        Render();
    }

    void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        _note = null;
        Render();
        RecordingEnded?.Invoke();
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording)
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                StartRecording();
                e.Handled = true;
            }
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            StopRecording();
            return;
        }
        if (key is Key.Back or Key.Delete && modifiers == ModifierKeys.None)
        {
            Commit("");
            return;
        }
        if (IsModifier(key) || key is Key.None or Key.ImeProcessed or Key.DeadCharProcessed)
        {
            Render(); // show the modifiers held so far
            return;
        }

        var gesture = new HotkeyGesture(modifiers, key);
        if (!gesture.IsSuitableForGlobal)
        {
            _note = "Add Ctrl, Alt, Shift or Win";
            Render();
            return;
        }
        Commit(gesture.ToString());
    }

    void OnClearClick(object sender, RoutedEventArgs e) => Commit("");

    void Commit(string gesture)
    {
        _gesture = gesture;
        GestureChanged?.Invoke(gesture);
        StopRecording();
        Render();
    }

    void Render()
    {
        Frame.SetResourceReference(Border.BorderBrushProperty, _recording ? "Snappy.Ring" : "Snappy.KbdBorder");
        ClearButton.Visibility = !_recording && _gesture.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Keys.Children.Clear();

        if (_recording)
        {
            var held = ModifierNames(Keyboard.Modifiers);
            foreach (var name in held) Keys.Children.Add(Chip(name));
            Placeholder.Text = _note ?? (held.Count == 0 ? "Press a shortcut…" : "…");
        }
        else if (_gesture.Length == 0)
        {
            Placeholder.Text = "Not set";
        }
        else
        {
            foreach (var part in _gesture.Split('+')) Keys.Children.Add(Chip(part));
            Placeholder.Text = "";
        }
    }

    Border Chip(string text)
    {
        var label = new TextBlock { Text = text, Style = (Style)FindResource("Snappy.KbdText") };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Snappy.Foreground");
        return new Border { Style = (Style)FindResource("Snappy.Kbd"), Margin = new Thickness(0, 0, 4, 0), Child = label };
    }

    static List<string> ModifierNames(ModifierKeys modifiers)
    {
        var names = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) names.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) names.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) names.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) names.Add("Win");
        return names;
    }

    static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}

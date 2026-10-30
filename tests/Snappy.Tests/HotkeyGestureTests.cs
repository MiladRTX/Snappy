using System.Windows.Input;
using Snappy.Hotkeys;

namespace Snappy.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Shift+S", ModifierKeys.Control | ModifierKeys.Shift, Key.S)]
    [InlineData("ctrl + alt + f1", ModifierKeys.Control | ModifierKeys.Alt, Key.F1)]
    [InlineData("Win+PrintScreen", ModifierKeys.Windows, Key.PrintScreen)]
    [InlineData("Shift+PrtSc", ModifierKeys.Shift, Key.PrintScreen)]
    [InlineData("Control+1", ModifierKeys.Control, Key.D1)]
    [InlineData("F9", ModifierKeys.None, Key.F9)]
    public void Parses_common_gestures(string text, ModifierKeys modifiers, Key key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(new HotkeyGesture(modifiers, key), gesture);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Hyper+S")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+LeftShift")]
    [InlineData("Ctrl+42")]
    public void Rejects_invalid_gestures(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Ctrl+Shift+S")]
    [InlineData("Ctrl+Alt+F1")]
    [InlineData("Win+PrintScreen")]
    [InlineData("Ctrl+1")]
    public void Round_trips_through_text(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(text, gesture.ToString());
    }

    [Theory]
    [InlineData("Ctrl+S", true)]
    [InlineData("Win+Shift+A", true)]
    [InlineData("F9", true)]
    [InlineData("F24", true)]
    [InlineData("PrintScreen", true)]
    [InlineData("Insert", true)]
    [InlineData("Pause", true)]
    [InlineData("Scroll", true)]
    [InlineData("Apps", true)]
    [InlineData("Ctrl+Space", true)]
    [InlineData("S", false)]       // would break typing "s" in every app
    [InlineData("7", false)]
    [InlineData("NumPad5", false)]
    [InlineData("OemComma", false)]
    [InlineData("Space", false)]
    [InlineData("Enter", false)]
    [InlineData("Home", false)]
    [InlineData("PageDown", false)]
    [InlineData("Left", false)]
    [InlineData("CapsLock", false)]
    public void Only_shortcuts_that_leave_typing_alone_are_suitable_globally(string text, bool suitable)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(suitable, gesture.IsSuitableForGlobal);
    }

    [Fact]
    public void Maps_to_RegisterHotKey_values()
    {
        HotkeyGesture.TryParse("Ctrl+Shift+S", out var gesture);

        Assert.Equal(0x2u | 0x4u, gesture.NativeModifiers); // MOD_CONTROL | MOD_SHIFT
        Assert.Equal(0x53u, gesture.VirtualKey);             // VK 'S'
    }
}

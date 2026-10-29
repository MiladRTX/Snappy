namespace Snappy.Hotkeys;

/// <summary>Something a global shortcut can start.</summary>
public enum HotkeyAction
{
    CaptureRegion,
    CaptureWindow,
    CaptureScreen,
    CaptureAllDisplays,
}

/// <summary>Whether a configured shortcut is working.</summary>
public enum HotkeyState
{
    /// <summary>No shortcut set.</summary>
    Off,
    Active,
    /// <summary>The text isn't a shortcut Snappy understands.</summary>
    Invalid,
    /// <summary>Another app already registered it.</summary>
    InUse,
    /// <summary>Another Snappy action already uses it.</summary>
    Duplicate,
}

/// <summary>A shortcut's state; for duplicates, the action that already has it.</summary>
public readonly record struct HotkeyStatus(HotkeyState State, HotkeyAction? ConflictsWith = null);

public static class HotkeyActions
{
    public static IReadOnlyList<HotkeyAction> All { get; } = Enum.GetValues<HotkeyAction>();

    public static string Label(HotkeyAction action) => action switch
    {
        HotkeyAction.CaptureRegion => "Capture region",
        HotkeyAction.CaptureWindow => "Capture window",
        HotkeyAction.CaptureScreen => "Capture display",
        _ => "Capture all displays",
    };
}

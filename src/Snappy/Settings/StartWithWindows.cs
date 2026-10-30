using System.IO;
using Microsoft.Win32;

namespace Snappy.Settings;

/// <summary>What Windows will do with Snappy at sign-in.</summary>
internal enum StartupState
{
    /// <summary>No startup entry.</summary>
    Off,

    /// <summary>This copy of Snappy starts.</summary>
    On,

    /// <summary>The entry is there, but Windows' Startup apps list (Settings, Task Manager) has it turned off.</summary>
    TurnedOffInWindows,

    /// <summary>The entry starts a different Snappy.exe.</summary>
    OtherCopy,

    /// <summary>The entry points to a Snappy.exe that no longer exists, so nothing starts.</summary>
    MissingCopy,
}

/// <summary>Starts Snappy when the user signs in, through the per-user Run key (no admin rights needed).</summary>
internal static class StartWithWindows
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Where Windows records a startup app the user turned off in Settings > Apps > Startup or Task Manager.
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string ValueName = "Snappy";

    /// <summary>On the command line when Windows starts Snappy at sign-in, so it starts quietly.</summary>
    public const string StartupArgument = "--startup";

    static string ThisExe => Environment.ProcessPath ?? "";
    static string Command => $"\"{ThisExe}\" {StartupArgument}";

    public static StartupState State => Evaluate(ReadCommand(), ReadApproval(), ThisExe, File.Exists);

    public static bool IsEnabled => State == StartupState.On;

    /// <summary>The Snappy.exe the startup entry runs, if there is one.</summary>
    public static string? RegisteredExe => ReadCommand() is { } command ? ExePath(command) : null;

    /// <summary>
    /// On: the entry starts this copy, and a "turned off" mark in Windows' Startup apps list is
    /// cleared, since switching it on here is the newer choice. Off: both are removed.
    /// </summary>
    public static void Set(bool enabled)
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled) run.SetValue(ValueName, Command);
            else run.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Run at launch. An entry whose Snappy.exe is gone (moved, or rebuilt elsewhere) is pointed at
    /// this copy so sign-in still starts Snappy, and this copy's own entry is brought up to date.
    /// Windows' on/off choice in Startup apps is left as it is.
    /// </summary>
    public static void Maintain()
    {
        var state = State;
        if (state is not (StartupState.MissingCopy or StartupState.On or StartupState.TurnedOffInWindows)) return;
        if (state != StartupState.MissingCopy && !SamePath(RegisteredExe, ThisExe)) return;
        if (ReadCommand() == Command) return;
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        run.SetValue(ValueName, Command);
    }

    internal static StartupState Evaluate(string? command, byte[]? approval, string thisExe, Func<string, bool> exists)
    {
        if (command is null || ExePath(command) is not { } exe) return StartupState.Off;
        // Windows marks a turned-off startup app with an odd first byte (3); enabled is 2, or no mark at all.
        if (approval is { Length: > 0 } && approval[0] % 2 == 1) return StartupState.TurnedOffInWindows;
        if (!exists(exe)) return StartupState.MissingCopy;
        return SamePath(exe, thisExe) ? StartupState.On : StartupState.OtherCopy;
    }

    /// <summary>The program in a Run command: the quoted part, or everything before the first space.</summary>
    internal static string? ExePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        int space = command.IndexOf(' ');
        string path = space < 0 ? command : command[..space];
        return path.Length > 0 ? path : null;
    }

    static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    static string? ReadCommand()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        return run?.GetValue(ValueName) as string;
    }

    static byte[]? ReadApproval()
    {
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return approved?.GetValue(ValueName) as byte[];
    }
}

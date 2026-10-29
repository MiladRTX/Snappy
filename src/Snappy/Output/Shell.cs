using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Snappy.Output;

/// <summary>Opens files and folders the way Explorer would.</summary>
internal static class Shell
{
    public static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    public static void ShowInFolder(string path) =>
        Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();

    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Open(folder);
    }

    /// <summary>Opens a text file in its associated editor, falling back to Notepad.</summary>
    public static void OpenTextFile(string path)
    {
        try
        {
            Open(path);
        }
        catch (Win32Exception)
        {
            Process.Start("notepad.exe", $"\"{path}\"")?.Dispose();
        }
    }
}

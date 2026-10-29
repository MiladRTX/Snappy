using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Snappy.Output;

public static partial class FileNames
{
    /// <summary>
    /// Expands a file-name pattern. Each <c>{...}</c> holds a .NET date/time format, e.g.
    /// <c>Snappy_{yyyy-MM-dd_HH-mm-ss}</c>. Characters Windows forbids in file names become "_".
    /// </summary>
    public static string Format(string pattern, DateTime time)
    {
        string name;
        try
        {
            name = DateToken().Replace(pattern, m => time.ToString(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        catch (FormatException)
        {
            name = pattern;
        }

        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length == 0 ? "Snappy" : name;
    }

    /// <summary>"name.png", or "name (2).png", "name (3).png", ... when taken.</summary>
    public static string UniquePath(string folder, string name, string extension, Func<string, bool> exists)
    {
        var path = Path.Combine(folder, name + extension);
        for (int n = 2; exists(path); n++) path = Path.Combine(folder, $"{name} ({n}){extension}");
        return path;
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex DateToken();
}

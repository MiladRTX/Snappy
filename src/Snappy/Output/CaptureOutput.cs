using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snappy.Settings;

namespace Snappy.Output;

/// <summary>What happened to a capture. <see cref="Error"/> describes anything that failed.</summary>
public sealed record CaptureResult(BitmapSource Image, string? FilePath, bool Copied, string? Error);

public static class CaptureOutput
{
    public static async Task<CaptureResult> DeliverAsync(BitmapSource image, AppSettings settings)
    {
        byte[] png = await Task.Run(() => EncodePng(image));
        var problems = new List<string>();
        bool copied = false;
        string? path = null;

        if (settings.CopyToClipboard)
        {
            try
            {
                await CopyToClipboardAsync(image, png);
                copied = true;
            }
            catch (ExternalException ex)
            {
                Log.Error("Copy to clipboard", ex);
                problems.Add("Couldn't copy to the clipboard.");
            }
        }

        if (settings.SaveToFile)
        {
            try
            {
                path = await SaveAsync(png, settings.SaveFolder, settings.FileNamePattern);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Log.Error("Save capture", ex);
                problems.Add($"Couldn't save: {ex.Message}");
            }
        }

        return new CaptureResult(image, path, copied, problems.Count == 0 ? null : string.Join(" ", problems));
    }

    /// <summary>Lossless PNG, pixel-identical to the capture.</summary>
    public static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    static async Task<string> SaveAsync(byte[] png, string folderSetting, string pattern)
    {
        var folder = Environment.ExpandEnvironmentVariables(folderSetting);
        Directory.CreateDirectory(folder);
        var path = FileNames.UniquePath(folder, FileNames.Format(pattern, DateTime.Now), ".png", File.Exists);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await file.WriteAsync(png);
        return path;
    }

    static async Task CopyToClipboardAsync(BitmapSource image, byte[] png)
    {
        var data = new DataObject();
        // PNG for apps that understand it (browsers, chat apps, Office)...
        data.SetData("PNG", new MemoryStream(png), autoConvert: false);
        // ...and a bitmap for everything else. The capture's unused 4th byte is 0, which some apps
        // read as fully transparent, so hand them an explicitly opaque copy.
        var opaque = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        opaque.Freeze();
        data.SetImage(opaque);

        // Another app may briefly hold the clipboard open.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (ExternalException) when (attempt < 5)
            {
                await Task.Delay(50);
            }
        }
    }
}

# Snappy

A Windows snipping tool that captures **true native pixels**: every screenshot is the display's real
physical resolution, never downscaled by Windows display scaling, even on mixed-DPI multi-monitor setups.

## Run

```bash
dotnet run --project src/Snappy
```

Snappy lives in the notification area. Start a capture by:

- pressing the hotkey (default `Ctrl+Shift+S`, see Settings),
- left-clicking the tray icon, or
- launching Snappy again (from Start or a pinned taskbar icon).

## Capturing

The screen freezes and dims; the toolbar at the top switches modes.

| Key | Action |
| --- | --- |
| `R` | Region: drag a rectangle (the magnifier shows exact pixels and colors) |
| `W` | Window: click a window (captures its visible frame, without invisible borders) |
| `F` | Screen: click a display |
| `Enter` | In Screen mode: all displays; in Window mode: the hovered window |
| `Esc` / right-click | Cancel |

Each capture is copied to the clipboard (PNG and bitmap) and saved as a lossless PNG in
`Pictures\Snappy`. A notification shows the size and lets you open the file or its folder.

## Settings

`%AppData%\Snappy\settings.json` (tray menu → Settings). Changes apply as soon as you save.

| Setting | Default | |
| --- | --- | --- |
| `hotkey` | `Ctrl+Shift+S` | e.g. `Ctrl+Alt+S`, `Ctrl+PrintScreen`, `Win+F9` |
| `saveFolder` | `Pictures\Snappy` | environment variables like `%USERPROFILE%` work |
| `fileNamePattern` | `Snappy_{yyyy-MM-dd_HH-mm-ss}` | `{...}` holds a .NET date/time format |
| `saveToFile` | `true` | |
| `copyToClipboard` | `true` | |
| `showNotification` | `true` | errors are always shown |

If another app already owns the hotkey, Snappy says so in a notification; pick another one here.

## How captures stay native

- `app.manifest` declares **Per-Monitor V2** DPI awareness, so every Win32 coordinate Snappy sees
  (display bounds, window bounds, cursor, the `BitBlt` source) is in physical pixels.
- The whole virtual desktop is copied once with GDI `BitBlt` into a 32-bit DIB; selections are
  cropped from that frozen image.
- Each display gets its own overlay window pinned to its exact physical rectangle, showing its
  slice of the frozen image at 1:1, so a 125% and a 100% display both render crisply.

Known limits: UAC prompts, DRM video and windows that opt out of capture come out black; HDR
displays are captured as SDR.

## Tests

```bash
dotnet test
```

Includes a real capture of the machine's displays, checked against each display's video mode.

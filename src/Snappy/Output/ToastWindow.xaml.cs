using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Snappy.Capture;
using Snappy.Geometry;
using Snappy.Interop;
using Snappy.Ui;

namespace Snappy.Output;

internal enum ToastKind
{
    Info,
    Error,
}

/// <summary>
/// Notification card that slides in at the bottom-right of the display the cursor is on.
/// Never takes focus; hovering keeps it open. Only one is shown at a time.
/// </summary>
internal partial class ToastWindow : Window
{
    static ToastWindow? s_current;
    readonly DispatcherTimer _dismissTimer;
    string? _filePath;
    bool _dismissing;

    ToastWindow(TimeSpan lifetime)
    {
        InitializeComponent();
        _dismissTimer = new DispatcherTimer { Interval = lifetime };
        _dismissTimer.Tick += (_, _) => Dismiss();
        MouseEnter += (_, _) => _dismissTimer.Stop();
        MouseLeave += (_, _) =>
        {
            if (!_dismissing) _dismissTimer.Start();
        };
        ContentRendered += (_, _) =>
        {
            Root.BeginAnimation(OpacityProperty, Motion.Enter(0, 1, 220));
            Slide.BeginAnimation(TranslateTransform.XProperty, Motion.Enter(28, 0, 420));
            _dismissTimer.Start();
        };
        Closed += (_, _) =>
        {
            _dismissTimer.Stop();
            if (s_current == this) s_current = null;
        };
    }

    /// <summary>Closes the visible toast at once (so it stays out of the next screenshot).</summary>
    public static bool CloseCurrent()
    {
        if (s_current is not { } toast) return false;
        toast.Close();
        return true;
    }

    public static void ShowCapture(CaptureResult result)
    {
        var toast = new ToastWindow(TimeSpan.FromSeconds(result.Error is null ? 5 : 8));
        toast._filePath = result.FilePath;
        toast.Thumb.ImageSource = result.Image;
        toast.ThumbFrame.Visibility = Visibility.Visible;
        toast.TitleText.Text = result.Error is null ? "Screenshot captured" : "Screenshot captured, with a problem";

        var facts = new List<string> { $"{result.Image.PixelWidth} × {result.Image.PixelHeight} px" };
        if (result.Copied) facts.Add("Copied");
        if (result.FilePath is not null) facts.Add("Saved");
        toast.MessageText.Text = string.Join("  ·  ", facts);

        if (result.Error is not null)
        {
            toast.ShowDetail(result.Error);
            toast.DetailText.SetResourceReference(ForegroundProperty, "Snappy.Destructive");
        }
        else if (result.FilePath is not null)
        {
            toast.ShowDetail(Path.GetFileName(result.FilePath));
        }

        if (result.FilePath is not null)
        {
            toast.Actions.Visibility = Visibility.Visible;
            toast.Root.Cursor = Cursors.Hand;
        }
        toast.Present();
    }

    public static void ShowMessage(string title, string message, ToastKind kind = ToastKind.Info)
    {
        var toast = new ToastWindow(TimeSpan.FromSeconds(kind == ToastKind.Error ? 8 : 4));
        toast.IconBadge.Visibility = Visibility.Visible;
        toast.IconGlyph.Glyph = (Glyph)toast.FindResource(kind == ToastKind.Error ? "Icon.Warning" : "Icon.Region");
        if (kind == ToastKind.Error) toast.IconGlyph.SetResourceReference(ForegroundProperty, "Snappy.Destructive");
        toast.TitleText.Text = title;
        toast.MessageText.Text = message;
        toast.Present();
    }

    void ShowDetail(string text)
    {
        DetailText.Text = text;
        DetailText.Visibility = Visibility.Visible;
    }

    void Present()
    {
        CloseCurrent();
        s_current = this;

        Root.Measure(new Size(Width, double.PositiveInfinity));
        Height = Math.Ceiling(Root.DesiredSize.Height);

        var monitor = TargetMonitor();
        int width = (int)Math.Ceiling(Width * monitor.Scale);
        int height = (int)Math.Ceiling(Height * monitor.Scale);
        var work = monitor.WorkArea;
        WindowPlacement.Pin(this, new PhysicalRect(work.Right - width, work.Bottom - height, width, height), topmost: true, noActivate: true);
        Show();
    }

    void Dismiss()
    {
        if (_dismissing) return;
        _dismissing = true;
        _dismissTimer.Stop();
        var fade = Motion.Exit(0, 180);
        fade.Completed += (_, _) => Close();
        Root.BeginAnimation(OpacityProperty, fade);
        Slide.BeginAnimation(TranslateTransform.XProperty, Motion.Exit(16, 180));
    }

    static MonitorInfo TargetMonitor()
    {
        var monitors = DisplayEnumerator.GetMonitors();
        var cursor = NativeCursor.Position;
        return monitors.FirstOrDefault(m => m.Bounds.Contains(cursor))
            ?? monitors.FirstOrDefault(m => m.IsPrimary)
            ?? monitors[0];
    }

    void OnBodyClick(object sender, MouseButtonEventArgs e)
    {
        if (_filePath is null || IsInsideButton(e.OriginalSource as DependencyObject)) return;
        OpenFile();
    }

    void OnOpenClick(object sender, RoutedEventArgs e) => OpenFile();

    void OnShowInFolderClick(object sender, RoutedEventArgs e)
    {
        if (_filePath is not null) Shell.ShowInFolder(_filePath);
        Dismiss();
    }

    void OnCloseClick(object sender, RoutedEventArgs e) => Dismiss();

    void OpenFile()
    {
        if (_filePath is not null) Shell.Open(_filePath);
        Dismiss();
    }

    static bool IsInsideButton(DependencyObject? element)
    {
        for (; element is not null; element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
        {
            if (element is ButtonBase) return true;
        }
        return false;
    }
}

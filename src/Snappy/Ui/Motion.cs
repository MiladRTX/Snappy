using System.Windows;
using System.Windows.Media.Animation;
using Snappy.Interop;

namespace Snappy.Ui;

/// <summary>
/// Shared animation curves, so every transition in the app moves the same way: things appearing
/// or responding to a click start fast and settle gently; things leaving accelerate out.
/// Durations follow the usual scale: 100-150 ms for hover feedback, 150-250 ms for state
/// changes, 300-500 ms for elements entering. Animations run at the display's refresh rate,
/// and are instant when Windows' "Animation effects" setting is off.
/// </summary>
internal static class Motion
{
    static readonly IEasingFunction EnterCurve = new CubicBezierEase(0.16, 1, 0.3, 1);
    static readonly IEasingFunction ExitCurve = new CubicBezierEase(0.55, 0, 1, 0.45);

    /// <summary>Animation frames per second; WPF's default is 60 whatever the display.</summary>
    public static int FrameRate { get; private set; } = 60;

    /// <summary>
    /// Animates at the main display's refresh rate (e.g. 155 Hz) instead of WPF's 60 fps.
    /// Call once at startup, before any animation exists.
    /// </summary>
    public static void MatchDisplayRefreshRate()
    {
        FrameRate = Math.Max(60, NativeMethods.DisplayRefreshRate());
        try
        {
            // Covers the storyboards in XAML too (hover fades, button presses).
            Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline),
                new FrameworkPropertyMetadata { DefaultValue = (int?)FrameRate });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Already in use; animations created below still get the rate explicitly.
        }
    }

    /// <summary>Something appearing or responding to the user: a fast start that settles gently.</summary>
    public static DoubleAnimation Enter(double to, int milliseconds) =>
        Smooth(new DoubleAnimation(to, Duration(milliseconds)) { EasingFunction = EnterCurve });

    public static DoubleAnimation Enter(double from, double to, int milliseconds) =>
        Smooth(new DoubleAnimation(from, to, Duration(milliseconds)) { EasingFunction = EnterCurve });

    public static RectAnimation Enter(Rect from, Rect to, int milliseconds) =>
        Smooth(new RectAnimation(from, to, Duration(milliseconds)) { EasingFunction = EnterCurve });

    /// <summary>Something leaving: a gentle start that finishes quickly.</summary>
    public static DoubleAnimation Exit(double to, int milliseconds) =>
        Smooth(new DoubleAnimation(to, Duration(milliseconds)) { EasingFunction = ExitCurve });

    /// <summary>Moves a property to <paramref name="to"/>, animated or at once.</summary>
    public static void Apply<T>(T target, DependencyProperty property, double to, int milliseconds, bool animate)
        where T : DependencyObject, IAnimatable
    {
        if (animate)
        {
            target.BeginAnimation(property, Enter(to, milliseconds));
            return;
        }
        target.BeginAnimation(property, null);
        target.SetValue(property, to);
    }

    static TTimeline Smooth<TTimeline>(TTimeline timeline) where TTimeline : Timeline
    {
        Timeline.SetDesiredFrameRate(timeline, FrameRate);
        return timeline;
    }

    static Duration Duration(int milliseconds) =>
        new(TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? milliseconds : 0));
}

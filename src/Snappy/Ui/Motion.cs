using System.Windows;
using System.Windows.Media.Animation;

namespace Snappy.Ui;

/// <summary>
/// Shared animation curves, so every transition in the app moves the same way: things appearing
/// decelerate in, things leaving accelerate out, things changing state ease in and out. Durations
/// follow the usual scale: 100-150 ms for hover feedback, 200-300 ms for small state changes,
/// 300-500 ms for elements entering. When Windows' "Animation effects" setting is off, every
/// transition is instant.
/// </summary>
internal static class Motion
{
    static readonly IEasingFunction EnterCurve = new CubicBezierEase(0.16, 1, 0.3, 1);
    static readonly IEasingFunction ExitCurve = new CubicBezierEase(0.55, 0, 1, 0.45);
    static readonly IEasingFunction MoveCurve = new CubicBezierEase(0.65, 0, 0.35, 1);

    /// <summary>Something appearing: a fast start that settles gently.</summary>
    public static DoubleAnimation Enter(double to, int milliseconds) =>
        new(to, Duration(milliseconds)) { EasingFunction = EnterCurve };

    public static DoubleAnimation Enter(double from, double to, int milliseconds) =>
        new(from, to, Duration(milliseconds)) { EasingFunction = EnterCurve };

    /// <summary>Something leaving: a gentle start that finishes quickly.</summary>
    public static DoubleAnimation Exit(double to, int milliseconds) =>
        new(to, Duration(milliseconds)) { EasingFunction = ExitCurve };

    /// <summary>Something moving from one state to another, like the mode pill.</summary>
    public static DoubleAnimation Move(double to, int milliseconds) =>
        new(to, Duration(milliseconds)) { EasingFunction = MoveCurve };

    public static RectAnimation Move(Rect from, Rect to, int milliseconds) =>
        new(from, to, Duration(milliseconds)) { EasingFunction = MoveCurve };

    /// <summary>Moves a property to <paramref name="to"/>, animated or at once.</summary>
    public static void Apply<T>(T target, DependencyProperty property, double to, int milliseconds, bool animate)
        where T : DependencyObject, IAnimatable
    {
        if (animate)
        {
            target.BeginAnimation(property, Move(to, milliseconds));
            return;
        }
        target.BeginAnimation(property, null);
        target.SetValue(property, to);
    }

    static Duration Duration(int milliseconds) =>
        new(TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? milliseconds : 0));
}

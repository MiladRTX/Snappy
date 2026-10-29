using System.Windows.Media.Animation;

namespace Snappy.Ui;

/// <summary>
/// A CSS-style <c>cubic-bezier(x1, y1, x2, y2)</c> timing curve, so animations can use the same
/// curves as web design systems. The curve runs from (0,0) to (1,1); the input is time, the
/// output is progress.
/// </summary>
internal sealed class CubicBezierEase(double x1, double y1, double x2, double y2) : IEasingFunction
{
    const double Epsilon = 1e-6;

    public double Ease(double normalizedTime)
    {
        if (normalizedTime <= 0) return 0;
        if (normalizedTime >= 1) return 1;
        return Bezier(SolveForT(normalizedTime), y1, y2);
    }

    /// <summary>Finds the curve parameter whose x is <paramref name="x"/>: Newton's method, then bisection.</summary>
    double SolveForT(double x)
    {
        double t = x;
        for (int i = 0; i < 8; i++)
        {
            double error = Bezier(t, x1, x2) - x;
            if (Math.Abs(error) < Epsilon) return t;
            double slope = Slope(t, x1, x2);
            if (Math.Abs(slope) < Epsilon) break;
            t = Math.Clamp(t - error / slope, 0, 1);
        }

        double low = 0, high = 1;
        t = x;
        for (int i = 0; i < 40; i++)
        {
            double value = Bezier(t, x1, x2);
            if (Math.Abs(value - x) < Epsilon) break;
            if (value < x) low = t;
            else high = t;
            t = (low + high) / 2;
        }
        return t;
    }

    static double Bezier(double t, double p1, double p2)
    {
        double u = 1 - t;
        return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
    }

    static double Slope(double t, double p1, double p2)
    {
        double u = 1 - t;
        return 3 * u * u * p1 + 6 * u * t * (p2 - p1) + 3 * t * t * (1 - p2);
    }
}

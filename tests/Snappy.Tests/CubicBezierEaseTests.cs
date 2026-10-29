using Snappy.Ui;

namespace Snappy.Tests;

public class CubicBezierEaseTests
{
    static readonly CubicBezierEase Enter = new(0.16, 1, 0.3, 1);
    static readonly CubicBezierEase Exit = new(0.55, 0, 1, 0.45);
    static readonly CubicBezierEase Move = new(0.65, 0, 0.35, 1);

    [Fact]
    public void Starts_at_zero_and_ends_at_one()
    {
        foreach (var curve in new[] { Enter, Exit, Move })
        {
            Assert.Equal(0, curve.Ease(0));
            Assert.Equal(1, curve.Ease(1));
        }
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void Linear_control_points_give_a_straight_line(double t)
    {
        Assert.Equal(t, new CubicBezierEase(0.25, 0.25, 0.75, 0.75).Ease(t), precision: 4);
    }

    [Fact]
    public void Enter_curve_front_loads_the_motion()
    {
        Assert.True(Enter.Ease(0.5) > 0.9);
    }

    [Fact]
    public void Exit_curve_back_loads_the_motion()
    {
        Assert.True(Exit.Ease(0.5) < 0.2);
    }

    [Fact]
    public void Move_curve_is_symmetric_around_the_midpoint()
    {
        Assert.Equal(0.5, Move.Ease(0.5), precision: 3);
        Assert.Equal(1 - Move.Ease(0.2), Move.Ease(0.8), precision: 3);
    }

    [Fact]
    public void Progress_never_goes_backwards()
    {
        foreach (var curve in new[] { Enter, Exit, Move })
        {
            double previous = 0;
            for (int i = 1; i <= 100; i++)
            {
                double value = curve.Ease(i / 100.0);
                Assert.True(value >= previous - 1e-9, $"dropped at t={i / 100.0}");
                previous = value;
            }
        }
    }
}

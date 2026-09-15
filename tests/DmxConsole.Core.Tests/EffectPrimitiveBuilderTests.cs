using DmxConsole.Core.Effects;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Tests;

public sealed class EffectPrimitiveBuilderTests
{
    [Fact]
    public void Rainbow_BuildsContinuousSixStepRgbWheel()
    {
        var steps = EffectPrimitiveBuilder.BuildRainbow(1);

        Assert.Equal(6, steps.Count);
        Assert.Equal(255, steps[0].AbsoluteValues[ChannelType.ColorRed]);
        Assert.Equal(0, steps[0].AbsoluteValues[ChannelType.ColorGreen]);
        Assert.Equal(0, steps[0].AbsoluteValues[ChannelType.ColorBlue]);
        Assert.All(steps, step => Assert.Equal(3, step.AbsoluteValues.Count));
    }

    [Fact]
    public void Sine_BuildsCanonicalTwoStepPhaserCurve()
    {
        var steps = EffectPrimitiveBuilder.Build(EffectPrimitiveKind.Sine, ChannelType.Dimmer, 10, 240);

        Assert.Equal(2, steps.Count);
        Assert.Equal(10, steps[0].AbsoluteValues[ChannelType.Dimmer]);
        Assert.Equal(240, steps[1].AbsoluteValues[ChannelType.Dimmer]);
        Assert.All(steps, step =>
        {
            Assert.Equal(100, step.Width);
            Assert.Equal(100, step.Transition);
            Assert.Equal(-100, step.Accel);
            Assert.Equal(-100, step.Decel);
        });
    }

    [Fact]
    public void Ramp_HoldsItsReturnStepAtMaximum()
    {
        var steps = EffectPrimitiveBuilder.Build(EffectPrimitiveKind.Ramp, ChannelType.Pan, 0, 255);

        Assert.Equal(2, steps.Count);
        Assert.Equal(100, steps[0].Width);
        Assert.Equal(100, steps[0].Transition);
        Assert.Equal(0, steps[1].Width);
        Assert.Equal(0, steps[1].Transition);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 20)]
    [InlineData(100, 99)]
    public void Square_ClampsDutyCycleAndUsesSnapTransitions(double requested, double expected)
    {
        var steps = EffectPrimitiveBuilder.Build(
            EffectPrimitiveKind.Square, ChannelType.Dimmer, 0, 255, requested);

        Assert.Equal(expected, steps[0].Width);
        Assert.Equal(100 - expected, steps[1].Width);
        Assert.All(steps, step => Assert.Equal(0, step.Transition));
    }
}

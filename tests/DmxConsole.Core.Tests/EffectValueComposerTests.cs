using DmxConsole.Core.Effects;

namespace DmxConsole.Core.Tests;

public sealed class EffectValueComposerTests
{
    [Theory]
    [InlineData(AddMode.Abs)]
    [InlineData(AddMode.Normal)]
    [InlineData(AddMode.Plus)]
    [InlineData(AddMode.Minus)]
    public void AbsoluteOnly_AlwaysProducesStoredAbsolute(AddMode mode)
    {
        Assert.Equal(200, EffectValueComposer.Compose(80, 200, 0, mode));
    }

    [Theory]
    [InlineData(AddMode.Abs, 20, 100)]
    [InlineData(AddMode.Normal, 20, 100)]
    [InlineData(AddMode.Plus, 20, 100)]
    [InlineData(AddMode.Minus, 20, 80)]
    [InlineData(AddMode.Abs, -20, 60)]
    [InlineData(AddMode.Normal, -20, 60)]
    [InlineData(AddMode.Plus, -20, 80)]
    [InlineData(AddMode.Minus, -20, 60)]
    public void RelativeOnly_ComposesAgainstLowerTierBase(AddMode mode, double delta, byte expected)
    {
        Assert.Equal(expected, EffectValueComposer.Compose(80, null, delta, mode));
    }

    [Theory]
    [InlineData(AddMode.Abs, 20, 220)]
    [InlineData(AddMode.Normal, 20, 220)]
    [InlineData(AddMode.Plus, 20, 220)]
    [InlineData(AddMode.Minus, 20, 200)]
    [InlineData(AddMode.Abs, -20, 180)]
    [InlineData(AddMode.Normal, -20, 180)]
    [InlineData(AddMode.Plus, -20, 200)]
    [InlineData(AddMode.Minus, -20, 180)]
    public void AbsoluteAndRelative_BothHaveExplicitMeaning(
        AddMode mode, double delta, byte expected)
    {
        Assert.Equal(expected, EffectValueComposer.Compose(80, 200, delta, mode));
    }

    [Theory]
    [InlineData(250, 20, 255)]
    [InlineData(5, -20, 0)]
    public void Result_IsClampedToDmxRange(double absolute, double delta, byte expected)
    {
        Assert.Equal(expected,
            EffectValueComposer.Compose(0, absolute, delta, AddMode.Normal));
    }
}

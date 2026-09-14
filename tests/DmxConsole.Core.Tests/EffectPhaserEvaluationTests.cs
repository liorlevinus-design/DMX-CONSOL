using DmxConsole.Core.Effects;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Tests;

public sealed class EffectPhaserEvaluationTests
{
    [Fact]
    public void LinearTwoStep_TransitionsToNextStepDuringCurrentWidth()
    {
        var phaser = Build(EffectPrimitiveKind.Triangle, min: 0, max: 200);

        phaser.Tick(TimeSpan.Zero);
        AssertValue(phaser, 0);

        phaser.Tick(TimeSpan.FromSeconds(0.25));
        AssertValue(phaser, 100);

        phaser.Tick(TimeSpan.FromSeconds(0.5));
        AssertValue(phaser, 200);
    }

    [Fact]
    public void ZeroTransition_SnapsImmediatelyToNextStep()
    {
        var fixture = DimmerFixture();
        var phaser = new EffectPhaser
        {
            Fixtures = new[] { fixture },
            Steps = new[]
            {
                Step(10, transition: 0),
                Step(200, transition: 0),
            },
        };

        phaser.Tick(TimeSpan.Zero);

        AssertValue(phaser, 200);
    }

    [Fact]
    public void PartialTransition_HoldsNextValueForRemainderOfStep()
    {
        var fixture = DimmerFixture();
        var phaser = new EffectPhaser
        {
            Fixtures = new[] { fixture },
            Steps = new[]
            {
                Step(0, transition: 25),
                Step(200, transition: 25),
            },
        };

        phaser.Tick(TimeSpan.FromSeconds(0.20));

        AssertValue(phaser, 200);
    }

    [Fact]
    public void RelativeStep_ComposesAgainstFrozenLowerTierBase()
    {
        var fixture = DimmerFixture();
        var phaser = new EffectPhaser
        {
            Fixtures = new[] { fixture },
            AddMode = AddMode.Normal,
            Steps = new[]
            {
                new EffectStep
                {
                    RelativeValues = new Dictionary<ChannelType, double> { [ChannelType.Dimmer] = 20 },
                },
            },
        };

        phaser.Tick(TimeSpan.Zero);

        Assert.True(phaser.TryGetChannelValue(0, 0, 80, out var value));
        Assert.Equal(100, value);
    }

    [Fact]
    public void TickEvaluation_DoesNotMutateRevision()
    {
        var phaser = Build(EffectPrimitiveKind.Sine, 0, 255);
        phaser.MarkAllControlledChannelsChanged();
        phaser.TryGetRevision(0, 0, out var before);

        for (int i = 0; i < 10; i++)
        {
            phaser.Tick(TimeSpan.FromMilliseconds(i * 25));
            Assert.True(phaser.TryGetChannelValue(0, 0, 40, out _));
        }

        Assert.True(phaser.TryGetRevision(0, 0, out var after));
        Assert.Equal(before, after);
    }

    private static EffectPhaser Build(EffectPrimitiveKind kind, double min, double max) => new()
    {
        Fixtures = new[] { DimmerFixture() },
        Steps = EffectPrimitiveBuilder.Build(kind, ChannelType.Dimmer, min, max),
        SpeedHz = 1,
    };

    private static EffectStep Step(double value, double transition) => new()
    {
        AbsoluteValues = new Dictionary<ChannelType, double> { [ChannelType.Dimmer] = value },
        Width = 100,
        Transition = transition,
    };

    private static void AssertValue(EffectPhaser phaser, byte expected)
    {
        Assert.True(phaser.TryGetChannelValue(0, 0, 0, out var value));
        Assert.Equal(expected, value);
    }

    private static PatchedFixture DimmerFixture()
    {
        var profile = new FixtureProfile
        {
            Id = "phaser-dimmer",
            Manufacturer = "Test",
            Model = "Dimmer",
            Modes = new[]
            {
                new FixtureMode
                {
                    Name = "1ch",
                    Channels = new[]
                    {
                        new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 },
                    },
                },
            },
        };
        return new PatchedFixture(profile, profile.Modes[0], 0, 1);
    }
}

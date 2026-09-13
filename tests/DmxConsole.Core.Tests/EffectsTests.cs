using DmxConsole.Core.Effects;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using Xunit;

namespace DmxConsole.Core.Tests;

public class EffectsTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer",
        Manufacturer = "Test",
        Model = "Dimmer",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "1ch",
                Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } },
            },
        },
    };

    private static FixtureProfile Rgb3() => new()
    {
        Id = "test-rgb",
        Manufacturer = "Test",
        Model = "RGB",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "3ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 0 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 1 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 2 },
                },
            },
        },
    };

    private static List<PatchedFixture> PatchDimmers(int count)
    {
        var profile = Dimmer1();
        var list = new List<PatchedFixture>();
        for (int i = 0; i < count; i++)
            list.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1 + i, $"Dim{i}"));
        return list;
    }

    [Fact]
    public void ChaseEffect_LightsOnlyTheActiveStep()
    {
        var fixtures = PatchDimmers(4);
        var chase = new ChaseEffect { Fixtures = fixtures, SpeedHz = 1.0, Width = 1, OnValue = 255, OffValue = 0 };

        // t=0 -> step 0 active
        var atStart = chase.Evaluate(0.0).ToDictionary(e => e.Key, e => e.Value);
        Assert.Equal(255, atStart[(0, fixtures[0].StartIndex)]);
        Assert.Equal(0, atStart[(0, fixtures[1].StartIndex)]);

        // t=0.26 of a 1s cycle over 4 steps -> step 1 active
        var later = chase.Evaluate(0.26).ToDictionary(e => e.Key, e => e.Value);
        Assert.Equal(0, later[(0, fixtures[0].StartIndex)]);
        Assert.Equal(255, later[(0, fixtures[1].StartIndex)]);
    }

    [Fact]
    public void StrobeEffect_TogglesByDutyCycle()
    {
        var fixtures = PatchDimmers(1);
        var strobe = new StrobeEffect { Fixtures = fixtures, SpeedHz = 1.0, DutyCycle = 0.5, OnValue = 255, OffValue = 0 };

        var on = strobe.Evaluate(0.1).Single();
        Assert.Equal(255, on.Value);

        var off = strobe.Evaluate(0.6).Single();
        Assert.Equal(0, off.Value);
    }

    [Fact]
    public void SineEffect_PeaksAtQuarterCycle()
    {
        var fixtures = PatchDimmers(1);
        var sine = new SineEffect { Fixtures = fixtures, SpeedHz = 1.0, Min = 0, Max = 255 };

        var peak = sine.Evaluate(0.25).Single();
        Assert.Equal(255, peak.Value);

        var trough = sine.Evaluate(0.75).Single();
        Assert.Equal(0, trough.Value);
    }

    [Fact]
    public void RainbowEffect_ProducesPureRedAtHueZero()
    {
        var fixtures = new List<PatchedFixture> { new(Rgb3(), Rgb3().Modes[0], 0, 1) };
        var rainbow = new RainbowEffect { Fixtures = fixtures, SpeedHz = 1.0, Brightness = 1.0 };

        var values = rainbow.Evaluate(0.0).ToDictionary(e => e.Key, e => e.Value);
        Assert.Equal(255, values[(0, 0)]); // Red
        Assert.Equal(0, values[(0, 1)]);   // Green
        Assert.Equal(0, values[(0, 2)]);   // Blue
    }

    [Fact]
    public void RainbowEffect_SkipsFixturesWithoutFullRgb()
    {
        var fixtures = PatchDimmers(1); // no color channels at all
        var rainbow = new RainbowEffect { Fixtures = fixtures };

        Assert.Empty(rainbow.Evaluate(0.0));
    }

    [Fact]
    public void EffectsEngine_TickThenQuery_ReflectsRunningEffect()
    {
        var fixtures = PatchDimmers(2);
        var chase = new ChaseEffect { Fixtures = fixtures, SpeedHz = 1.0, Width = 1 };

        var engine = new EffectsEngine();
        engine.Effects.Add(chase);
        engine.Tick(TimeSpan.Zero);

        Assert.True(engine.TryGetChannelValue(0, fixtures[0].StartIndex, out var v0));
        Assert.Equal(255, v0);
        Assert.True(engine.TryGetChannelValue(0, fixtures[1].StartIndex, out var v1));
        Assert.Equal(0, v1);
    }

    [Fact]
    public void EffectsEngine_DisabledEffect_DoesNotContribute()
    {
        var fixtures = PatchDimmers(1);
        var sine = new SineEffect { Fixtures = fixtures, Enabled = false };

        var engine = new EffectsEngine();
        engine.Effects.Add(sine);
        engine.Tick(TimeSpan.Zero);

        Assert.False(engine.IsActive);
        Assert.False(engine.TryGetChannelValue(0, fixtures[0].StartIndex, out _));
    }
}

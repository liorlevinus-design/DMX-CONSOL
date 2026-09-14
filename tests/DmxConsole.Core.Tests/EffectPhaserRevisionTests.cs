using DmxConsole.Core.Effects;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Tests;

public sealed class EffectPhaserRevisionTests
{
    [Fact]
    public void ParameterEdit_BumpsOnlyTheExactFixtureAddress()
    {
        var fixtures = TwoPanFixtures();
        var phaser = PanPhaser(fixtures);

        phaser.MarkAllControlledChannelsChanged();
        Assert.True(phaser.TryGetRevision(0, 0, out var firstBefore));
        Assert.True(phaser.TryGetRevision(0, 1, out var secondBefore));

        phaser.MarkChannelsChanged(new[] { (fixtures[0], ChannelType.Pan) });

        Assert.True(phaser.TryGetRevision(0, 0, out var firstAfter));
        Assert.True(phaser.TryGetRevision(0, 1, out var secondAfter));
        Assert.True(firstAfter > firstBefore);
        Assert.Equal(secondBefore, secondAfter);
    }

    [Fact]
    public void GlobalTimingChange_BumpsEveryControlledAddress()
    {
        var fixtures = TwoPanFixtures();
        var phaser = PanPhaser(fixtures);
        phaser.MarkAllControlledChannelsChanged();
        phaser.TryGetRevision(0, 0, out var firstBefore);
        phaser.TryGetRevision(0, 1, out var secondBefore);

        phaser.MarkAllControlledChannelsChanged();

        Assert.True(phaser.TryGetRevision(0, 0, out var firstAfter));
        Assert.True(phaser.TryGetRevision(0, 1, out var secondAfter));
        Assert.True(firstAfter > firstBefore);
        Assert.True(secondAfter > secondBefore);
        Assert.Equal(firstAfter, secondAfter);
    }

    [Fact]
    public void Tick_NeverChangesChannelRevision()
    {
        var fixtures = TwoPanFixtures();
        var phaser = PanPhaser(fixtures);
        phaser.MarkAllControlledChannelsChanged();
        phaser.TryGetRevision(0, 0, out var before);

        phaser.Tick(TimeSpan.FromSeconds(10));
        phaser.Tick(TimeSpan.FromSeconds(20));

        Assert.True(phaser.TryGetRevision(0, 0, out var after));
        Assert.Equal(before, after);
        Assert.Equal(TimeSpan.FromSeconds(20), phaser.Elapsed);
    }

    private static EffectPhaser PanPhaser(IReadOnlyList<PatchedFixture> fixtures) => new()
    {
        Fixtures = fixtures,
        Steps = EffectPrimitiveBuilder.Build(EffectPrimitiveKind.Sine, ChannelType.Pan, 0, 255),
    };

    private static IReadOnlyList<PatchedFixture> TwoPanFixtures()
    {
        var profile = new FixtureProfile
        {
            Id = "two-pan",
            Manufacturer = "Test",
            Model = "Pan",
            Modes = new[]
            {
                new FixtureMode
                {
                    Name = "1ch",
                    Channels = new[]
                    {
                        new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 },
                    },
                },
            },
        };

        return new[]
        {
            new PatchedFixture(profile, profile.Modes[0], 0, 1),
            new PatchedFixture(profile, profile.Modes[0], 0, 2),
        };
    }
}

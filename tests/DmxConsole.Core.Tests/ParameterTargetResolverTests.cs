using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Core.Tests;

/// <summary>
/// CLAUDE.md §16 (PSEL-1/PSEL-2/PSEL-5) - <see cref="ParameterTargetResolver"/> is the one shared
/// primitive that turns (ordered Fixture Selection) x (ordered Parameter Selection) into concrete
/// (fixture, channel) write targets. Exercised directly against Core types, with no
/// Application/Web/grammar wiring involved - the Parameter-AT grammar slice's own tests
/// (DmxConsole.Application.Tests) build on top of this and never re-derive compatibility
/// themselves.
/// </summary>
public class ParameterTargetResolverTests
{
    private static FixtureProfile MovingHeadColor() => new()
    {
        Id = "test-mover-color",
        Manufacturer = "Test",
        Model = "MoverColor",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "6ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 },
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 1 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2 },
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 3 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 4 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 5 },
                },
            },
        },
    };

    private static FixtureProfile ColorOnlyPar() => new()
    {
        Id = "test-par-color",
        Manufacturer = "Test",
        Model = "ParColor",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "4ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 },
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 1 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 2 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 3 },
                },
            },
        },
    };

    private static FixtureProfile PanTiltFine() => new()
    {
        Id = "test-mover-fine",
        Manufacturer = "Test",
        Model = "MoverFine",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "4ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 },
                    new FixtureChannel { Name = "Pan Fine", Type = ChannelType.PanFine, Offset = 1 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2 },
                    new FixtureChannel { Name = "Tilt Fine", Type = ChannelType.TiltFine, Offset = 3 },
                },
            },
        },
    };

    private static PatchedFixture Make(FixtureProfile profile, int address = 1) =>
        new(profile, profile.Modes[0], universeId: 0, startAddress: address);

    // ---- (1) RED resolves only on fixtures supporting RED ----
    [Fact]
    public void Resolve_Red_OnlyMatchesFixturesWithRedChannel()
    {
        var mover = Make(MovingHeadColor());
        var dimmerOnly = Make(TestFixtureProfiles.DimmerOnly());

        var result = ParameterTargetResolver.Resolve(new[] { mover, dimmerOnly }, new[] { ChannelType.ColorRed });

        Assert.Single(result.Targets);
        Assert.Equal(mover, result.Targets[0].Fixture);
        Assert.Equal(ChannelType.ColorRed, result.Targets[0].Parameter);
        Assert.Single(result.Skipped);
        Assert.Equal(dimmerOnly, result.Skipped[0].Fixture);
    }

    // ---- (2) PAN resolves only on fixtures supporting PAN ----
    [Fact]
    public void Resolve_Pan_OnlyMatchesFixturesWithPanChannel()
    {
        var mover = Make(MovingHeadColor());
        var par = Make(ColorOnlyPar());

        var result = ParameterTargetResolver.Resolve(new[] { mover, par }, new[] { ChannelType.Pan });

        Assert.Single(result.Targets);
        Assert.Equal(mover, result.Targets[0].Fixture);
        Assert.Single(result.Skipped);
        Assert.Equal(par, result.Skipped[0].Fixture);
    }

    // ---- (3) incompatible fixtures skipped, not failed ----
    [Fact]
    public void Resolve_MixedCompatibility_NeverThrows_ReportsSkippedSeparately()
    {
        var mover1 = Make(MovingHeadColor(), 1);
        var par = Make(ColorOnlyPar(), 10);
        var mover2 = Make(MovingHeadColor(), 20);

        var result = ParameterTargetResolver.Resolve(new[] { mover1, par, mover2 }, new[] { ChannelType.Pan });

        Assert.Equal(2, result.Targets.Count);
        Assert.Equal(new[] { mover1, mover2 }, result.Targets.Select(t => t.Fixture));
        Assert.Single(result.Skipped);
        Assert.Equal(par, result.Skipped[0].Fixture);
    }

    // ---- (4) PanFine/TiltFine fold correctly ----
    [Fact]
    public void Resolve_LogicalPan_FindsCoarseChannel_EvenOnAFineOnlyOrPairedProfile()
    {
        var fixture = Make(PanTiltFine());

        var result = ParameterTargetResolver.Resolve(new[] { fixture }, new[] { ChannelType.Pan, ChannelType.Tilt });

        Assert.Equal(2, result.Targets.Count);
        Assert.Equal(ChannelType.Pan, result.Targets[0].Channel.Type); // primary/coarse, never Fine
        Assert.Equal(ChannelType.Tilt, result.Targets[1].Channel.Type);
        Assert.Empty(result.Skipped);
    }

    // ---- (5) fixture order preserved ----
    [Fact]
    public void Resolve_PreservesFixtureOrder()
    {
        var f1 = Make(MovingHeadColor(), 1);
        var f2 = Make(MovingHeadColor(), 10);
        var f3 = Make(MovingHeadColor(), 20);

        var result = ParameterTargetResolver.Resolve(new[] { f3, f1, f2 }, new[] { ChannelType.ColorRed });

        Assert.Equal(new[] { f3, f1, f2 }, result.Targets.Select(t => t.Fixture));
    }

    // ---- (6) multiple parameters preserve selection order ----
    [Fact]
    public void Resolve_MultipleParameters_PreserveParameterSelectionOrderPerFixture()
    {
        var fixture = Make(MovingHeadColor());

        var result = ParameterTargetResolver.Resolve(new[] { fixture }, new[] { ChannelType.ColorGreen, ChannelType.ColorRed });

        Assert.Equal(new[] { ChannelType.ColorGreen, ChannelType.ColorRed }, result.Targets.Select(t => t.Parameter));
    }

    // ---- (7) no duplicate logical targets ----
    [Fact]
    public void Resolve_DuplicateParameterRequest_NeverProducesADuplicateTarget()
    {
        var fixture = Make(MovingHeadColor());

        var result = ParameterTargetResolver.Resolve(new[] { fixture }, new[] { ChannelType.ColorRed, ChannelType.ColorRed });

        Assert.Single(result.Targets);
    }
}

/// <summary>Shared minimal profiles reused by more than one test in this file/other Core tests.</summary>
internal static class TestFixtureProfiles
{
    public static FixtureProfile DimmerOnly() => new()
    {
        Id = "test-dimmer-only",
        Manufacturer = "Test",
        Model = "DimmerOnly",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "1ch",
                Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } },
            },
        },
    };
}

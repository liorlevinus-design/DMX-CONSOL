using DmxConsole.Core.Effects;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Tests;

public sealed class EffectPhaseCalculatorTests
{
    [Fact]
    public void ForwardSpread_MatchesDocumentedEvenCycleOffsets()
    {
        var fixtures = Fixtures(4);

        Assert.Equal(new[] { 0, 0.25, 0.5, 0.75 },
            Offsets(fixtures, spread: 1, direction: EffectDirection.Forward));
    }

    [Fact]
    public void Backward_ReversesFixtureOrdering()
    {
        var fixtures = Fixtures(4);

        Assert.Equal(new[] { 0.75, 0.5, 0.25, 0 },
            Offsets(fixtures, spread: 1, direction: EffectDirection.Backward));
    }

    [Fact]
    public void Parts_RepeatsCycleAcrossFixtureGroup()
    {
        var fixtures = Fixtures(6);

        Assert.Equal(new[] { 0, 0.5, 0, 0.5, 0, 0.5 },
            Offsets(fixtures, spread: 1, parts: 2));
    }

    [Fact]
    public void Segments_GivesAdjacentFixturesSameOffset()
    {
        var fixtures = Fixtures(6);

        Assert.Equal(new[] { 0, 0, 1.0 / 3, 1.0 / 3, 2.0 / 3, 2.0 / 3 },
            Offsets(fixtures, spread: 1, segments: 2));
    }

    [Fact]
    public void CenterDirections_PairFixturesSymmetrically()
    {
        var fixtures = Fixtures(6);

        Assert.Equal(new[] { 0, 1.0 / 6, 2.0 / 6, 2.0 / 6, 1.0 / 6, 0 },
            Offsets(fixtures, 1, direction: EffectDirection.CenterIn));
        Assert.Equal(new[] { 2.0 / 6, 1.0 / 6, 0, 0, 1.0 / 6, 2.0 / 6 },
            Offsets(fixtures, 1, direction: EffectDirection.CenterOut));
    }

    [Fact]
    public void Random_IsStableAndProducesOnePermutation()
    {
        var fixtures = Fixtures(5);
        var seed = Guid.NewGuid();

        var first = Offsets(fixtures, 1, direction: EffectDirection.Random, seed: seed);
        var second = Offsets(fixtures, 1, direction: EffectDirection.Random, seed: seed);

        Assert.Equal(first, second);
        Assert.Equal(5, first.Distinct().Count());
    }

    private static double[] Offsets(
        IReadOnlyList<PatchedFixture> fixtures,
        double spread,
        int? parts = null,
        int? segments = null,
        EffectDirection direction = EffectDirection.Forward,
        Guid? seed = null) =>
        Enumerable.Range(0, fixtures.Count)
            .Select(index => EffectPhaseCalculator.OffsetFor(
                fixtures, index, spread, parts, segments, direction, seed ?? Guid.Empty))
            .ToArray();

    private static IReadOnlyList<PatchedFixture> Fixtures(int count)
    {
        var profile = new FixtureProfile
        {
            Id = "phase-dimmer",
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

        return Enumerable.Range(0, count)
            .Select(index => new PatchedFixture(profile, profile.Modes[0], 0, index + 1))
            .ToArray();
    }
}

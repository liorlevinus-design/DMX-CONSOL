using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Effects;

/// <summary>Calculates fixture offsets independently from waveform/value evaluation.</summary>
public static class EffectPhaseCalculator
{
    public static double OffsetFor(
        IReadOnlyList<PatchedFixture> fixtures,
        int fixtureIndex,
        double spread,
        int? parts,
        int? segments,
        EffectDirection direction,
        Guid randomSeed)
    {
        if (fixtures.Count == 0 || spread == 0) return 0;
        if (fixtureIndex < 0 || fixtureIndex >= fixtures.Count)
            throw new ArgumentOutOfRangeException(nameof(fixtureIndex));

        int segmentSize = Math.Max(1, segments ?? 1);
        int groupedIndex = fixtureIndex / segmentSize;
        int groupedCount = (fixtures.Count + segmentSize - 1) / segmentSize;
        int cycleSize = Math.Clamp(parts ?? groupedCount, 1, groupedCount);
        int position = groupedIndex % cycleSize;

        position = direction switch
        {
            EffectDirection.Forward => position,
            EffectDirection.Backward => cycleSize - 1 - position,
            EffectDirection.CenterIn => Math.Min(position, cycleSize - 1 - position),
            EffectDirection.CenterOut => CenterOutPosition(position, cycleSize),
            EffectDirection.Random => RandomPosition(fixtures, fixtureIndex, segmentSize, cycleSize, randomSeed),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };

        // Dividing by cycleSize (not cycleSize-1) matches MagicQ's documented 12-head
        // 100% spread table: 0%, 8%, 16%, ... 91%, with no duplicated 0/100 endpoint.
        return spread * position / cycleSize;
    }

    private static int CenterOutPosition(int position, int count)
    {
        int inwardRank = Math.Min(position, count - 1 - position);
        return Math.Max(0, (count - 1) / 2 - inwardRank);
    }

    private static int RandomPosition(
        IReadOnlyList<PatchedFixture> fixtures,
        int fixtureIndex,
        int segmentSize,
        int cycleSize,
        Guid seed)
    {
        var groupRepresentatives = Enumerable.Range(0, cycleSize)
            .Select(position => Math.Min(position * segmentSize, fixtures.Count - 1))
            .Select((index, position) => new
            {
                Position = position,
                Key = HashCode.Combine(seed, fixtures[index].Id),
            })
            .OrderBy(item => item.Key)
            .Select((item, randomPosition) => new { item.Position, RandomPosition = randomPosition })
            .ToDictionary(item => item.Position, item => item.RandomPosition);

        return groupRepresentatives[fixtureIndex / segmentSize % cycleSize];
    }
}

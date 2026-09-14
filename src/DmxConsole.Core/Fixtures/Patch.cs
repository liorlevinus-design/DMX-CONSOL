using System.Collections.ObjectModel;
using DmxConsole.Core.Engine;

namespace DmxConsole.Core.Fixtures;

/// <summary>
/// The full set of fixtures placed in the rig. Owns overlap validation - the one
/// invariant that must hold across the whole show (two fixtures cannot share
/// DMX addresses in the same universe).
/// </summary>
public sealed class Patch : IChannelTypeLookup
{
    private readonly List<PatchedFixture> _fixtures = new();

    public ObservableCollection<PatchedFixture> Fixtures { get; } = new();

    /// <summary>Adds a fixture to the patch, throwing if it overlaps an existing one.</summary>
    public void Add(PatchedFixture fixture)
    {
        var conflict = FindConflict(fixture);
        if (conflict is not null)
        {
            throw new InvalidOperationException(
                $"Cannot patch '{fixture.Name}' at Universe {fixture.UniverseId} / Address {fixture.StartAddress}: " +
                $"overlaps '{conflict.Name}' (Universe {conflict.UniverseId}, Address {conflict.StartAddress}-{conflict.StartAddress + conflict.Footprint - 1}).");
        }

        // Assign a stable operator-facing number unless the caller already set one
        // (and it isn't already taken by another patched fixture).
        if (fixture.Number <= 0 || _fixtures.Any(f => f.Number == fixture.Number))
            fixture.Number = NextFreeNumber();

        _fixtures.Add(fixture);
        Fixtures.Add(fixture);
    }

    private int NextFreeNumber() => _fixtures.Count == 0 ? 1 : _fixtures.Max(f => f.Number) + 1;

    public bool Remove(PatchedFixture fixture)
    {
        Fixtures.Remove(fixture);
        return _fixtures.Remove(fixture);
    }

    /// <summary>Returns the first already-patched fixture that would overlap this one, if any.</summary>
    public PatchedFixture? FindConflict(PatchedFixture candidate) =>
        _fixtures.FirstOrDefault(f => f.Id != candidate.Id && f.OverlapsWith(candidate));

    public IReadOnlyList<PatchedFixture> InUniverse(int universeId) =>
        _fixtures.Where(f => f.UniverseId == universeId).ToList();

    public IEnumerable<int> UsedUniverseIds => _fixtures.Select(f => f.UniverseId).Distinct().OrderBy(id => id);

    /// <summary>Looks up a patched fixture by its stable operator-facing number, or null if none matches.</summary>
    public PatchedFixture? FindByNumber(int number) => _fixtures.FirstOrDefault(f => f.Number == number);

    /// <summary>Stable, storage-independent channel identity: which ChannelType is patched at a
    /// given address, regardless of what any specific Cue/Preset/Programmer currently holds for
    /// it. This is what lets Executor classify a channel as Intensity without depending on a
    /// source's current stored/tracked data (see IChannelTypeLookup's own doc comment).</summary>
    public bool TryGetChannelType(int universeId, int channelIndex, out ChannelType channelType)
    {
        foreach (var fixture in InUniverse(universeId))
        {
            int offset = channelIndex - fixture.StartIndex;
            if (offset < 0 || offset >= fixture.Footprint) continue;

            var channel = fixture.Mode.Channels.FirstOrDefault(c => c.Offset == offset);
            if (channel is not null)
            {
                channelType = channel.Type;
                return true;
            }
        }

        channelType = default;
        return false;
    }
}

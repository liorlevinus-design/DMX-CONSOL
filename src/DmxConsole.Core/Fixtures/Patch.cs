using System.Collections.ObjectModel;

namespace DmxConsole.Core.Fixtures;

/// <summary>
/// The full set of fixtures placed in the rig. Owns overlap validation - the one
/// invariant that must hold across the whole show (two fixtures cannot share
/// DMX addresses in the same universe).
/// </summary>
public sealed class Patch
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

        _fixtures.Add(fixture);
        Fixtures.Add(fixture);
    }

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
}

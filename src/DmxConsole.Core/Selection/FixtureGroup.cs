using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Selection;

/// <summary>
/// A named, ordered set of fixture references (not a value snapshot) - e.g. "Back Wash".
/// Selecting a group just expands to its member fixtures; it carries no color/position
/// data of its own.
/// </summary>
public sealed class FixtureGroup
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }

    /// <summary>Operator-facing, stable, renumberable-without-losing-identity - same pattern as
    /// PatchedFixture.Number/Preset.Number/Executor.Number. What the command line's "Group N"
    /// syntax addresses; Id remains the real identity for Undo/references.</summary>
    public int Number { get; set; }

    public List<PatchedFixture> Fixtures { get; } = new();

    public FixtureGroup(string name, IEnumerable<PatchedFixture> fixtures, int number = 0)
    {
        Name = name;
        Number = number;
        Fixtures.AddRange(fixtures);
    }
}

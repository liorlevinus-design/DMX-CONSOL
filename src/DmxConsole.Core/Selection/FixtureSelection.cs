using System.Collections.ObjectModel;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Selection;

/// <summary>
/// The console's "current selection" - an ORDERED set of fixtures (order matters: it drives
/// Next/Previous and Odd/Even, which work off selection order, not patch order). This is a
/// pure query/UI-state layer over <see cref="Patch"/>; it never holds attribute values itself.
/// </summary>
public sealed class FixtureSelection
{
    public ObservableCollection<PatchedFixture> Items { get; } = new();

    public bool Contains(PatchedFixture fixture) => Items.Contains(fixture);

    public void Add(PatchedFixture fixture)
    {
        if (!Items.Contains(fixture)) Items.Add(fixture);
    }

    public void Remove(PatchedFixture fixture) => Items.Remove(fixture);

    public void Toggle(PatchedFixture fixture)
    {
        if (!Items.Remove(fixture)) Items.Add(fixture);
    }

    public void Clear() => Items.Clear();

    /// <summary>Adds every patched fixture whose Number falls within [from, to] (either order), ascending, to the selection.</summary>
    public void SelectRange(Patch patch, int from, int to)
    {
        int lo = Math.Min(from, to), hi = Math.Max(from, to);
        foreach (var fixture in patch.Fixtures.Where(f => f.Number >= lo && f.Number <= hi).OrderBy(f => f.Number))
            Add(fixture);
    }

    /// <summary>Keeps only the fixtures at odd 1-based positions in the current selection order (1st, 3rd, 5th...).</summary>
    public void FilterOdd() => FilterByIndex(i => i % 2 == 0);

    /// <summary>Keeps only the fixtures at even 1-based positions in the current selection order (2nd, 4th, 6th...).</summary>
    public void FilterEven() => FilterByIndex(i => i % 2 == 1);

    private void FilterByIndex(Func<int, bool> keepZeroBasedIndex)
    {
        var kept = Items.Where((_, i) => keepZeroBasedIndex(i)).ToList();
        Items.Clear();
        foreach (var fixture in kept) Items.Add(fixture);
    }

    public void AddGroup(FixtureGroup group)
    {
        foreach (var fixture in group.Fixtures) Add(fixture);
    }

    /// <summary>Replaces the selection with the single fixture after the current one (by Number), wrapping at the end.</summary>
    public void Next(Patch patch) => MoveCursor(patch, +1);

    /// <summary>Replaces the selection with the single fixture before the current one (by Number), wrapping at the start.</summary>
    public void Previous(Patch patch) => MoveCursor(patch, -1);

    private void MoveCursor(Patch patch, int direction)
    {
        var ordered = patch.Fixtures.OrderBy(f => f.Number).ToList();
        if (ordered.Count == 0) { Clear(); return; }

        var current = Items.LastOrDefault();
        int index = current is null ? -1 : ordered.IndexOf(current);
        int nextIndex = index < 0
            ? (direction > 0 ? 0 : ordered.Count - 1)
            : ((index + direction) % ordered.Count + ordered.Count) % ordered.Count;

        Clear();
        Add(ordered[nextIndex]);
    }
}

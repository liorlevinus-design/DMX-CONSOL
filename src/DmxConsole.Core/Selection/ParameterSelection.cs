using System.Collections.ObjectModel;

namespace DmxConsole.Core.Selection;

/// <summary>
/// The console's "current Parameter Selection" (CLAUDE.md §16, PSEL-1 through PSEL-5) - an
/// ORDERED set of LOGICAL parameters (PAN, TILT, RED, ...) the operator is currently targeting.
/// This is a structural sibling of <see cref="FixtureSelection"/> (same "ordered, explicit,
/// operator-driven" shape) but is NOT a copy of it: Parameter Selection has its own, narrower
/// semantics per PSEL-1/2/4/5 and deliberately does not carry FixtureSelection's range/odd/even/
/// reverse/next/previous operations, which are Fixture-Selection-specific gestures with no
/// Parameter Selection equivalent (yet, or possibly ever - not this slice's decision).
///
/// PSEL-1 (ordered, no duplicates): Items preserves insertion order; selecting an
/// already-present logical parameter again is a no-op, never a duplicate entry.
///
/// PSEL-2 (state, not filtered by compatibility): this class stores exactly what the operator
/// asked for. It never removes/filters an entry because some currently-selected fixture doesn't
/// support it - that compatibility resolution is a future operation-execution-time concern (AT/
/// HOME/RELEASE/TIME), not a property of this state.
///
/// PSEL-4 (independent lifecycle): this state is distinct from Fixture Selection, Programmer,
/// CommandComposer and Playback. Nothing here is derived from any of those - it changes only
/// through explicit Select/Remove/Clear calls made by an operator-facing surface.
///
/// PSEL-5 (logical parameters only): a coarse/fine pair (Pan+PanFine, Tilt+TiltFine) is ONE
/// logical parameter. Select normalizes any raw ChannelType to its logical representative via
/// the SAME <see cref="ChannelTypeExtensions.SemanticComponents"/> folding Slice 1 established for
/// the Encoder Drawer's read path - this class introduces no second folding map.
/// </summary>
public sealed class ParameterSelection
{
    public ObservableCollection<ChannelType> Items { get; } = new();

    public int Count => Items.Count;
    public bool IsEmpty => Items.Count == 0;

    /// <summary>The logical parameter a raw ChannelType folds into (PSEL-5) - e.g. PanFine -> Pan.
    /// Every mutation/query below normalizes through this single helper so no caller can bypass
    /// the folding by, say, calling Contains(PanFine) after Select(Pan) and expecting false.</summary>
    private static ChannelType Normalize(ChannelType type) => type.SemanticComponents()[0];

    public bool Contains(ChannelType type) => Items.Contains(Normalize(type));

    /// <summary>Adds the logical parameter for <paramref name="type"/> to the end of the ordered
    /// selection (PSEL-1) unless it is already present (PSEL-1 no-duplicates), normalizing
    /// coarse/fine first (PSEL-5).</summary>
    public void Select(ChannelType type)
    {
        var normalized = Normalize(type);
        if (!Items.Contains(normalized)) Items.Add(normalized);
    }

    public void Remove(ChannelType type) => Items.Remove(Normalize(type));

    public void Clear() => Items.Clear();
}

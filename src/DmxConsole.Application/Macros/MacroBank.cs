namespace DmxConsole.Application.Macros;

/// <summary>The show's Macro slots (docs/COMMAND_SURFACE_KEY_SPEC.md MACROS §1/§10) - fixed
/// physical slots 1-8 (MACRO 1-4 plus their Shift-mapped 5-8 siblings). Show data owned by the
/// Application layer, not browser/UI-only state - lives on ConsoleContext so every client sees
/// the same Macros. Persistence infrastructure for this object type does not exist yet (no
/// Workspace/show-file layer reaches Macros today) - this is clean in-memory Application
/// ownership, ready to be serialized once that layer exists, per this slice's own scope.</summary>
public sealed class MacroBank
{
    public const int MinSlot = 1;
    public const int MaxSlot = 8;

    private readonly Dictionary<int, Macro> _slots = new();

    public static bool IsValidSlot(int slot) => slot is >= MinSlot and <= MaxSlot;

    public Macro? FindBySlot(int slot) => _slots.TryGetValue(slot, out var macro) ? macro : null;

    public void Set(int slot, Macro macro) => _slots[slot] = macro;

    public void Remove(int slot) => _slots.Remove(slot);

    public IReadOnlyCollection<Macro> All => _slots.Values;
}

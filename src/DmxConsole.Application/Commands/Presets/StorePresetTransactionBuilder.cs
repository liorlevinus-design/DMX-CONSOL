using DmxConsole.Application.Commands;
using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;

namespace DmxConsole.Application.Commands.Presets;

/// <summary>
/// The ONE place a multi-family Preset Store transaction is built and checked for number
/// conflicts - shared by CommandComposer (the deterministic/unambiguous grammar paths: an
/// explicit family, or exactly one touched family) and CommandSurfaceViewModel (after the
/// operator confirms a family choice and/or an OVERWRITE/UPDATE/CANCEL conflict decision through
/// the STORE-family panel), so the two never grow a second, divergent notion of "what a
/// multi-family Store actually does". Building the transaction NEVER mutates anything itself -
/// it only constructs commands; a conflict is discovered (via <see cref="FindConflicts"/>)
/// BEFORE any command is built, so the whole transaction can be resolved atomically in one
/// CompositeCommand once every conflict has an operator decision.
/// </summary>
public static class StorePresetTransactionBuilder
{
    /// <summary>Which of the given families already have a Preset at this number - the operator
    /// must be asked OVERWRITE/UPDATE/CANCEL for these before anything is dispatched.</summary>
    public static IReadOnlyList<AttributeClass> FindConflicts(PresetLibrary library, IReadOnlyList<AttributeClass> families, int number) =>
        families.Where(family => library.FindByNumber(family, number) is not null).ToList();

    /// <summary>Builds one atomic operation storing every family in <paramref name="families"/> at
    /// <paramref name="number"/> - a brand-new Preset per family that doesn't yet exist there, or
    /// (for a family in <paramref name="conflictResolution"/>'s Overwrite/Update set) a merge or
    /// full-replace of the existing one, per the operator's single, shared choice for this whole
    /// transaction. precedingCommands (the selection-building commands from "FIXTURE 1 THRU 10
    /// ODD STORE ...") run first, in the same one Undo transaction.</summary>
    public static IConsoleCommand BuildTransaction(PresetLibrary library, IReadOnlyList<AttributeClass> families, int number,
        IReadOnlyList<PatchedFixture> targets, IReadOnlyList<IConsoleCommand> precedingCommands, bool overwrite)
    {
        var commands = new List<IConsoleCommand>(precedingCommands);
        foreach (var family in families)
        {
            var existing = library.FindByNumber(family, number);
            string name = existing?.Name ?? $"{family} {number}";
            commands.Add(new StorePresetCommand(library, targets, family, name, number, existing, overwrite));
        }
        return commands.Count == 1 ? commands[0] : new CompositeCommand(commands);
    }
}

using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.CommandSurface;

/// <summary>
/// The CommandComposer's structured view of its current partial (or just-finalized) command -
/// what CommandLine renders and what a soft-key surface uses to highlight valid next tokens.
/// PreviewText exists for display only; it is never re-parsed by anything - the composer's own
/// internal token/clause state is the source of truth, same "structured data first, prose
/// second" rule as CommandResult.Message elsewhere in this project.
/// </summary>
public sealed record CommandComposition
{
    public IReadOnlyList<CommandToken> Tokens { get; init; } = Array.Empty<CommandToken>();

    /// <summary>Operator-readable rendering of the composition so far, e.g. "FIXTURE 1 THRU 5 AT 70".</summary>
    public string PreviewText { get; init; } = string.Empty;

    /// <summary>True only when Enter was just pressed and the command resolved cleanly -
    /// <see cref="ReadyOperation"/> is populated in that case and only that case.</summary>
    public bool IsComplete { get; init; }

    /// <summary>True when the token sequence has more than one valid interpretation and the
    /// composer refuses to guess - see the individual token-kind doc comments for which
    /// situations trigger this today (deliberately narrow in this first milestone).</summary>
    public bool IsAmbiguous { get; init; }

    /// <summary>Set whenever the composition cannot proceed as entered - a resolution failure
    /// (e.g. "Fixture 99 not found") on Enter, or a structurally invalid next token. Never
    /// clears the token buffer - the operator corrects via Backspace, per "preserve the entered
    /// command, allow correction" rather than silently discarding it.</summary>
    public string? Error { get; init; }

    /// <summary>Which token kinds would be structurally valid next - what a context-sensitive
    /// CommandSurface uses to enable/highlight soft keys.</summary>
    public IReadOnlyList<CommandTokenKind> ExpectedNext { get; init; } = Array.Empty<CommandTokenKind>();

    /// <summary>True only for a Clear pressed while the command line was already empty - the
    /// composer itself never touches ConsoleContext/Selection, so this is a signal for whoever
    /// owns the CommandDispatcher to apply the console's CLEAR selection-cycle semantics.</summary>
    public bool EmptyClearRequested { get; init; }

    /// <summary>
    /// True when this completed command did more than select objects and therefore closes the
    /// current selection cycle. In the first grammar slice this means an AT value was executed.
    /// The next new Fixture/Group selection should therefore start fresh while the just-used
    /// selection remains visibly selected until that next selection actually happens.
    /// </summary>
    public bool EndsSelectionCycle { get; init; }
    public int? ResolvedGroupNumber { get; init; }
    public double? AppliedAtPercent { get; init; }

    /// <summary>Populated only when IsComplete is true - the actual structured operation, ready
    /// to hand to CommandDispatcher.Dispatch. The composer builds this but never dispatches it
    /// itself - see CommandComposer's own doc comment for why that boundary matters.</summary>
    public IConsoleCommand? ReadyOperation { get; init; }

    /// <summary>Like <see cref="ReadyOperation"/>, but for a structural/operational IConsoleAction
    /// (e.g. Quick Patch - Patch/Undo-architecture slice) that must be dispatched via
    /// CommandDispatcher.DispatchAction instead of Dispatch, so it never enters the normal
    /// programming Undo stack. Mutually exclusive with ReadyOperation - a composition populates
    /// at most one of the two.</summary>
    public IConsoleAction? ReadyAction { get; init; }

    /// <summary>Set when a completed STORE command needs the operator to disambiguate which
    /// PROGRAMMER family/families a bare "STORE PRESET n" should be stored as (more than one
    /// touched family, none specified in the grammar) - the CommandSurface arms a contextual
    /// family-choice panel (mirroring the RELEASE panel) instead of guessing. Never populated
    /// alongside ReadyOperation/ReadyAction - this composition is NOT complete; IsComplete stays
    /// false until the operator confirms a choice through the panel.</summary>
    public PendingStoreChoice? PendingStoreChoice { get; init; }

    /// <summary>Set when a STORE PRESET transaction (whether the family was explicit, singly
    /// touched, or just confirmed through the family-choice panel) found that one or more target
    /// families already have a Preset at the requested number - the CommandSurface must ask
    /// OVERWRITE/UPDATE/CANCEL (never guess) before anything is dispatched. Like
    /// PendingStoreChoice, this composition is NOT complete.</summary>
    public PendingStoreConflict? PendingStoreConflict { get; init; }
}

/// <summary>What CommandSurfaceViewModel needs to render/confirm the STORE-family-choice panel and
/// finish building the actual Preset-store transaction once the operator picks. Deliberately plain
/// data - CommandComposer builds it but (like everything else it builds) never dispatches anything
/// itself.</summary>
public sealed record PendingStoreChoice(
    int? Number,
    IReadOnlyList<AttributeClass> TouchedFamilies,
    IReadOnlyList<PatchedFixture> Targets,
    IReadOnlyList<IConsoleCommand> PrecedingCommands);

/// <summary>What CommandSurfaceViewModel needs to render/confirm the OVERWRITE/UPDATE/CANCEL
/// panel and finish the Preset-store transaction. One shared decision applies to every family in
/// <see cref="ConflictingFamilies"/> at once (not resolved per-Preset) - the whole transaction
/// (every family in <see cref="Families"/>, conflicting or not, plus PrecedingCommands) is then
/// built and dispatched as one atomic CompositeCommand via StorePresetTransactionBuilder.</summary>
public sealed record PendingStoreConflict(
    int Number,
    IReadOnlyList<AttributeClass> Families,
    IReadOnlyList<AttributeClass> ConflictingFamilies,
    IReadOnlyList<PatchedFixture> Targets,
    IReadOnlyList<IConsoleCommand> PrecedingCommands);

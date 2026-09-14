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
    /// owns the CommandDispatcher to clear the Selection as a separate, explicit dispatch.</summary>
    public bool EmptyClearRequested { get; init; }

    /// <summary>Populated only when IsComplete is true - the actual structured operation, ready
    /// to hand to CommandDispatcher.Dispatch. The composer builds this but never dispatches it
    /// itself - see CommandComposer's own doc comment for why that boundary matters.</summary>
    public IConsoleCommand? ReadyOperation { get; init; }
}

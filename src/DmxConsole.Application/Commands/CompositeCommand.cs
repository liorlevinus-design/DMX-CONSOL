using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands;

/// <summary>
/// Wraps several commands as one atomic transaction - this is what lets "lower Cold Wash
/// and raise the Fronts" (two mutations from one utterance) collapse into a single Undo.
///
/// Atomicity: if every sub-command succeeds, the whole batch succeeds and becomes one
/// Undo entry. If any sub-command fails, everything that already succeeded in this batch
/// is rolled back (Undo, in reverse order) before Execute returns - nothing partial is
/// ever left applied, and CommandDispatcher never pushes a failed batch onto the undo
/// stack. Redo re-executes every sub-command in order, exactly like a fresh Dispatch.
/// </summary>
public sealed class CompositeCommand : IConsoleCommand, IReplayableCommand, IHasUndoRisk
{
    private readonly IReadOnlyList<IConsoleCommand> _commands;

    /// <summary>Read-only view of the wrapped commands - used by MacroRecorder to recursively
    /// decide whether a whole batch is safely macro-recordable (every child must itself be
    /// IReplayableCommand, or a further nested CompositeCommand whose own children all are).</summary>
    public IReadOnlyList<IConsoleCommand> Commands => _commands;

    public CompositeCommand(IReadOnlyList<IConsoleCommand> commands) => _commands = commands;

    /// <summary>Only ever called after a caller (MacroRecorder) has already verified every child
    /// implements IReplayableCommand - see that type's own recursive check. Builds a brand-new
    /// CompositeCommand wrapping a fresh instance of every child, never reusing this instance's
    /// own already-executed children.</summary>
    public IConsoleCommand CreateFreshInstance() =>
        new CompositeCommand(_commands.Select(c => ((IReplayableCommand)c).CreateFreshInstance()).ToList());

    public CommandResult Execute(ConsoleContext context)
    {
        var childResults = new List<CommandResult>();
        var executedSoFar = new List<IConsoleCommand>();

        foreach (var command in _commands)
        {
            var result = command.Execute(context);
            childResults.Add(result);

            if (!result.Success)
            {
                // Roll back everything that already succeeded in this batch, in reverse order,
                // so a failure never leaves a partial change applied.
                for (int i = executedSoFar.Count - 1; i >= 0; i--) executedSoFar[i].Undo(context);

                return new CommandResult
                {
                    ActionType = ConsoleActionType.Batch,
                    Success = false,
                    Error = result.Error ?? "A command in the batch failed.",
                    ChildResults = childResults,
                    AffectedFixtures = Array.Empty<PatchedFixture>(), // rolled back - nothing remains affected
                };
            }

            executedSoFar.Add(command);
        }

        return new CommandResult
        {
            ActionType = ConsoleActionType.Batch,
            Success = true,
            ChildResults = childResults,
            AffectedFixtures = childResults.SelectMany(r => r.AffectedFixtures).Distinct().ToList(),
            Warning = CombineWarnings(childResults),
        };
    }

    public void Undo(ConsoleContext context)
    {
        for (int i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo(context);
    }

    /// <summary>
    /// Aggregates Undo risk from every child that implements <see cref="IHasUndoRisk"/> - without
    /// this, UndoRedoService.PeekUndo's fallback (<see cref="UndoProposal.SingleSafe"/> for any
    /// command that isn't itself IHasUndoRisk) would silently downgrade a composite containing a
    /// genuinely Destructive child (e.g. [StoreCueCommand, ClearProgrammerCommand] - StoreCueCommand
    /// alone always proposes Destructive) to Safe, violating CLAUDE.md §14's "Destructive Undo
    /// requires an explicit confirmed option id from PeekUndo". If ANY child proposes a Destructive
    /// option, the whole composite does too - reusing the "delete" id every Destructive
    /// UndoOption in this codebase already uses, so a confirmed id from an earlier PeekUndo() still
    /// matches when Undo() re-derives this same proposal. No child today ever proposes more than
    /// one Destructive option, so combining every child's description is enough - never silent.
    /// </summary>
    public UndoProposal PrepareUndo()
    {
        var destructiveDescriptions = _commands
            .OfType<IHasUndoRisk>()
            .Select(c => c.PrepareUndo())
            .SelectMany(p => p.Options)
            .Where(o => o.Risk == UndoRisk.Destructive)
            .Select(o => o.Description)
            .ToList();

        if (destructiveDescriptions.Count == 0) return UndoProposal.SingleSafe("Undo this action");

        var description = string.Join(" and ", destructiveDescriptions);
        return new UndoProposal(description, new[] { new UndoOption("delete", description, UndoRisk.Destructive) });
    }

    private static string? CombineWarnings(IReadOnlyList<CommandResult> results)
    {
        var warnings = results.Where(r => r.Warning is not null).Select(r => r.Warning!).ToList();
        return warnings.Count == 0 ? null : string.Join(" | ", warnings);
    }
}

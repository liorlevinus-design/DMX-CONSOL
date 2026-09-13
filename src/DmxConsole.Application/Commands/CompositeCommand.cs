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
public sealed class CompositeCommand : IConsoleCommand
{
    private readonly IReadOnlyList<IConsoleCommand> _commands;

    public CompositeCommand(IReadOnlyList<IConsoleCommand> commands) => _commands = commands;

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

    private static string? CombineWarnings(IReadOnlyList<CommandResult> results)
    {
        var warnings = results.Where(r => r.Warning is not null).Select(r => r.Warning!).ToList();
        return warnings.Count == 0 ? null : string.Join(" | ", warnings);
    }
}

namespace DmxConsole.Application.Commands.Selection;

/// <summary>
/// Base for every command that only mutates the current FixtureSelection. Undo is
/// implemented once, correctly, for all of them: capture a full snapshot of the selection
/// before applying the change, and restore it verbatim on Undo. This is simpler and safer
/// than hand-writing an inverse for Toggle/Range/Odd/Even/Next/Previous individually.
/// </summary>
public abstract class SelectionCommandBase : IConsoleCommand, IReplayableCommand
{
    private List<Core.Fixtures.PatchedFixture>? _previousSelection;

    protected abstract ConsoleActionType ActionType { get; }

    /// <summary>Selection History rule: true (the default) for every selection-mutating command -
    /// CommandDispatcher reads this after a successful Execute to decide whether the resulting
    /// Selection becomes the new SelectionCycle.LastSelection ("Last Selection is always the most
    /// recent ordered Selection state produced by any selection operation or transformation... it
    /// does not matter where that Selection came from"). This is the ONE place that rule is
    /// implemented - every Toggle/Range/Odd/Even/Reverse/Next/Previous/AddGroup/Replace command
    /// gets it for free, including from callers that don't remember to ask for it explicitly
    /// (the exact bug class this flag exists to close). <see cref="ClearSelectionCommand"/> is the
    /// sole, deliberate exception - see its own override.</summary>
    protected internal virtual bool ProducesSelectionSnapshot => true;

    /// <summary>Performs the actual selection mutation - runs after the previous state has been captured.</summary>
    protected abstract void Apply(ConsoleContext context);

    /// <summary>Builds a brand-new instance with this command's own construction-time
    /// parameters, never sharing this instance's own _previousSelection snapshot (docs/COMMAND_SURFACE_KEY_SPEC.md
    /// MACROS "REPLAY INSTANCE SAFETY").</summary>
    public abstract IConsoleCommand CreateFreshInstance();

    public CommandResult Execute(ConsoleContext context)
    {
        _previousSelection = context.Selection.Items.ToList();
        Apply(context);

        return new CommandResult
        {
            ActionType = ActionType,
            AffectedFixtures = context.Selection.Items.ToList(),
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_previousSelection is null) return; // Undo without a prior Execute - nothing to do

        context.Selection.Clear();
        foreach (var fixture in _previousSelection) context.Selection.Add(fixture);
    }
}

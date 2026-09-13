namespace DmxConsole.Application.Commands.Selection;

/// <summary>
/// Base for every command that only mutates the current FixtureSelection. Undo is
/// implemented once, correctly, for all of them: capture a full snapshot of the selection
/// before applying the change, and restore it verbatim on Undo. This is simpler and safer
/// than hand-writing an inverse for Toggle/Range/Odd/Even/Next/Previous individually.
/// </summary>
public abstract class SelectionCommandBase : IConsoleCommand
{
    private List<Core.Fixtures.PatchedFixture>? _previousSelection;

    protected abstract ConsoleActionType ActionType { get; }

    /// <summary>Performs the actual selection mutation - runs after the previous state has been captured.</summary>
    protected abstract void Apply(ConsoleContext context);

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

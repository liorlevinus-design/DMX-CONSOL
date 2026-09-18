using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Selection;

/// <summary>
/// Replaces the current ordered selection with an explicit snapshot. Used by semantic CLEAR to
/// restore the state before the last selection gesture while remaining fully undoable through
/// the existing SelectionCommandBase snapshot mechanism.
/// </summary>
public sealed class ReplaceSelectionCommand : SelectionCommandBase
{
    private readonly IReadOnlyList<PatchedFixture> _target;

    public ReplaceSelectionCommand(IEnumerable<PatchedFixture> target) => _target = target.ToList();

    protected override ConsoleActionType ActionType => ConsoleActionType.ClearSelection;

    protected override void Apply(ConsoleContext context)
    {
        context.Selection.Clear();
        foreach (var fixture in _target) context.Selection.Add(fixture);
    }

    public override IConsoleCommand CreateFreshInstance() => new ReplaceSelectionCommand(_target);
}

namespace DmxConsole.Application.Commands.Selection;

/// <summary>Adds every fixture whose Number falls within [from, to] (either order) to the current selection.</summary>
public sealed class SelectRangeCommand : SelectionCommandBase
{
    private readonly int _from;
    private readonly int _to;

    public SelectRangeCommand(int from, int to)
    {
        _from = from;
        _to = to;
    }

    protected override ConsoleActionType ActionType => ConsoleActionType.SelectRange;

    protected override void Apply(ConsoleContext context) => context.Selection.SelectRange(context.Patch, _from, _to);

    public override IConsoleCommand CreateFreshInstance() => new SelectRangeCommand(_from, _to);
}

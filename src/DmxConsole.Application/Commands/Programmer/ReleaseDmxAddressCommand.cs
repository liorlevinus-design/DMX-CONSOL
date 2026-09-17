namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// "DMX &lt;Universe&gt;.&lt;Address&gt; RELEASE" (and its THRU range form) - releases the
/// Programmer's stored value (and any knockout) at the given raw addresses. Same "no-op if
/// nothing was actually stored" rule as ReleaseCommand/ReleaseParameterCommand - a no-op is
/// never counted as an affected address.
/// </summary>
public sealed class ReleaseDmxAddressCommand : DmxAddressCommandBase
{
    public ReleaseDmxAddressCommand(IReadOnlyList<(int Universe, int Channel)> addresses)
        : base(addresses)
    {
    }

    protected override ConsoleActionType ActionType => ConsoleActionType.ReleaseDmxAddress;

    protected override bool Apply(ConsoleContext context, int universe, int channel)
    {
        bool hasSomething = context.Programmer.HasStoredValue(universe, channel, out _) || context.Programmer.IsKnockedOut(universe, channel);
        if (!hasSomething) return false;

        context.Programmer.ClearChannel(universe, channel);
        return true;
    }
}

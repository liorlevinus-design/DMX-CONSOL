namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// "DMX &lt;Universe&gt;.&lt;Address&gt; AT &lt;value|FULL&gt;" - writes one byte (already
/// converted from the operator's semantic percent at the CommandComposer boundary, per this
/// slice's spec §3 - the operator never types a raw 0-255 byte) to every given raw address via
/// the Programmer, unconditionally (unlike Release, "set" always counts as a real change).
/// </summary>
public sealed class SetDmxAddressCommand : DmxAddressCommandBase
{
    private readonly byte _value;

    public SetDmxAddressCommand(IReadOnlyList<(int Universe, int Channel)> addresses, byte value)
        : base(addresses)
    {
        _value = value;
    }

    protected override ConsoleActionType ActionType => ConsoleActionType.SetDmxAddress;

    protected override bool Apply(ConsoleContext context, int universe, int channel)
    {
        context.Programmer.SetChannel(universe, channel, _value);
        return true;
    }
}

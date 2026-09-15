using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>Undoable absolute value change from an encoder for one semantic channel type.</summary>
public sealed class SetAttributeValueCommand : ProgrammerChannelCommandBase
{
    private readonly ChannelType _channelType;
    private readonly byte _value;

    public SetAttributeValueCommand(IReadOnlyList<PatchedFixture> targets, ChannelType channelType, byte value)
        : base(targets, channelType.ToAttributeClass())
    {
        _channelType = channelType;
        _value = value;
    }

    protected override ConsoleActionType ActionType => ConsoleActionType.SetAttributeValue;

    protected override bool ApplyToChannel(ConsoleContext context, PatchedFixture fixture, FixtureChannel channel)
    {
        if (channel.Type != _channelType) return false;
        context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(channel), _value);
        return true;
    }
}

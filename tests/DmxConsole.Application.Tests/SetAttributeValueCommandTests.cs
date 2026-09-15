using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Core;

namespace DmxConsole.Application.Tests;

public class SetAttributeValueCommandTests
{
    [Fact]
    public void SetsOnlyRequestedChannelType_AndUndoRestoresProgrammer()
    {
        var (context, dispatcher, undo) = TestFixtures.BuildConsole(fixtureCount: 1);
        var fixture = Assert.Single(context.Patch.Fixtures);
        var dimmer = fixture.FindChannel(ChannelType.Dimmer)!;

        var result = dispatcher.Dispatch(new SetAttributeValueCommand(new[] { fixture }, ChannelType.Dimmer, 123));

        Assert.True(result.Success);
        Assert.True(context.Programmer.TryGetChannelValue(fixture.UniverseId, fixture.AbsoluteIndex(dimmer), out var value));
        Assert.Equal(123, value);
        Assert.True(undo.Undo().Performed);
        Assert.False(context.Programmer.HasStoredValue(fixture.UniverseId, fixture.AbsoluteIndex(dimmer), out _));
    }
}

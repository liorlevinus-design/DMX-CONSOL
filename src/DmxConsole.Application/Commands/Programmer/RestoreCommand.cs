using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using CoreProgrammer = DmxConsole.Core.Engine.Programmer;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>Un-suppresses target fixtures previously knocked out with <see cref="KnockoutCommand"/>.</summary>
public sealed class RestoreCommand : ProgrammerChannelCommandBase
{
    public RestoreCommand(IReadOnlyList<PatchedFixture> targets, AttributeClass? attribute = null)
        : base(targets, attribute)
    {
    }

    protected override ConsoleActionType ActionType => ConsoleActionType.Restore;

    protected override bool ApplyToChannel(CoreProgrammer programmer, PatchedFixture fixture, FixtureChannel channel)
    {
        int idx = fixture.AbsoluteIndex(channel);
        if (!programmer.IsKnockedOut(fixture.UniverseId, idx)) return false;

        programmer.Restore(fixture.UniverseId, idx);
        return true;
    }
}

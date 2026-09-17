using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>Un-suppresses target fixtures previously knocked out with <see cref="KnockoutCommand"/>.</summary>
public sealed class RestoreCommand : ProgrammerChannelCommandBase
{
    public RestoreCommand(IReadOnlyList<PatchedFixture> targets, AttributeClass? attribute = null)
        : base(targets, attribute)
    {
    }

    protected override ConsoleActionType ActionType => ConsoleActionType.Restore;

    protected override bool ApplyToChannel(ConsoleContext context, PatchedFixture fixture, FixtureChannel channel)
    {
        int idx = fixture.AbsoluteIndex(channel);
        if (!context.Programmer.IsKnockedOut(fixture.UniverseId, idx)) return false;

        context.Programmer.Restore(fixture.UniverseId, idx);
        return true;
    }

    public override IConsoleCommand CreateFreshInstance() => new RestoreCommand(Targets, AttributeFilter);
}

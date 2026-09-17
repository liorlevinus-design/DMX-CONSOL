using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// Releases Programmer values for the target fixtures. With no attribute filter this is a
/// full "Release" (every channel); with one, it's a "Clear &lt;Attribute&gt;" - the null-vs-set
/// filter is the only difference, so one class covers both rather than duplicating a nearly
/// identical command.
/// </summary>
public sealed class ReleaseCommand : ProgrammerChannelCommandBase
{
    public ReleaseCommand(IReadOnlyList<PatchedFixture> targets, AttributeClass? attribute = null)
        : base(targets, attribute)
    {
    }

    protected override ConsoleActionType ActionType =>
        AttributeFilter is null ? ConsoleActionType.Release : ConsoleActionType.ClearAttribute;

    protected override bool ApplyToChannel(ConsoleContext context, PatchedFixture fixture, FixtureChannel channel)
    {
        int idx = fixture.AbsoluteIndex(channel);
        bool hasSomething = context.Programmer.HasStoredValue(fixture.UniverseId, idx, out _) || context.Programmer.IsKnockedOut(fixture.UniverseId, idx);
        if (!hasSomething) return false;

        context.Programmer.ClearChannel(fixture.UniverseId, idx);
        return true;
    }

    public override IConsoleCommand CreateFreshInstance() => new ReleaseCommand(Targets, AttributeFilter);
}

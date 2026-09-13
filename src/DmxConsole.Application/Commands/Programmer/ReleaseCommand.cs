using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using CoreProgrammer = DmxConsole.Core.Engine.Programmer;

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

    protected override bool ApplyToChannel(CoreProgrammer programmer, PatchedFixture fixture, FixtureChannel channel)
    {
        int idx = fixture.AbsoluteIndex(channel);
        bool hasSomething = programmer.HasStoredValue(fixture.UniverseId, idx, out _) || programmer.IsKnockedOut(fixture.UniverseId, idx);
        if (!hasSomething) return false;

        programmer.ClearChannel(fixture.UniverseId, idx);
        return true;
    }
}

using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using CoreProgrammer = DmxConsole.Core.Engine.Programmer;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// Temporarily suppresses the target fixtures' Programmer output without discarding the
/// stored values - lets the operator "peek" at what a cue/effect would show without a
/// fixture's live override, then bring it back exactly with <see cref="RestoreCommand"/>.
/// </summary>
public sealed class KnockoutCommand : ProgrammerChannelCommandBase
{
    public KnockoutCommand(IReadOnlyList<PatchedFixture> targets, AttributeClass? attribute = null)
        : base(targets, attribute)
    {
    }

    protected override ConsoleActionType ActionType => ConsoleActionType.Knockout;

    protected override bool ApplyToChannel(CoreProgrammer programmer, PatchedFixture fixture, FixtureChannel channel)
    {
        int idx = fixture.AbsoluteIndex(channel);

        // Only meaningful for a channel that actually has a live value to suppress, and
        // not already knocked out - otherwise this is a no-op, not an "affected" fixture.
        if (!programmer.HasStoredValue(fixture.UniverseId, idx, out _)) return false;
        if (programmer.IsKnockedOut(fixture.UniverseId, idx)) return false;

        programmer.Knockout(fixture.UniverseId, idx);
        return true;
    }
}

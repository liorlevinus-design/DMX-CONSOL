using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// Parameter-scoped TIME (CLAUDE.md §16, ROADMAP §9a): writes an already-resolved list of
/// (fixture, channel, TimeIn?, TimeOut?) overrides into the Programmer's parallel per-channel
/// timing store as ONE atomic, undoable transaction - regardless of whether it came from a fixed
/// value, a distributed value fan, or several logical parameters at once. One generic command
/// class reused for every logical parameter, mirroring <see cref="SetParameterValuesCommand"/>
/// exactly, but on the independent TIMING axis: this command never reads or writes a Programmer
/// VALUE, only <see cref="DmxConsole.Core.Engine.Programmer.SetTimeIn"/>/<c>SetTimeOut</c>.
///
/// Deliberately NOT built on <see cref="ProgrammerChannelCommandBase"/>: that base's snapshot/undo
/// machinery is VALUE-and-knockout shaped (HasStoredValue/SetChannel/ClearChannel) and its Undo
/// calls ClearChannel whenever a channel had no prior value - which would incorrectly wipe an
/// unrelated, independently-set timing override on the same channel if reused here. Timing gets
/// its own small, parallel snapshot/undo, capturing exactly the previous (TimeIn, TimeOut) pair per
/// target and restoring it verbatim via <see cref="DmxConsole.Core.Engine.Programmer.SetTimingRaw"/>.
/// </summary>
public sealed class SetParameterTimingCommand : IConsoleCommand, IReplayableCommand
{
    /// <summary>One resolved write: a specific fixture's specific channel gets an optional TimeIn
    /// and/or TimeOut override. A null side means "leave that side untouched" (e.g. "TIME IN 5"
    /// only ever populates TimeIn) - it is never interpreted as "clear that side".</summary>
    public readonly record struct ParameterTimingTarget(PatchedFixture Fixture, FixtureChannel Channel, TimeSpan? TimeIn, TimeSpan? TimeOut);

    private readonly record struct TimingSnapshot(int Universe, int Channel, TimeSpan? PreviousTimeIn, TimeSpan? PreviousTimeOut);

    private readonly IReadOnlyList<ParameterTimingTarget> _targets;
    private readonly IReadOnlyList<ParameterTargetResolver.SkippedTarget> _skipped;
    private List<TimingSnapshot>? _previous;

    public SetParameterTimingCommand(
        IReadOnlyList<ParameterTimingTarget> targets,
        IReadOnlyList<ParameterTargetResolver.SkippedTarget>? skipped = null)
    {
        _targets = targets;
        _skipped = skipped ?? Array.Empty<ParameterTargetResolver.SkippedTarget>();
    }

    public CommandResult Execute(ConsoleContext context)
    {
        var snapshots = new List<TimingSnapshot>();
        var affected = new List<PatchedFixture>();

        foreach (var target in _targets)
        {
            int idx = target.Fixture.AbsoluteIndex(target.Channel);
            context.Programmer.TryGetTiming(target.Fixture.UniverseId, idx, out var previousIn, out var previousOut);
            snapshots.Add(new TimingSnapshot(target.Fixture.UniverseId, idx, previousIn, previousOut));

            if (target.TimeIn is { } timeIn) context.Programmer.SetTimeIn(target.Fixture.UniverseId, idx, timeIn);
            if (target.TimeOut is { } timeOut) context.Programmer.SetTimeOut(target.Fixture.UniverseId, idx, timeOut);

            if (!affected.Contains(target.Fixture)) affected.Add(target.Fixture);
        }

        _previous = snapshots;

        var attributes = _targets.Select(t => t.Channel.Type.ToAttributeClass()).Distinct().ToList();
        var result = new CommandResult
        {
            ActionType = ConsoleActionType.SetParameterTiming,
            AffectedFixtures = affected,
            AffectedAttributes = attributes,
        };

        if (_skipped.Count == 0) return result;

        return result with
        {
            Warning = $"{affected.Count} fixture(s) affected, {_skipped.Count} (fixture, parameter) pair(s) skipped - parameter not supported.",
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_previous is null) return;

        foreach (var snap in _previous)
            context.Programmer.SetTimingRaw(snap.Universe, snap.Channel, snap.PreviousTimeIn, snap.PreviousTimeOut);
    }

    public IConsoleCommand CreateFreshInstance() => new SetParameterTimingCommand(_targets, _skipped);
}

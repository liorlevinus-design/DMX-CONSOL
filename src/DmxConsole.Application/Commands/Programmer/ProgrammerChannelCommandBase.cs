using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using CoreProgrammer = DmxConsole.Core.Engine.Programmer;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// Base for every command that mutates the Programmer for a set of target fixtures,
/// optionally scoped to one attribute class (null = every channel of the fixture). Undo is
/// implemented once, correctly, for all of them: capture each touched channel's stored
/// value and knockout state before applying the change, and restore both verbatim on Undo -
/// the same "snapshot then restore" approach <c>SelectionCommandBase</c> uses for selection.
/// </summary>
public abstract class ProgrammerChannelCommandBase : IConsoleCommand
{
    private readonly record struct ChannelSnapshot(int Universe, int Channel, bool HadValue, byte Value, bool WasKnockedOut);

    private readonly IReadOnlyList<PatchedFixture> _targets;
    private List<ChannelSnapshot>? _previous;

    /// <summary>Null means "every channel of the fixture"; otherwise only channels in this attribute class.</summary>
    protected AttributeClass? AttributeFilter { get; }

    protected abstract ConsoleActionType ActionType { get; }

    protected ProgrammerChannelCommandBase(IReadOnlyList<PatchedFixture> targets, AttributeClass? attributeFilter)
    {
        _targets = targets;
        AttributeFilter = attributeFilter;
    }

    /// <summary>
    /// Applies this command's effect to one fixture channel. Return true only if something
    /// actually changed - that's what marks the fixture as affected and captures undo state
    /// for this channel, so a no-op (e.g. knocking out an already-knocked-out channel) can
    /// cleanly report "nothing happened" by returning false.
    /// </summary>
    protected abstract bool ApplyToChannel(CoreProgrammer programmer, PatchedFixture fixture, FixtureChannel channel);

    public CommandResult Execute(ConsoleContext context)
    {
        var snapshots = new List<ChannelSnapshot>();
        var affected = new List<PatchedFixture>();
        var previousValues = new Dictionary<(Guid, ChannelType), byte>();
        var newValues = new Dictionary<(Guid, ChannelType), byte>();

        foreach (var fixture in _targets)
        {
            var channels = AttributeFilter is { } cls ? fixture.ChannelsForAttribute(cls) : fixture.Mode.Channels;
            bool touchedAny = false;

            foreach (var channel in channels)
            {
                int idx = fixture.AbsoluteIndex(channel);
                bool hadValue = context.Programmer.HasStoredValue(fixture.UniverseId, idx, out var beforeValue);
                bool wasKnockedOut = context.Programmer.IsKnockedOut(fixture.UniverseId, idx);

                if (!ApplyToChannel(context.Programmer, fixture, channel)) continue;

                snapshots.Add(new ChannelSnapshot(fixture.UniverseId, idx, hadValue, hadValue ? beforeValue : (byte)0, wasKnockedOut));
                touchedAny = true;

                if (hadValue) previousValues[(fixture.Id, channel.Type)] = beforeValue;
                if (context.Programmer.HasStoredValue(fixture.UniverseId, idx, out var afterValue))
                    newValues[(fixture.Id, channel.Type)] = afterValue;
            }

            if (touchedAny) affected.Add(fixture);
        }

        _previous = snapshots;

        return new CommandResult
        {
            ActionType = ActionType,
            AffectedFixtures = affected,
            AffectedAttributes = AttributeFilter is { } filterClass ? new[] { filterClass } : Array.Empty<AttributeClass>(),
            PreviousValues = previousValues,
            NewValues = newValues,
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_previous is null) return;

        foreach (var snap in _previous)
        {
            if (snap.HadValue) context.Programmer.SetChannel(snap.Universe, snap.Channel, snap.Value);
            else context.Programmer.ClearChannel(snap.Universe, snap.Channel);

            if (snap.WasKnockedOut) context.Programmer.Knockout(snap.Universe, snap.Channel);
            else context.Programmer.Restore(snap.Universe, snap.Channel);
        }
    }
}

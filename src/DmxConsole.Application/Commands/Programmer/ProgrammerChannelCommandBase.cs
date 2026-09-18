using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// Base for every command that mutates the Programmer for a set of target fixtures,
/// optionally scoped to one attribute class (null = every channel of the fixture). Undo is
/// implemented once, correctly, for all of them: capture each touched channel's stored
/// value and knockout state before applying the change, and restore both verbatim on Undo -
/// the same "snapshot then restore" approach <c>SelectionCommandBase</c> uses for selection.
/// </summary>
public abstract class ProgrammerChannelCommandBase : IConsoleCommand, IReplayableCommand
{
    private readonly record struct ChannelSnapshot(int Universe, int Channel, bool HadValue, byte Value, bool WasKnockedOut);

    private readonly IReadOnlyList<PatchedFixture> _targets;
    private List<ChannelSnapshot>? _previous;

    /// <summary>The fixtures this command was constructed with - exposed so a subclass's own
    /// CreateFreshInstance() can pass the same construction-time targets to a brand-new instance,
    /// never this instance's own accumulated _previous snapshot.</summary>
    protected IReadOnlyList<PatchedFixture> Targets => _targets;

    /// <summary>Null means "every channel of the fixture"; otherwise only channels in this attribute class.</summary>
    protected AttributeClass? AttributeFilter { get; }

    protected abstract ConsoleActionType ActionType { get; }

    /// <summary>Builds a brand-new instance with this command's own construction-time
    /// parameters, never sharing this instance's own _previous snapshot (docs/COMMAND_SURFACE_KEY_SPEC.md
    /// MACROS "REPLAY INSTANCE SAFETY").</summary>
    public abstract IConsoleCommand CreateFreshInstance();

    protected ProgrammerChannelCommandBase(IReadOnlyList<PatchedFixture> targets, AttributeClass? attributeFilter)
    {
        _targets = targets;
        AttributeFilter = attributeFilter;
    }

    /// <summary>
    /// Applies this command's effect to one fixture channel. Return true only if something
    /// actually changed - that's what marks the fixture as affected and captures undo state
    /// for this channel, so a no-op (e.g. knocking out an already-knocked-out channel) can
    /// cleanly report "nothing happened" by returning false. Takes the full context (not
    /// just Programmer) so a subclass like AdjustIntensityCommand can also read
    /// <see cref="ConsoleContext.EffectiveOutput"/> when it needs the true merged value.
    /// </summary>
    protected abstract bool ApplyToChannel(ConsoleContext context, PatchedFixture fixture, FixtureChannel channel);

    /// <summary>Hook for a subclass to add its own fields to the result (e.g. ApplyPresetCommand attaching the Preset it applied) via a `with` expression.</summary>
    protected virtual CommandResult DecorateResult(CommandResult result) => result;

    /// <summary>Which of the fixture's channels this command considers. Defaults to the
    /// AttributeFilter-based family scoping every existing subclass already used before this was
    /// extracted; a subclass needing finer granularity (e.g. ReleaseParameterCommand, scoped to
    /// one semantic parameter's ChannelType(s) rather than a whole family) overrides this instead
    /// of duplicating Execute()'s snapshot/undo machinery.</summary>
    protected virtual IEnumerable<FixtureChannel> SelectChannels(PatchedFixture fixture) =>
        AttributeFilter is { } cls ? fixture.ChannelsForAttribute(cls) : fixture.Mode.Channels;

    public CommandResult Execute(ConsoleContext context)
    {
        var snapshots = new List<ChannelSnapshot>();
        var affected = new List<PatchedFixture>();
        var previousValues = new Dictionary<(Guid, ChannelType), byte>();
        var newValues = new Dictionary<(Guid, ChannelType), byte>();

        foreach (var fixture in _targets)
        {
            var channels = SelectChannels(fixture);
            bool touchedAny = false;

            foreach (var channel in channels)
            {
                int idx = fixture.AbsoluteIndex(channel);
                bool hadValue = context.Programmer.HasStoredValue(fixture.UniverseId, idx, out var beforeValue);
                bool wasKnockedOut = context.Programmer.IsKnockedOut(fixture.UniverseId, idx);

                if (!ApplyToChannel(context, fixture, channel)) continue;

                snapshots.Add(new ChannelSnapshot(fixture.UniverseId, idx, hadValue, hadValue ? beforeValue : (byte)0, wasKnockedOut));
                touchedAny = true;

                if (hadValue) previousValues[(fixture.Id, channel.Type)] = beforeValue;
                if (context.Programmer.HasStoredValue(fixture.UniverseId, idx, out var afterValue))
                    newValues[(fixture.Id, channel.Type)] = afterValue;
            }

            if (touchedAny) affected.Add(fixture);
        }

        _previous = snapshots;

        return DecorateResult(new CommandResult
        {
            ActionType = ActionType,
            AffectedFixtures = affected,
            AffectedAttributes = AttributeFilter is { } filterClass ? new[] { filterClass } : Array.Empty<AttributeClass>(),
            PreviousValues = previousValues,
            NewValues = newValues,
        });
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

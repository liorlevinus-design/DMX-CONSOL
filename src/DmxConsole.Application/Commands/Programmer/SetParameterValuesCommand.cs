using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// Parameter-scoped AT (CLAUDE.md §16, PSEL-1/PSEL-2/PSEL-3): writes an already-resolved list of
/// (fixture, channel, value) triples into the Programmer as ONE atomic, undoable transaction -
/// regardless of whether it came from a fixed value, a distributed value fan, or several logical
/// parameters at once. Deliberately one generic command class reused for every logical parameter,
/// never a per-parameter command (no SetRedCommand/SetPanCommand) - the grammar layer
/// (CommandComposer) is responsible for resolving targets via
/// <see cref="ParameterTargetResolver"/> and computing each target's byte value; this command only
/// ever applies exactly what it was constructed with.
///
/// Reuses <see cref="ProgrammerChannelCommandBase"/>'s snapshot/undo machinery like every other
/// Programmer-mutating command (AdjustIntensityCommand, SetAttributeValueCommand,
/// ReleaseParameterCommand) - the only new thing here is that <see cref="SelectChannels"/> and
/// <see cref="ApplyToChannel"/> are keyed per-target instead of by one shared AttributeClass/
/// ChannelType/value, since a single Parameter AT can span several different logical parameters
/// (RED+GREEN) and/or several different values per fixture (a distributed fan) at once.
/// </summary>
public sealed class SetParameterValuesCommand : ProgrammerChannelCommandBase
{
    /// <summary>One resolved write: a specific fixture's specific channel gets a specific byte
    /// value. <see cref="ParameterTargetResolver"/> resolves WHICH (fixture, channel) pairs are
    /// compatible; the grammar layer computes the VALUE (fixed, or interpolated per fixture
    /// position) - this record is simply their intersection, ready to write.</summary>
    public readonly record struct ParameterValueTarget(PatchedFixture Fixture, FixtureChannel Channel, byte Value);

    private readonly IReadOnlyList<ParameterValueTarget> _valueTargets;
    private readonly IReadOnlyList<ParameterTargetResolver.SkippedTarget> _skipped;
    private readonly Dictionary<(Guid FixtureId, ChannelType Channel), byte> _valueByKey;

    public SetParameterValuesCommand(
        IReadOnlyList<ParameterValueTarget> valueTargets,
        IReadOnlyList<ParameterTargetResolver.SkippedTarget>? skipped = null)
        : base(BuildFixtureList(valueTargets), attributeFilter: null)
    {
        _valueTargets = valueTargets;
        _skipped = skipped ?? Array.Empty<ParameterTargetResolver.SkippedTarget>();
        _valueByKey = new Dictionary<(Guid, ChannelType), byte>();
        foreach (var target in valueTargets)
            _valueByKey[(target.Fixture.Id, target.Channel.Type)] = target.Value;
    }

    protected override ConsoleActionType ActionType => ConsoleActionType.SetParameterValues;

    protected override IEnumerable<FixtureChannel> SelectChannels(PatchedFixture fixture) =>
        _valueTargets.Where(t => t.Fixture.Id == fixture.Id).Select(t => t.Channel);

    protected override bool ApplyToChannel(ConsoleContext context, PatchedFixture fixture, FixtureChannel channel)
    {
        if (!_valueByKey.TryGetValue((fixture.Id, channel.Type), out var value)) return false;
        context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(channel), value);
        return true;
    }

    protected override CommandResult DecorateResult(CommandResult result)
    {
        var attributes = _valueTargets.Select(t => t.Channel.Type.ToAttributeClass()).Distinct().ToList();
        result = result with { AffectedAttributes = attributes };

        if (_skipped.Count == 0) return result;

        int affected = _valueTargets.Select(t => t.Fixture.Id).Distinct().Count();
        return result with
        {
            Warning = $"{affected} fixture(s) affected, {_skipped.Count} (fixture, parameter) pair(s) skipped - parameter not supported.",
        };
    }

    private static List<PatchedFixture> BuildFixtureList(IReadOnlyList<ParameterValueTarget> valueTargets)
    {
        var list = new List<PatchedFixture>();
        var seen = new HashSet<Guid>();
        foreach (var target in valueTargets)
            if (seen.Add(target.Fixture.Id)) list.Add(target.Fixture);
        return list;
    }

    public override IConsoleCommand CreateFreshInstance() => new SetParameterValuesCommand(_valueTargets, _skipped);
}

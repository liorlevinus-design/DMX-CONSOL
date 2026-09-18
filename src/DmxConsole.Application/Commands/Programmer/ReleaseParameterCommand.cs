using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// PARAMETER RELEASE (docs/COMMAND_SURFACE_KEY_SPEC.md §7): releases Programmer values for one
/// semantic parameter only, leaving every other parameter - including siblings in the same
/// family - untouched. A third, finer granularity below RELEASE (all families) and FAMILY
/// RELEASE (one family): "PAN RELEASE" affects only Pan(+PanFine), never Tilt, even though both
/// are Position. "Parameter" means the complete semantic parameter, not one raw DMX byte - a
/// multi-byte parameter (Pan = Pan+PanFine) releases as one atomic unit via
/// ChannelTypeExtensions.SemanticComponents, the same abstraction a future coarse/fine parameter
/// would reuse without this command needing to change.
/// </summary>
public sealed class ReleaseParameterCommand : ProgrammerChannelCommandBase
{
    private readonly ChannelType _parameter;
    private readonly IReadOnlyList<ChannelType> _components;

    public ReleaseParameterCommand(IReadOnlyList<PatchedFixture> targets, ChannelType parameter)
        : base(targets, parameter.ToAttributeClass())
    {
        _parameter = parameter;
        _components = parameter.SemanticComponents();
    }

    protected override ConsoleActionType ActionType => ConsoleActionType.ReleaseParameter;

    protected override IEnumerable<FixtureChannel> SelectChannels(PatchedFixture fixture) =>
        fixture.Mode.Channels.Where(c => _components.Contains(c.Type));

    protected override bool ApplyToChannel(ConsoleContext context, PatchedFixture fixture, FixtureChannel channel)
    {
        int idx = fixture.AbsoluteIndex(channel);
        bool hasSomething = context.Programmer.HasStoredValue(fixture.UniverseId, idx, out _) || context.Programmer.IsKnockedOut(fixture.UniverseId, idx);
        if (!hasSomething) return false;

        context.Programmer.ClearChannel(fixture.UniverseId, idx);
        return true;
    }

    public override IConsoleCommand CreateFreshInstance() => new ReleaseParameterCommand(Targets, _parameter);
}

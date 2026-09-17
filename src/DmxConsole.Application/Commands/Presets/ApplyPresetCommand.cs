using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;

namespace DmxConsole.Application.Commands.Presets;

/// <summary>
/// Writes a Preset's stored values into the Programmer for the target fixtures. Built on
/// top of <see cref="ProgrammerChannelCommandBase"/> (scoped to the preset's AttributeClass)
/// so Undo/PreviousValues/NewValues all come for free - a fixture only gets touched for the
/// channel types the Preset actually contains, so a Position preset recorded from a
/// fixture with Pan+Tilt still applies safely to one with only Pan.
/// </summary>
public sealed class ApplyPresetCommand : ProgrammerChannelCommandBase
{
    private readonly Preset _preset;

    public ApplyPresetCommand(IReadOnlyList<PatchedFixture> targets, Preset preset)
        : base(targets, preset.Class)
    {
        _preset = preset;
    }

    protected override ConsoleActionType ActionType => ConsoleActionType.ApplyPreset;

    protected override bool ApplyToChannel(ConsoleContext context, PatchedFixture fixture, FixtureChannel channel)
    {
        if (!_preset.Values.TryGetValue(channel.Type, out var value)) return false;

        context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(channel), value);
        return true;
    }

    protected override CommandResult DecorateResult(CommandResult result) => result with { Preset = _preset };

    public override IConsoleCommand CreateFreshInstance() => new ApplyPresetCommand(Targets, _preset);
}

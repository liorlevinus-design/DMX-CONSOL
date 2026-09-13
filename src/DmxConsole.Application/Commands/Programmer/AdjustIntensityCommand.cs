using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using CoreProgrammer = DmxConsole.Core.Engine.Programmer;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// Raises or lowers the target fixtures' Intensity (Dimmer channel) by a percentage.
/// Hard-wired to <see cref="AttributeClass.Intensity"/> via the base constructor - this is
/// a compile-time scope limit, not a runtime check: Position/Color/Beam relative adjustment
/// needs its own semantics (a single scalar percentage doesn't map cleanly onto Pan+Tilt or
/// R+G+B) and is deferred to a later step, most likely alongside Presets/Palettes.
///
/// "Current value" for a Relative adjustment is whatever the Programmer itself currently
/// holds for that channel (or the fixture's channel DefaultValue if untouched) - NOT the
/// live merged DMX output, which could differ if a Cue or Effect is driving the fixture.
/// Reading the true merged output would need DmxOutputEngine to expose its last computed
/// snapshot, which doesn't exist yet; this is an accepted limitation for this step.
/// </summary>
public sealed class AdjustIntensityCommand : ProgrammerChannelCommandBase
{
    private readonly AdjustOperation _operation;
    private readonly double _percent;

    public AdjustIntensityCommand(IReadOnlyList<PatchedFixture> targets, AdjustOperation operation, double percent)
        : base(targets, AttributeClass.Intensity)
    {
        _operation = operation;
        _percent = percent;
    }

    protected override ConsoleActionType ActionType => ConsoleActionType.AdjustIntensity;

    protected override bool ApplyToChannel(CoreProgrammer programmer, PatchedFixture fixture, FixtureChannel channel)
    {
        int idx = fixture.AbsoluteIndex(channel);
        byte current = programmer.HasStoredValue(fixture.UniverseId, idx, out var stored) ? stored : channel.DefaultValue;

        double currentPercent = current / 255.0 * 100.0;
        double newPercent = _operation == AdjustOperation.Relative ? currentPercent + _percent : _percent;
        newPercent = Math.Clamp(newPercent, 0.0, 100.0);
        byte newValue = (byte)Math.Round(newPercent / 100.0 * 255.0);

        programmer.SetChannel(fixture.UniverseId, idx, newValue);
        return true;
    }
}

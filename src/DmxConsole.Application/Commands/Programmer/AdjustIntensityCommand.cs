using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// Raises or lowers the target fixtures' Intensity (Dimmer channel) by a percentage.
/// Hard-wired to <see cref="AttributeClass.Intensity"/> via the base constructor - this is
/// a compile-time scope limit, not a runtime check: Position/Color/Beam relative adjustment
/// needs its own semantics (a single scalar percentage doesn't map cleanly onto Pan+Tilt or
/// R+G+B) and is deferred to a later step, most likely alongside Presets/Palettes.
///
/// A Relative adjustment is computed against the engine's actual merged output
/// (<see cref="ConsoleContext.EffectiveOutput"/>) - "raise Fronts by 20" means 20 points on
/// top of whatever the fixture is really showing right now, whether that came from a Cue,
/// an Effect, or the Programmer itself. It deliberately does NOT fall back to reading the
/// Programmer's own stored value or the fixture's DefaultValue: if a universe has never
/// been ticked (engine never started), there genuinely is no "current" yet and this reads
/// as 0 - an accepted edge case, not a bug, since nothing is actually being output either.
/// Absolute is unaffected either way - it sets the requested percentage outright.
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

    protected override bool ApplyToChannel(ConsoleContext context, PatchedFixture fixture, FixtureChannel channel)
    {
        int idx = fixture.AbsoluteIndex(channel);

        double newPercent;
        if (_operation == AdjustOperation.Relative)
        {
            byte current = context.EffectiveOutput.GetEffectiveValue(fixture.UniverseId, idx);
            newPercent = (current / 255.0 * 100.0) + _percent;
        }
        else
        {
            newPercent = _percent;
        }

        newPercent = Math.Clamp(newPercent, 0.0, 100.0);
        byte newValue = (byte)Math.Round(newPercent / 100.0 * 255.0);

        context.Programmer.SetChannel(fixture.UniverseId, idx, newValue);
        return true;
    }

    public override IConsoleCommand CreateFreshInstance() => new AdjustIntensityCommand(Targets, _operation, _percent);
}

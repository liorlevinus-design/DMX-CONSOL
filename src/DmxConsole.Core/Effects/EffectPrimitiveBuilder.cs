using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Effects;

/// <summary>Maps simple UI primitives onto the canonical step model.</summary>
public static class EffectPrimitiveBuilder
{
    public static IReadOnlyList<EffectStep> Build(
        EffectPrimitiveKind kind,
        ChannelType channel,
        double min,
        double max,
        double dutyCyclePercent = 50)
    {
        return kind switch
        {
            EffectPrimitiveKind.Sine => TwoStep(channel, min, max, 100, 100, -100, -100),
            EffectPrimitiveKind.Ramp => new[]
            {
                Step(channel, min, 100, 100),
                Step(channel, max, 0, 0),
            },
            EffectPrimitiveKind.Triangle => TwoStep(channel, min, max, 100, 100, 0, 0),
            EffectPrimitiveKind.Square => TwoStep(
                channel, min, max, 100, Math.Clamp(dutyCyclePercent, 1, 99), 0, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static IReadOnlyList<EffectStep> TwoStep(
        ChannelType channel,
        double min,
        double max,
        double width,
        double transition,
        double accel,
        double decel) =>
        new[]
        {
            Step(channel, min, width, transition, accel, decel),
            Step(channel, max, width, transition, accel, decel),
        };

    private static EffectStep Step(
        ChannelType channel,
        double value,
        double width,
        double transition,
        double accel = 0,
        double decel = 0) =>
        new()
        {
            AbsoluteValues = new Dictionary<ChannelType, double> { [channel] = value },
            Width = width,
            Transition = transition,
            Accel = accel,
            Decel = decel,
        };
}

using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Effects;

/// <summary>Maps simple UI primitives onto the canonical step model.</summary>
public static class EffectPrimitiveBuilder
{
    public static IReadOnlyList<EffectStep> BuildRainbow(double brightness)
    {
        double level = Math.Clamp(brightness, 0, 1);
        return Enumerable.Range(0, 6)
            .Select(index => HsvColor.ToRgb(index / 6.0, 1, level))
            .Select(rgb => new EffectStep
            {
                AbsoluteValues = new Dictionary<ChannelType, double>
                {
                    [ChannelType.ColorRed] = rgb.R,
                    [ChannelType.ColorGreen] = rgb.G,
                    [ChannelType.ColorBlue] = rgb.B,
                },
                Width = 100,
                Transition = 100,
            })
            .ToArray();
    }

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
            EffectPrimitiveKind.Square => Square(channel, min, max, dutyCyclePercent),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static IReadOnlyList<EffectStep> Square(
        ChannelType channel, double min, double max, double dutyCyclePercent)
    {
        double duty = Math.Clamp(dutyCyclePercent, 1, 99);
        // A 0% transition snaps to the NEXT step at the beginning of the current width.
        // min->max owns the ON duration; max->min owns the OFF duration.
        return new[]
        {
            Step(channel, min, duty, 0),
            Step(channel, max, 100 - duty, 0),
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

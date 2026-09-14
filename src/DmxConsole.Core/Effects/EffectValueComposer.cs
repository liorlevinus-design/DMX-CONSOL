namespace DmxConsole.Core.Effects;

/// <summary>Canonical composition of a phaser step's separate value layers.</summary>
public static class EffectValueComposer
{
    public static byte Compose(
        byte baseValue,
        double? absoluteValue,
        double relativeValue,
        AddMode addMode)
    {
        double center = absoluteValue ?? baseValue;
        double result = addMode switch
        {
            AddMode.Abs or AddMode.Normal => center + relativeValue,
            AddMode.Plus => center + Math.Max(0, relativeValue),
            AddMode.Minus => center - Math.Max(0, -relativeValue),
            _ => throw new ArgumentOutOfRangeException(nameof(addMode), addMode, null),
        };

        return (byte)Math.Round(Math.Clamp(result, byte.MinValue, byte.MaxValue),
            MidpointRounding.AwayFromZero);
    }
}

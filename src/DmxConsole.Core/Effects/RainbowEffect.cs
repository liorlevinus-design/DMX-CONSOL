namespace DmxConsole.Core.Effects;

/// <summary>
/// Cycles RGB fixtures around the color wheel. With Spread &gt; 0 each successive
/// fixture sits at a later point on the wheel, producing a travelling rainbow chase.
/// </summary>
public sealed class RainbowEffect : Effect
{
    public override string EffectType => "Rainbow";

    /// <summary>Brightness (0..1) applied to every color while cycling.</summary>
    public double Brightness { get; set; } = 1.0;

    public override IEnumerable<((int Universe, int Channel) Key, byte Value)> Evaluate(double elapsedSeconds)
    {
        for (int i = 0; i < Fixtures.Count; i++)
        {
            var fixture = Fixtures[i];
            var red = fixture.FindChannel(ChannelType.ColorRed);
            var green = fixture.FindChannel(ChannelType.ColorGreen);
            var blue = fixture.FindChannel(ChannelType.ColorBlue);
            if (red is null || green is null || blue is null) continue;

            double hue = PhaseFor(i, elapsedSeconds);
            var (r, g, b) = HsvColor.ToRgb(hue, saturation: 1.0, value: Math.Clamp(Brightness, 0, 1));

            yield return ((fixture.UniverseId, fixture.AbsoluteIndex(red)), r);
            yield return ((fixture.UniverseId, fixture.AbsoluteIndex(green)), g);
            yield return ((fixture.UniverseId, fixture.AbsoluteIndex(blue)), b);
        }
    }
}

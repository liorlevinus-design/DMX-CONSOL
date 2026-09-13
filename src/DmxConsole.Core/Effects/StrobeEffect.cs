namespace DmxConsole.Core.Effects;

/// <summary>
/// Square-wave flash of one channel type. With Spread=0 every fixture flashes together;
/// a non-zero Spread turns it into a travelling on/off wave across the group.
/// </summary>
public sealed class StrobeEffect : Effect
{
    public override string EffectType => "Strobe";

    public ChannelType TargetChannel { get; set; } = ChannelType.Dimmer;
    public byte OnValue { get; set; } = 255;
    public byte OffValue { get; set; } = 0;

    /// <summary>Fraction (0..1) of each cycle spent ON.</summary>
    public double DutyCycle { get; set; } = 0.15;

    public override IEnumerable<((int Universe, int Channel) Key, byte Value)> Evaluate(double elapsedSeconds)
    {
        for (int i = 0; i < Fixtures.Count; i++)
        {
            var fixture = Fixtures[i];
            var channel = fixture.FindChannel(TargetChannel);
            if (channel is null) continue;

            double phase = PhaseFor(i, elapsedSeconds);
            byte value = phase < DutyCycle ? OnValue : OffValue;

            yield return ((fixture.UniverseId, fixture.AbsoluteIndex(channel)), value);
        }
    }
}

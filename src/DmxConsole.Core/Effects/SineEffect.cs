namespace DmxConsole.Core.Effects;

/// <summary>Smooth oscillation of one channel type (typically Dimmer) between Min and Max.</summary>
public sealed class SineEffect : Effect
{
    public override string EffectType => "Sine";

    public ChannelType TargetChannel { get; set; } = ChannelType.Dimmer;
    public byte Min { get; set; } = 0;
    public byte Max { get; set; } = 255;

    public override IEnumerable<((int Universe, int Channel) Key, byte Value)> Evaluate(double elapsedSeconds)
    {
        for (int i = 0; i < Fixtures.Count; i++)
        {
            var fixture = Fixtures[i];
            var channel = fixture.FindChannel(TargetChannel);
            if (channel is null) continue;

            double phase = PhaseFor(i, elapsedSeconds);
            double wave = 0.5 + 0.5 * Math.Sin(2 * Math.PI * phase); // 0..1
            byte value = (byte)Math.Round(Min + (Max - Min) * wave);

            yield return ((fixture.UniverseId, fixture.AbsoluteIndex(channel)), value);
        }
    }
}

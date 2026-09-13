namespace DmxConsole.Core.Effects;

/// <summary>
/// Classic step chase: at any instant, <see cref="Width"/> consecutive fixtures out of
/// the group are ON and the rest are OFF, stepping through the group SpeedHz times per second.
/// </summary>
public sealed class ChaseEffect : Effect
{
    public override string EffectType => "Chase";

    public ChannelType TargetChannel { get; set; } = ChannelType.Dimmer;
    public byte OnValue { get; set; } = 255;
    public byte OffValue { get; set; } = 0;

    /// <summary>How many consecutive fixtures are lit at once.</summary>
    public int Width { get; set; } = 1;

    public override IEnumerable<((int Universe, int Channel) Key, byte Value)> Evaluate(double elapsedSeconds)
    {
        int count = Fixtures.Count;
        if (count == 0) yield break;

        double cyclePosition = (elapsedSeconds * SpeedHz) % 1.0;
        if (cyclePosition < 0) cyclePosition += 1.0;
        int activeStep = (int)(cyclePosition * count) % count;

        for (int i = 0; i < count; i++)
        {
            var fixture = Fixtures[i];
            var channel = fixture.FindChannel(TargetChannel);
            if (channel is null) continue;

            int distance = ((i - activeStep) % count + count) % count;
            bool isOn = distance < Width;

            yield return ((fixture.UniverseId, fixture.AbsoluteIndex(channel)), isOn ? OnValue : OffValue);
        }
    }
}

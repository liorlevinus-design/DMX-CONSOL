using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Effects;

/// <summary>
/// One canonical phaser step. Absolute and relative values remain distinct so editing,
/// storage, and playback never silently collapse the two value layers.
/// </summary>
public sealed record EffectStep
{
    public IReadOnlyDictionary<ChannelType, double> AbsoluteValues { get; init; }
        = new Dictionary<ChannelType, double>();

    public IReadOnlyDictionary<ChannelType, double> RelativeValues { get; init; }
        = new Dictionary<ChannelType, double>();

    /// <summary>Step duration as a percentage of one beat.</summary>
    public double Width { get; init; } = 100;

    /// <summary>Percentage of Width spent transitioning to the next step.</summary>
    public double Transition { get; init; } = 100;

    public double Accel { get; init; }
    public double Decel { get; init; }
}

namespace DmxConsole.Core.Engine;

/// <summary>
/// Optional output-layer capability for composition against the immutable result of all
/// strictly lower priority tiers in the same engine tick.
/// </summary>
public interface IBaseAwareLayer
{
    bool TryGetChannelValue(int universeId, int channelIndex, byte baseValue, out byte value);
}

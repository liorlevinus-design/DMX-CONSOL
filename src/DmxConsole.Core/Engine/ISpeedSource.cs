namespace DmxConsole.Core.Engine;

/// <summary>Optional future speed-master hook. Step G does not provide an implementation.</summary>
public interface ISpeedSource
{
    double GetSpeedMultiplier();
}

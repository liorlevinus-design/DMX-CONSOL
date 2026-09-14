namespace DmxConsole.Core.Engine;

/// <summary>
/// Narrow mutation boundary for wiring independently mergeable output layers. Application
/// commands may register effect layers through this contract without controlling engine timing,
/// lifecycle, universe output, or effective-output reads.
/// </summary>
public interface IOutputLayerRegistry
{
    void AddLayer(IOutputLayer layer);
    void RemoveLayer(IOutputLayer layer);
}

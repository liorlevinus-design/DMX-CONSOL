using System.Collections.ObjectModel;
using DmxConsole.Core.Engine;

namespace DmxConsole.Core.Effects;

/// <summary>Owns show phasers and keeps collection membership and output wiring atomic.</summary>
public sealed class EffectBank
{
    private readonly IOutputLayerRegistry? _registry;
    public ObservableCollection<EffectPhaser> Effects { get; } = new();

    public EffectBank(IOutputLayerRegistry? registry = null) => _registry = registry;

    public void Add(EffectPhaser effect) => Insert(Effects.Count, effect);

    public void Insert(int index, EffectPhaser effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (Effects.Contains(effect)) return;
        Effects.Insert(Math.Clamp(index, 0, Effects.Count), effect);
        _registry?.AddLayer(effect);
    }

    public bool Remove(EffectPhaser effect)
    {
        if (!Effects.Remove(effect)) return false;
        _registry?.RemoveLayer(effect);
        return true;
    }
}

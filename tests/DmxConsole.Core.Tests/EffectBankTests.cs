using DmxConsole.Core.Effects;
using DmxConsole.Core.Engine;

namespace DmxConsole.Core.Tests;

public sealed class EffectBankTests
{
    [Fact]
    public void AddAndRemove_KeepOutputRegistryInSync()
    {
        var registry = new RecordingRegistry();
        var bank = new EffectBank(registry);
        var effect = new EffectPhaser();

        bank.Add(effect);
        Assert.Contains(effect, bank.Effects);
        Assert.Contains(effect, registry.Layers);

        Assert.True(bank.Remove(effect));
        Assert.DoesNotContain(effect, bank.Effects);
        Assert.DoesNotContain(effect, registry.Layers);
    }

    private sealed class RecordingRegistry : IOutputLayerRegistry
    {
        public List<IOutputLayer> Layers { get; } = new();
        public void AddLayer(IOutputLayer layer) => Layers.Add(layer);
        public void RemoveLayer(IOutputLayer layer) => Layers.Remove(layer);
    }
}

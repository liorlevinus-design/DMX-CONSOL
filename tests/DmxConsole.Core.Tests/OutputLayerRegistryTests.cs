using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Tests;

public sealed class OutputLayerRegistryTests
{
    [Fact]
    public void RegisteredLayer_ContributesUntilRemoved()
    {
        var patch = new Patch();
        var profile = new FixtureProfile
        {
            Id = "registry-test",
            Manufacturer = "Test",
            Model = "Dimmer",
            Modes = new[]
            {
                new FixtureMode
                {
                    Name = "1ch",
                    Channels = new[]
                    {
                        new FixtureChannel
                        {
                            Name = "Dimmer",
                            Type = ChannelType.Dimmer,
                            Offset = 0,
                        },
                    },
                },
            },
        };
        patch.Add(new PatchedFixture(profile, profile.Modes[0], 0, 1));

        var engine = new DmxOutputEngine(patch);
        IOutputLayerRegistry registry = engine;
        var layer = new SingleChannelLayer();

        registry.AddLayer(layer);
        engine.Tick();
        Assert.Equal(123, engine.GetEffectiveValue(0, 0));

        registry.RemoveLayer(layer);
        engine.Tick();
        Assert.Equal(0, engine.GetEffectiveValue(0, 0));
    }

    private sealed class SingleChannelLayer : IOutputLayer
    {
        public string Name => "Test layer";
        public int Priority => 300;
        public bool IsActive => true;

        public bool TryGetChannelValue(int universeId, int channelIndex, out byte value)
        {
            value = 123;
            return universeId == 0 && channelIndex == 0;
        }
    }
}

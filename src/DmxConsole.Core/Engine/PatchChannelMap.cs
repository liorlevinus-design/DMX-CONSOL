using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Engine;

/// <summary>
/// Flattened, fast-lookup view of a Patch: for every DMX address actually in use,
/// which fixture/channel definition lives there. Rebuilt whenever the patch changes.
/// </summary>
public sealed class PatchChannelMap
{
    public readonly record struct Entry(PatchedFixture Fixture, FixtureChannel Channel);

    private readonly Dictionary<(int Universe, int Channel), Entry> _map;

    public PatchChannelMap(Patch patch)
    {
        _map = new Dictionary<(int, int), Entry>();
        foreach (var fixture in patch.Fixtures)
        {
            foreach (var channel in fixture.Mode.Channels)
            {
                _map[(fixture.UniverseId, fixture.AbsoluteIndex(channel))] = new Entry(fixture, channel);
            }
        }
    }

    public bool TryGet(int universeId, int channelIndex, out Entry entry) =>
        _map.TryGetValue((universeId, channelIndex), out entry);

    public IEnumerable<(int Universe, int Channel, Entry Entry)> AllEntries =>
        _map.Select(kv => (kv.Key.Universe, kv.Key.Channel, kv.Value));

    public IEnumerable<int> UniverseIds => _map.Keys.Select(k => k.Universe).Distinct();
}

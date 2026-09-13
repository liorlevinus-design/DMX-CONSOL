namespace DmxConsole.Core.Fixtures;

/// <summary>
/// One selectable personality/mode of a fixture (e.g. "8-channel", "Basic", "Extended").
/// Different modes of the same fixture typically have different DMX footprints.
/// </summary>
public sealed class FixtureMode
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<FixtureChannel> Channels { get; init; } = Array.Empty<FixtureChannel>();

    /// <summary>Number of DMX channels this mode occupies.</summary>
    public int FootprintSize => Channels.Count == 0 ? 0 : Channels.Max(c => c.Offset) + 1;

    public bool HasPanTilt => Channels.Any(c => c.Type == ChannelType.Pan) && Channels.Any(c => c.Type == ChannelType.Tilt);
}

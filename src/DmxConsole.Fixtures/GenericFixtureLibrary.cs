using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Fixtures;

/// <summary>
/// A small set of built-in, manufacturer-agnostic fixture profiles so the console is
/// immediately usable without an external fixture library. A JSON-based, user-extensible
/// library (custom format or GDTF import) is planned for a later phase.
/// </summary>
public static class GenericFixtureLibrary
{
    public static IReadOnlyList<FixtureProfile> All { get; } = new[]
    {
        Dimmer1Channel(),
        Rgb3Channel(),
        Rgbw4Channel(),
        MovingHead8Channel(),
    };

    public static FixtureProfile ById(string id) =>
        All.FirstOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException($"No built-in fixture profile with id '{id}'.");

    private static FixtureProfile Dimmer1Channel() => new()
    {
        Id = "generic-dimmer-1ch",
        Manufacturer = "Generic",
        Model = "Dimmer (1ch)",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "1-channel",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 },
                },
            },
        },
    };

    private static FixtureProfile Rgb3Channel() => new()
    {
        Id = "generic-rgb-3ch",
        Manufacturer = "Generic",
        Model = "RGB Par (3ch)",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "3-channel",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 0 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 1 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 2 },
                },
            },
        },
    };

    private static FixtureProfile Rgbw4Channel() => new()
    {
        Id = "generic-rgbw-4ch",
        Manufacturer = "Generic",
        Model = "RGBW Par (4ch)",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "4-channel",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 0 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 1 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 2 },
                    new FixtureChannel { Name = "White", Type = ChannelType.ColorWhite, Offset = 3 },
                },
            },
        },
    };

    private static FixtureProfile MovingHead8Channel() => new()
    {
        Id = "generic-movinghead-8ch",
        Manufacturer = "Generic",
        Model = "Moving Head (8ch)",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "8-channel",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 },
                    new FixtureChannel { Name = "Pan Fine", Type = ChannelType.PanFine, Offset = 1 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2 },
                    new FixtureChannel { Name = "Tilt Fine", Type = ChannelType.TiltFine, Offset = 3 },
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 4 },
                    new FixtureChannel { Name = "Shutter/Strobe", Type = ChannelType.Shutter, Offset = 5 },
                    new FixtureChannel { Name = "Color Wheel", Type = ChannelType.ColorWheel, Offset = 6 },
                    new FixtureChannel { Name = "Gobo", Type = ChannelType.Gobo, Offset = 7 },
                },
            },
        },
    };
}

using DmxConsole.Core.Fixtures;
using Xunit;

namespace DmxConsole.Core.Tests;

public class PatchTests
{
    private static FixtureProfile Rgb3() => new()
    {
        Id = "test-rgb",
        Manufacturer = "Test",
        Model = "RGB",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "3ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 0 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 1 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 2 },
                },
            },
        },
    };

    [Fact]
    public void Add_NonOverlappingFixtures_Succeeds()
    {
        var patch = new Patch();
        var profile = Rgb3();
        patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1));
        patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 4));

        Assert.Equal(2, patch.Fixtures.Count);
    }

    [Fact]
    public void Add_OverlappingFixtures_Throws()
    {
        var patch = new Patch();
        var profile = Rgb3();
        patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1));

        Assert.Throws<InvalidOperationException>(() =>
            patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 3)));
    }

    [Fact]
    public void Add_SameAddressesDifferentUniverse_Succeeds()
    {
        var patch = new Patch();
        var profile = Rgb3();
        patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1));
        patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 1, startAddress: 1));

        Assert.Equal(2, patch.Fixtures.Count);
    }

    [Fact]
    public void StartAddress_TooLargeForFootprint_Throws()
    {
        var profile = Rgb3();
        var fixture = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.StartAddress = 511);
    }
}

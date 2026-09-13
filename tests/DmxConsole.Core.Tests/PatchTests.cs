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

    [Fact]
    public void Add_AssignsSequentialNumbers_WhenNotSpecified()
    {
        var patch = new Patch();
        var profile = Rgb3();
        var a = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        var b = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 4);

        patch.Add(a);
        patch.Add(b);

        Assert.Equal(1, a.Number);
        Assert.Equal(2, b.Number);
    }

    [Fact]
    public void Add_KeepsExplicitNumber_WhenSetBeforePatching()
    {
        var patch = new Patch();
        var profile = Rgb3();
        var a = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1) { Number = 10 };

        patch.Add(a);

        Assert.Equal(10, a.Number);
    }

    [Fact]
    public void Add_ReassignsNumber_WhenExplicitNumberAlreadyTaken()
    {
        var patch = new Patch();
        var profile = Rgb3();
        var a = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1) { Number = 5 };
        var b = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 4) { Number = 5 };

        patch.Add(a);
        patch.Add(b);

        Assert.Equal(5, a.Number);
        Assert.Equal(6, b.Number); // duplicate requested number bumped to next free one
    }

    [Fact]
    public void FindByNumber_ReturnsMatchingFixture()
    {
        var patch = new Patch();
        var profile = Rgb3();
        var a = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        patch.Add(a);

        Assert.Same(a, patch.FindByNumber(a.Number));
        Assert.Null(patch.FindByNumber(999));
    }
}

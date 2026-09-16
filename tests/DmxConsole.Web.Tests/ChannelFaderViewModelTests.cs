using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>Regression coverage for a real bug found while building the LIVE view (OPERATOR_UX_
/// ROADMAP.md §1): RefreshAllFaders()/ClearProgrammer used to assign the ChannelFaderViewModel's
/// own Value property to resync the display from the Programmer - but that property's setter
/// ALWAYS writes back to the Programmer (that's how a genuine fader drag works), so every refresh
/// immediately re-stored the just-displayed value straight back into the Programmer, silently
/// undoing the Release/Restore/ClearAll the refresh was supposed to reflect. RefreshFromProgrammer
/// must update the displayed Value without ever touching the Programmer.</summary>
public class ChannelFaderViewModelTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 20 } } } },
    };

    private static (PatchedFixture Fixture, FixtureChannel Channel, Programmer Programmer, ChannelFaderViewModel Fader) Build()
    {
        var profile = Dimmer1();
        var fixture = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        var channel = fixture.Mode.Channels[0];
        var programmer = new Programmer();
        var fader = new ChannelFaderViewModel(fixture, channel, programmer);
        return (fixture, channel, programmer, fader);
    }

    [Fact]
    public void Value_UserDrivenSet_WritesToProgrammer()
    {
        var (_, _, programmer, fader) = Build();

        fader.Value = 200;

        Assert.True(programmer.HasStoredValue(0, 0, out var stored));
        Assert.Equal(200, stored);
    }

    [Fact]
    public void RefreshFromProgrammer_UpdatesDisplay_NeverWritesToProgrammer()
    {
        var (_, _, programmer, fader) = Build();
        // Simulate the exact scenario that was broken: Programmer just got cleared (Release/
        // ClearAll), and the fader is being resynced to reflect that.
        programmer.SetChannel(0, 0, 150);
        programmer.ClearChannel(0, 0);
        Assert.False(programmer.HasStoredValue(0, 0, out _)); // confirms the "just cleared" premise

        fader.RefreshFromProgrammer(20); // e.g. falling back to the channel's own DefaultValue

        Assert.Equal(20, fader.Value); // display updated
        Assert.False(programmer.HasStoredValue(0, 0, out _)); // Programmer must STILL be empty
    }

    [Fact]
    public void RefreshFromProgrammer_NeverWritesToProgrammer_EvenWithADifferentValue()
    {
        // Proves the suppression is a real "never call SetChannel" guarantee, not a coincidental
        // same-value no-op: refresh to a DIFFERENT byte than what's actually stored, and confirm
        // the Programmer's own value is completely untouched by the display-only refresh.
        var (_, _, programmer, fader) = Build();
        programmer.SetChannel(0, 0, 77);

        fader.RefreshFromProgrammer(20);

        Assert.Equal(20, fader.Value); // display shows the refreshed value
        Assert.True(programmer.HasStoredValue(0, 0, out var stillStored));
        Assert.Equal(77, stillStored); // Programmer itself was never touched
    }
}

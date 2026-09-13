using DmxConsole.Core.Engine;
using Xunit;

namespace DmxConsole.Core.Tests;

public class ProgrammerTests
{
    [Fact]
    public void SetChannel_ThenTryGetChannelValue_ReturnsIt()
    {
        var programmer = new Programmer();
        programmer.SetChannel(0, 5, 200);

        Assert.True(programmer.TryGetChannelValue(0, 5, out var value));
        Assert.Equal(200, value);
    }

    [Fact]
    public void Knockout_SuppressesContribution_ButKeepsStoredValue()
    {
        var programmer = new Programmer();
        programmer.SetChannel(0, 5, 200);

        programmer.Knockout(0, 5);

        Assert.False(programmer.TryGetChannelValue(0, 5, out _)); // engine sees nothing
        Assert.True(programmer.HasStoredValue(0, 5, out var stored)); // admin query still sees it
        Assert.Equal(200, stored);
        Assert.True(programmer.IsKnockedOut(0, 5));
    }

    [Fact]
    public void Restore_AfterKnockout_BringsContributionBackExactly()
    {
        var programmer = new Programmer();
        programmer.SetChannel(0, 5, 200);
        programmer.Knockout(0, 5);

        programmer.Restore(0, 5);

        Assert.False(programmer.IsKnockedOut(0, 5));
        Assert.True(programmer.TryGetChannelValue(0, 5, out var value));
        Assert.Equal(200, value);
    }

    [Fact]
    public void ClearChannel_RemovesBothValueAndKnockoutState()
    {
        var programmer = new Programmer();
        programmer.SetChannel(0, 5, 200);
        programmer.Knockout(0, 5);

        programmer.ClearChannel(0, 5);

        Assert.False(programmer.HasStoredValue(0, 5, out _));
        Assert.False(programmer.IsKnockedOut(0, 5));
    }

    [Fact]
    public void ClearAll_RemovesEveryValueAndKnockoutState()
    {
        var programmer = new Programmer();
        programmer.SetChannel(0, 1, 10);
        programmer.SetChannel(0, 2, 20);
        programmer.Knockout(0, 2);

        programmer.ClearAll();

        Assert.False(programmer.HasStoredValue(0, 1, out _));
        Assert.False(programmer.HasStoredValue(0, 2, out _));
        Assert.False(programmer.IsKnockedOut(0, 2));
        Assert.False(programmer.IsActive);
    }

    [Fact]
    public void IsActive_TrueWhileAnyChannelIsKnockedOut_EvenWithNoStoredValues()
    {
        var programmer = new Programmer();
        programmer.SetChannel(0, 1, 5);
        programmer.Knockout(0, 1);
        programmer.ClearChannel(0, 1); // clears both - back to fully empty, sanity check first
        Assert.False(programmer.IsActive);

        programmer.Knockout(0, 1); // knockout without ever setting a value - edge case, still tracked
        Assert.True(programmer.IsActive);
    }
}

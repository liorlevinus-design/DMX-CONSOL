using DmxConsole.Core.Engine;
using Xunit;

namespace DmxConsole.Core.Tests;

/// <summary>
/// Parameter TIME slice (CLAUDE.md §16/ROADMAP §9a) - Programmer's parallel per-channel timing
/// store (SetTimeIn/SetTimeOut/TryGetTiming/SetTimingRaw), independent of the existing VALUE store
/// (_values) it sits alongside. Model/storage requirements 1-6 of the slice's own test plan.
/// </summary>
public class ProgrammerTimingTests
{
    [Fact]
    public void SetTimeIn_IsStorableAndReadable()
    {
        var programmer = new Programmer();
        programmer.SetTimeIn(0, 5, TimeSpan.FromSeconds(3));

        Assert.True(programmer.TryGetTiming(0, 5, out var timeIn, out var timeOut));
        Assert.Equal(TimeSpan.FromSeconds(3), timeIn);
        Assert.Null(timeOut);
    }

    [Fact]
    public void SetTimeOut_IsStorableAndReadable()
    {
        var programmer = new Programmer();
        programmer.SetTimeOut(0, 5, TimeSpan.FromSeconds(4));

        Assert.True(programmer.TryGetTiming(0, 5, out var timeIn, out var timeOut));
        Assert.Null(timeIn);
        Assert.Equal(TimeSpan.FromSeconds(4), timeOut);
    }

    [Fact]
    public void TimeIn_AndTimeOut_AreIndependent_SettingOneNeverClobbersTheOther()
    {
        var programmer = new Programmer();
        programmer.SetTimeIn(0, 5, TimeSpan.FromSeconds(1));
        programmer.SetTimeOut(0, 5, TimeSpan.FromSeconds(9));
        programmer.SetTimeIn(0, 5, TimeSpan.FromSeconds(2)); // must preserve TimeOut=9

        Assert.True(programmer.TryGetTiming(0, 5, out var timeIn, out var timeOut));
        Assert.Equal(TimeSpan.FromSeconds(2), timeIn);
        Assert.Equal(TimeSpan.FromSeconds(9), timeOut);
    }

    [Fact]
    public void BareTime_SetsBothSides_ToTheSameValue()
    {
        var programmer = new Programmer();
        var value = TimeSpan.FromSeconds(5);
        programmer.SetTimeIn(0, 5, value);
        programmer.SetTimeOut(0, 5, value);

        Assert.True(programmer.TryGetTiming(0, 5, out var timeIn, out var timeOut));
        Assert.Equal(value, timeIn);
        Assert.Equal(value, timeOut);
    }

    [Fact]
    public void SettingTiming_NeverChangesTheChannelsStoredValue()
    {
        var programmer = new Programmer();
        programmer.SetChannel(0, 5, 123);
        programmer.SetTimeIn(0, 5, TimeSpan.FromSeconds(2));
        programmer.SetTimeOut(0, 5, TimeSpan.FromSeconds(3));

        Assert.True(programmer.HasStoredValue(0, 5, out var value));
        Assert.Equal((byte)123, value);
    }

    [Fact]
    public void Timing_IsKeyedByUniverseAndChannel_UnrelatedChannelsUnaffected()
    {
        var programmer = new Programmer();
        programmer.SetTimeIn(0, 5, TimeSpan.FromSeconds(2));

        Assert.False(programmer.TryGetTiming(0, 6, out _, out _));
        Assert.False(programmer.TryGetTiming(1, 5, out _, out _));
    }

    [Fact]
    public void ClearAll_RemovesTimingToo()
    {
        var programmer = new Programmer();
        programmer.SetTimeIn(0, 5, TimeSpan.FromSeconds(2));
        programmer.ClearAll();

        Assert.False(programmer.TryGetTiming(0, 5, out _, out _));
    }

    [Fact]
    public void ClearChannel_DoesNotTouchTiming_AxisIndependencePreservedForUndo()
    {
        // ClearChannel backs ProgrammerChannelCommandBase's per-channel Undo restore for VALUE-only
        // commands (e.g. SetParameterValuesCommand) - it must never also wipe an independently-set
        // timing override on the same channel, or an AT's Undo would corrupt an unrelated TIME.
        var programmer = new Programmer();
        programmer.SetChannel(0, 5, 100);
        programmer.SetTimeIn(0, 5, TimeSpan.FromSeconds(2));

        programmer.ClearChannel(0, 5);

        Assert.False(programmer.HasStoredValue(0, 5, out _));
        Assert.True(programmer.TryGetTiming(0, 5, out var timeIn, out _));
        Assert.Equal(TimeSpan.FromSeconds(2), timeIn);
    }

    [Fact]
    public void SetTimingRaw_RemovesEntryEntirely_WhenBothSidesNull()
    {
        var programmer = new Programmer();
        programmer.SetTimeIn(0, 5, TimeSpan.FromSeconds(2));
        programmer.SetTimingRaw(0, 5, null, null);

        Assert.False(programmer.TryGetTiming(0, 5, out _, out _));
    }

    [Fact]
    public void SetTimingRaw_RestoresExactPreviousPair()
    {
        var programmer = new Programmer();
        programmer.SetTimingRaw(0, 5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        Assert.True(programmer.TryGetTiming(0, 5, out var timeIn, out var timeOut));
        Assert.Equal(TimeSpan.FromSeconds(1), timeIn);
        Assert.Equal(TimeSpan.FromSeconds(2), timeOut);
    }
}

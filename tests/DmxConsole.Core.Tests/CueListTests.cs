using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using Xunit;

namespace DmxConsole.Core.Tests;

public class CueListTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer",
        Manufacturer = "Test",
        Model = "Dimmer",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "1ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 20 },
                },
            },
        },
    };

    private static (Patch patch, PatchedFixture fixture) BuildPatch()
    {
        var patch = new Patch();
        var profile = Dimmer1();
        var fixture = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        patch.Add(fixture);
        return (patch, fixture);
    }

    [Fact]
    public void RecordCue_UsesProgrammerValue_WhenSet()
    {
        var (patch, fixture) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 200);

        var cueList = new CueList();
        var cue = cueList.RecordCue(patch, programmer, "Test", 1, TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(200, cue.Levels[(0, 0)]);
    }

    [Fact]
    public void RecordCue_FallsBackToFixtureDefault_WhenProgrammerUntouched()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();

        var cueList = new CueList();
        var cue = cueList.RecordCue(patch, programmer, "Test", 1, TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(20, cue.Levels[(0, 0)]); // fixture DefaultValue
    }

    [Fact]
    public void Go_WithZeroFadeTime_JumpsImmediatelyToTarget()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 150);

        var cueList = new CueList();
        cueList.RecordCue(patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();

        Assert.True(cueList.IsActive);
        Assert.True(cueList.TryGetChannelValue(0, 0, out var value));
        Assert.Equal(150, value);
    }

    [Fact]
    public void Go_PastLastCue_DoesNothing()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        cueList.RecordCue(patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);

        cueList.Go(); // -> cue 1
        cueList.Go(); // no cue 2, should stay put

        Assert.Equal(0, cueList.CurrentCueIndex);
    }

    [Fact]
    public void Back_BeforeFirstCue_DoesNothing()
    {
        var cueList = new CueList();
        cueList.Back();

        Assert.Equal(-1, cueList.CurrentCueIndex);
        Assert.False(cueList.IsActive);
    }

    [Fact]
    public void Stop_ReleasesLayer()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        cueList.RecordCue(patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();

        cueList.Stop();

        Assert.False(cueList.IsActive);
        Assert.False(cueList.TryGetChannelValue(0, 0, out _));
    }

    [Fact]
    public void Go_MidFade_InterpolatesBetweenFromAndTarget()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 100);

        var cueList = new CueList();
        // Cue 0: instantly at 100 (baseline for the real test below)
        cueList.RecordCue(patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();
        cueList.TryGetChannelValue(0, 0, out _); // settle _currentOutput at 100

        programmer.SetChannel(0, 0, 200);
        cueList.RecordCue(patch, programmer, "Cue 2", 2, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300));
        cueList.Go();

        Thread.Sleep(100);
        Assert.True(cueList.TryGetChannelValue(0, 0, out var mid));
        Assert.InRange(mid, 101, 199); // partway between 100 and 200, not yet settled
    }
}

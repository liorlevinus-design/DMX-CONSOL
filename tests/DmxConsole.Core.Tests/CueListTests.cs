using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Core.Tests;

public class CueListTests
{
    /// <summary>Always returns 0 - fine for every test here, since none of them use a
    /// CueStoreFilter that actually reads effectiveOutput (AllStage, the default/only filter
    /// used below, never consults it - see CueList.BuildCue).</summary>
    private sealed class StubEffectiveOutputReader : IEffectiveOutputReader
    {
        public byte GetEffectiveValue(int universeId, int channelIndex) => 0;
        public OutputOwner? GetOwner(int universeId, int channelIndex) => null;
    }

    private static readonly FixtureSelection EmptySelection = new();
    private static readonly IEffectiveOutputReader Stub = new StubEffectiveOutputReader();

    private static Cue Record(CueList cueList, Patch patch, Programmer programmer, string name, double number,
        TimeSpan timeIn, TimeSpan timeOut) =>
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, name, number,
            new CueStoreOptions(new CueTiming(timeIn, timeOut, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));

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
        var cue = Record(cueList, patch, programmer, "Test", 1, TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(200, cue.Levels[(0, 0)].AbsoluteValue);
    }

    [Fact]
    public void RecordCue_FallsBackToFixtureDefault_WhenProgrammerUntouched()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();

        var cueList = new CueList();
        var cue = Record(cueList, patch, programmer, "Test", 1, TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(20, cue.Levels[(0, 0)].AbsoluteValue); // fixture DefaultValue
    }

    [Fact]
    public void Go_WithZeroFadeTime_JumpsImmediatelyToTarget()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 150);

        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
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
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);

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
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
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
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();
        cueList.TryGetChannelValue(0, 0, out _); // settle _currentOutput at 100

        programmer.SetChannel(0, 0, 200);
        Record(cueList, patch, programmer, "Cue 2", 2, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300));
        cueList.Go();

        Thread.Sleep(100);
        Assert.True(cueList.TryGetChannelValue(0, 0, out var mid));
        Assert.InRange(mid, 101, 199); // partway between 100 and 200, not yet settled
    }

    [Fact]
    public void Pause_FreezesInterpolatedValue_Resume_ContinuesFromThatPoint()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 0);

        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();
        cueList.TryGetChannelValue(0, 0, out _); // settle at 0

        programmer.SetChannel(0, 0, 200);
        Record(cueList, patch, programmer, "Cue 2", 2, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(400));
        cueList.Go();

        Thread.Sleep(100);
        cueList.Pause();
        Assert.True(cueList.IsPaused);
        cueList.TryGetChannelValue(0, 0, out var frozen1);

        Thread.Sleep(100); // time passes while paused - must not affect the frozen value
        cueList.TryGetChannelValue(0, 0, out var frozen2);
        Assert.Equal(frozen1, frozen2);

        cueList.Resume();
        Assert.False(cueList.IsPaused);
        Thread.Sleep(50);
        cueList.TryGetChannelValue(0, 0, out var afterResume);
        Assert.True(afterResume >= frozen1); // continued climbing from where it left off, not from 0 again
    }

    [Fact]
    public void GetStatus_MidFade_ReportsTimeFirstProgress()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 200);

        var cueList = new CueList();
        var cue = Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        cueList.Go();

        var status = Assert.IsType<CueListPlaybackStatus>(cueList.GetStatus());
        Assert.Equal(cue, status.CurrentCue);
        Assert.True(status.IsRunning);
        Assert.False(status.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(1), status.Overall.Total);
        Assert.True(status.Overall.Remaining <= TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), status.FadeIn.Total);
        Assert.Equal(TimeSpan.FromSeconds(1), status.FadeOut.Total);
    }

    [Fact]
    public void TryGetRevision_BumpsOnGo_SameForEveryChannelInThatCue()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();

        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();

        Assert.True(cueList.TryGetRevision(0, 0, out var revision));
        Assert.True(revision > 0);
        Assert.False(cueList.TryGetRevision(0, 99, out _)); // never-patched channel
    }

    [Fact]
    public void FindByNumber_ReturnsMatchingCue_OrNull()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        var cue = Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);

        Assert.Same(cue, cueList.FindByNumber(1));
        Assert.Null(cueList.FindByNumber(2));
    }

    [Fact]
    public void UpdateCue_ReplacesLevelsAndName_KeepsSameNumberAndListPosition()
    {
        var (patch, fixture) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 0", 0, TimeSpan.Zero, TimeSpan.Zero);
        var original = Record(cueList, patch, programmer, "Original", 1, TimeSpan.Zero, TimeSpan.Zero);
        Record(cueList, patch, programmer, "Cue 2", 2, TimeSpan.Zero, TimeSpan.Zero);

        programmer.SetChannel(0, 0, 222);
        var options = new CueStoreOptions(new CueTiming(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.Zero, TimeSpan.Zero),
            CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage);
        var updated = cueList.UpdateCue(original, patch, programmer, EmptySelection, Stub, "Renamed", options);

        Assert.NotNull(updated);
        Assert.Equal(1, updated!.Number);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(222, updated.Levels[(0, 0)].AbsoluteValue);
        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, cueList.Cues.Select(c => c.Number)); // position/order preserved
        Assert.Same(updated, cueList.FindByNumber(1));
    }

    [Fact]
    public void UpdateCue_ReturnsNull_WhenCueNoLongerInList()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        var cue = Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.RemoveCue(cue);

        var result = cueList.UpdateCue(cue, patch, programmer, EmptySelection, Stub, "New Name", CueStoreOptions.Default);

        Assert.Null(result);
    }

    [Fact]
    public void UpdateCue_OnCurrentlyActiveCue_UpdatesTheLivePointer_SoGoToCueStillWorks()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        var cue = Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go(); // cue becomes _currentCue

        var updated = cueList.UpdateCue(cue, patch, programmer, EmptySelection, Stub, "Cue 1 renamed", CueStoreOptions.Default);

        Assert.NotNull(updated);
        Assert.Equal("Cue 1 renamed", cueList.CurrentCue!.Name);
    }

    [Fact]
    public void SetTriggerMode_OnCueStillInList_UpdatesModeAndReturnsTrue()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        var cue = Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);

        Assert.True(cueList.SetTriggerMode(cue, CueTriggerMode.Follow));
        Assert.Equal(CueTriggerMode.Follow, cue.TriggerMode);
    }

    [Fact]
    public void SetTriggerMode_OnRemovedCue_ReturnsFalse_DoesNotThrow()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        var cue = Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.RemoveCue(cue);

        Assert.False(cueList.SetTriggerMode(cue, CueTriggerMode.Follow));
    }

    [Fact]
    public void DelayIn_HoldsChannelAtFromValue_UntilDelayElapses_ThenFadesOverTimeIn()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 0);

        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();
        cueList.TryGetChannelValue(0, 0, out _); // settle at 0

        programmer.SetChannel(0, 0, 200);
        var options = new CueStoreOptions(new CueTiming(TimeSpan.FromMilliseconds(200), TimeSpan.Zero, TimeSpan.FromMilliseconds(150), TimeSpan.Zero),
            CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage);
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 2", 2, options);
        cueList.Go();

        Thread.Sleep(50); // still within the 150ms DelayIn
        Assert.True(cueList.TryGetChannelValue(0, 0, out var duringDelay));
        Assert.Equal(0, duringDelay); // frozen at "from" - delay hasn't elapsed yet

        Thread.Sleep(400); // comfortably past delay (150ms) + time (200ms) = 350ms total
        Assert.True(cueList.TryGetChannelValue(0, 0, out var afterFade));
        Assert.Equal(200, afterFade);
    }

    [Fact]
    public void Follow_AutoAdvances_OnceWaitTimeElapses_NotBefore_NotTwice()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();

        var cueList = new CueList();
        var followOptions = new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
            CueTriggerMode.Follow, TimeSpan.FromMilliseconds(80), CueStoreFilter.AllStage);
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 1", 1, followOptions);
        Record(cueList, patch, programmer, "Cue 2", 2, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go(); // -> Cue 1

        cueList.Tick(TimeSpan.Zero); // well before WaitTime
        Assert.Equal(0, cueList.CurrentCueIndex); // still on Cue 1

        Thread.Sleep(120);
        cueList.Tick(TimeSpan.Zero);
        Assert.Equal(1, cueList.CurrentCueIndex); // auto-advanced to Cue 2

        cueList.Tick(TimeSpan.Zero); // must not advance again (no Cue 3, and Cue 2 is Manual anyway)
        Assert.Equal(1, cueList.CurrentCueIndex);
    }

    [Fact]
    public void Manual_NeverAutoAdvances_EvenAfterLongTick()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();

        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero); // Manual by default
        Record(cueList, patch, programmer, "Cue 2", 2, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();

        Thread.Sleep(50);
        cueList.Tick(TimeSpan.Zero);

        Assert.Equal(0, cueList.CurrentCueIndex); // still on Cue 1 - nothing auto-advanced it
    }

    [Fact]
    public void ReferencedAddresses_UnionsEveryCuesLevels_NoDuplicates()
    {
        var (patch, fixture) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        Record(cueList, patch, programmer, "Cue 2", 2, TimeSpan.Zero, TimeSpan.Zero);

        var addresses = cueList.ReferencedAddresses();

        Assert.Contains((fixture.UniverseId, fixture.AbsoluteIndex(fixture.Mode.Channels[0])), addresses);
        Assert.Single(addresses); // both cues store the same single dimmer address - union, not sum
    }

    [Fact]
    public void ReferencedAddresses_EmptyCueList_ReturnsEmptySet()
    {
        var cueList = new CueList();
        Assert.Empty(cueList.ReferencedAddresses());
    }
}

using System.Linq;
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

        Assert.True(cueList.SetTriggerMode(cue, CueTriggerMode.AutoFollow));
        Assert.Equal(CueTriggerMode.AutoFollow, cue.TriggerMode);
    }

    [Fact]
    public void SetTriggerMode_OnRemovedCue_ReturnsFalse_DoesNotThrow()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        var cue = Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.RemoveCue(cue);

        Assert.False(cueList.SetTriggerMode(cue, CueTriggerMode.AutoFollow));
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

    /// <summary>AutoFollow correction slice: the Trigger that makes an automatic advance happen
    /// belongs to the cue being ENTERED (Cue 2), not the cue currently playing (Cue 1) - Cue 1 is
    /// left Manual (its default, and irrelevant to its own advancement either way).</summary>
    [Fact]
    public void Wait_AutoAdvances_OnceWaitTimeElapses_NotBefore_NotTwice()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();

        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero); // Manual
        var waitOptions = new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
            CueTriggerMode.Wait, TimeSpan.FromMilliseconds(80), CueStoreFilter.AllStage);
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 2", 2, waitOptions); // WAIT 80ms
        cueList.Go(); // -> Cue 1

        cueList.Tick(TimeSpan.Zero); // well before WaitTime
        Assert.Equal(0, cueList.CurrentCueIndex); // still on Cue 1

        Thread.Sleep(120);
        cueList.Tick(TimeSpan.Zero);
        Assert.Equal(1, cueList.CurrentCueIndex); // auto-advanced to Cue 2

        cueList.Tick(TimeSpan.Zero); // must not advance again (no Cue 3)
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

    // ---------- AutoFollow/Wait correction slice: the Trigger belongs to the TARGET cue (the one
    // being entered) and describes how it's entered once the PREVIOUS cue fully completes. It must
    // only fire on forward playback progression (GO, or a chained automatic advance), never merely
    // because a cue became current via BACK/GO TO. ----------

    private static CueStoreOptions WaitOptions(TimeSpan waitTime) =>
        new(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero), CueTriggerMode.Wait, waitTime, CueStoreFilter.AllStage);

    /// <summary>A 4-cue rig: 1 (Manual), 2 (Manual), 3 (Manual), 4 (WAIT cue4WaitTime) - the
    /// trigger that makes entry into Cue 4 automatic belongs to Cue 4 itself (the TARGET cue),
    /// per the corrected model. Renamed from the old rig, which put the trigger on Cue 3 (the cue
    /// being LEFT) - exactly the backwards model this slice corrects.</summary>
    private static (Patch patch, Programmer programmer, CueList cueList) BuildFourCueRig(TimeSpan cue4WaitTime)
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        Record(cueList, patch, programmer, "Cue 2", 2, TimeSpan.Zero, TimeSpan.Zero);
        Record(cueList, patch, programmer, "Cue 3", 3, TimeSpan.Zero, TimeSpan.Zero);
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 4", 4, WaitOptions(cue4WaitTime)); // WAIT
        return (patch, programmer, cueList);
    }

    [Fact]
    public void Go_ThroughChain_ThenAutoAdvances_IntoWaitCue()
    {
        var (_, _, cueList) = BuildFourCueRig(TimeSpan.FromMilliseconds(50));
        cueList.Go(); cueList.Go(); cueList.Go(); // 1 -> 2 -> 3 (explicit Go each time)
        Assert.Equal(2, cueList.CurrentCueIndex); // on Cue 3

        cueList.Tick(TimeSpan.Zero); // anchor the completion instant right away (Cue 3 has zero timing)
        Thread.Sleep(90);
        cueList.Tick(TimeSpan.Zero);

        Assert.Equal(3, cueList.CurrentCueIndex); // auto-advanced into Cue 4 (WAIT) once Cue 3 completed + WaitTime elapsed
    }

    [Fact]
    public void WaitChain_ContinuesCorrectly_AcrossConsecutiveWaitCues()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        var wait = TimeSpan.FromMilliseconds(50);
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero); // Manual
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 2", 2, WaitOptions(wait)); // entering Cue 2 is automatic
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 3", 3, WaitOptions(wait)); // entering Cue 3 is automatic

        cueList.Go(); // -> Cue 1 (explicit Go, arms the chain)
        Assert.Equal(0, cueList.CurrentCueIndex);

        cueList.Tick(TimeSpan.Zero); // anchor the completion instant right away (Cue 1 has zero timing)
        Thread.Sleep(90);
        cueList.Tick(TimeSpan.Zero); // Cue 1 completes -> Cue 2's own WAIT fires -> chained Go re-arms the chain
        Assert.Equal(1, cueList.CurrentCueIndex);

        cueList.Tick(TimeSpan.Zero); // anchor again for Cue 2's own completion
        Thread.Sleep(90);
        cueList.Tick(TimeSpan.Zero); // Cue 2 completes -> Cue 3's own WAIT fires
        Assert.Equal(2, cueList.CurrentCueIndex);

        cueList.Tick(TimeSpan.Zero);
        Thread.Sleep(90);
        cueList.Tick(TimeSpan.Zero); // Cue 3 is last - chain correctly stops (no Cue 4 to consult)
        Assert.Equal(2, cueList.CurrentCueIndex);
    }

    /// <summary>Navigation must never seed an automatic chain: BACK from Cue 4 (WAIT) into Cue 3
    /// must land on Cue 3 and HOLD, never auto-re-advance forward into Cue 4.</summary>
    [Fact]
    public void Back_IntoCuePrecedingWaitCue_Holds_NeverReAdvances()
    {
        var (_, _, cueList) = BuildFourCueRig(TimeSpan.FromMilliseconds(50));
        cueList.Go(); cueList.Go(); cueList.Go(); // -> Cue 3
        cueList.Tick(TimeSpan.Zero); // anchor the completion instant right away (Cue 3 has zero timing)
        Thread.Sleep(90);
        cueList.Tick(TimeSpan.Zero); // legitimate forward auto-advance: Cue 3 -> Cue 4
        Assert.Equal(3, cueList.CurrentCueIndex);

        cueList.Back(); // BACK from Cue 4 into Cue 3
        Assert.Equal(2, cueList.CurrentCueIndex);

        Thread.Sleep(90);
        cueList.Tick(TimeSpan.Zero);

        Assert.Equal(2, cueList.CurrentCueIndex); // still on Cue 3 - did NOT re-advance to Cue 4
    }

    /// <summary>No duplicate timers/stale scheduling: repeated ticks after a BACK into the cue
    /// preceding a WAIT cue must never advance, not just on the first tick.</summary>
    [Fact]
    public void Back_IntoCuePrecedingWaitCue_HoldsAcrossManyRepeatedTicks_NoStaleScheduling()
    {
        var (_, _, cueList) = BuildFourCueRig(TimeSpan.FromMilliseconds(30));
        cueList.Go(); cueList.Go(); cueList.Go(); cueList.Go(); // walk all the way to the end (Cue 4), no auto-advance needed
        Assert.Equal(3, cueList.CurrentCueIndex);

        cueList.Back(); // -> Cue 3
        Assert.Equal(2, cueList.CurrentCueIndex);

        for (int i = 0; i < 10; i++)
        {
            Thread.Sleep(40);
            cueList.Tick(TimeSpan.Zero);
            Assert.Equal(2, cueList.CurrentCueIndex); // never advances, on any tick
        }
    }

    [Fact]
    public void GoToCue_IntoCuePrecedingWaitCue_Holds_NeverArmsChain()
    {
        var (_, _, cueList) = BuildFourCueRig(TimeSpan.FromMilliseconds(50));
        cueList.Go(); // -> Cue 1
        var cue3 = cueList.Cues.Single(c => c.Number == 3);

        cueList.GoToCue(cue3); // direct jump, not progression
        Assert.Equal(2, cueList.CurrentCueIndex);

        Thread.Sleep(90);
        cueList.Tick(TimeSpan.Zero);

        Assert.Equal(2, cueList.CurrentCueIndex); // held - GO TO never arms the chain
    }

    [Fact]
    public void GoAfterGoToCue_ResumesNormalForwardPlayback_AndCanArmChainAgain()
    {
        var (_, _, cueList) = BuildFourCueRig(TimeSpan.FromMilliseconds(50));
        var cue3 = cueList.Cues.Single(c => c.Number == 3);
        cueList.GoToCue(cue3); // jump straight to Cue 3, holds
        Assert.Equal(2, cueList.CurrentCueIndex);

        cueList.Go(); // operator presses GO - normal forward progression resumes
        Assert.Equal(3, cueList.CurrentCueIndex); // -> Cue 4
    }

    [Fact]
    public void NonAutoFollowBack_BehaviorUnchanged()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        Record(cueList, patch, programmer, "Cue 2", 2, TimeSpan.Zero, TimeSpan.Zero); // Manual
        cueList.Go(); cueList.Go();
        Assert.Equal(1, cueList.CurrentCueIndex);

        cueList.Back();
        Assert.Equal(0, cueList.CurrentCueIndex);

        Thread.Sleep(50);
        cueList.Tick(TimeSpan.Zero);
        Assert.Equal(0, cueList.CurrentCueIndex); // unchanged - never auto-advanced (was never AutoFollow anyway)
    }

    /// <summary>EDIT (LoadForEdit at the ViewModel layer) never touches CueList/playback at all -
    /// documented here at the Core layer as "nothing to prove", since Core has no notion of an
    /// edit-form. The real guarantee is structural: CueListViewModel.LoadForEdit only assigns to
    /// its own form-field properties, never calls CueList.Go/Back/GoToCue - see that method's own
    /// code. This test instead proves the CueList-level invariant EDIT relies on: simply reading a
    /// Cue's data (Number/Name/Timing/TriggerMode) never mutates CurrentCueIndex or arms a chain.</summary>
    [Fact]
    public void ReadingACuesData_NeverChangesCurrentCueOrArmsChain()
    {
        var (_, _, cueList) = BuildFourCueRig(TimeSpan.FromMilliseconds(30));
        cueList.Go(); // -> Cue 1
        var cue3 = cueList.Cues.Single(c => c.Number == 3);
        _ = cue3.Number; _ = cue3.Name; _ = cue3.Timing; _ = cue3.TriggerMode; _ = cue3.WaitTime; // "LOAD CUE 3" / "EDIT CUE 3" - read-only

        Assert.Equal(0, cueList.CurrentCueIndex); // still on Cue 1 - reading Cue 3's data never made it current

        Thread.Sleep(60);
        cueList.Tick(TimeSpan.Zero);
        Assert.Equal(0, cueList.CurrentCueIndex); // Cue 2 is Manual by default - nothing auto-advanced
    }

    [Fact]
    public void AutoAdvanceTransitions_NeverTouchSelectionOrProgrammer()
    {
        var (patch, programmer, cueList) = BuildFourCueRig(TimeSpan.FromMilliseconds(50));
        var selection = EmptySelection;
        var fixture = patch.Fixtures.Single();
        int dimmerIndex = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Dimmer)!);
        programmer.SetChannel(fixture.UniverseId, dimmerIndex, 111); // an unrelated Programmer value

        cueList.Go(); cueList.Go(); cueList.Go(); // -> Cue 3
        cueList.Tick(TimeSpan.Zero); // anchor the completion instant right away (Cue 3 has zero timing)
        Thread.Sleep(90);
        cueList.Tick(TimeSpan.Zero); // Cue 3 -> Cue 4 (legitimate auto-advance into the WAIT cue)
        cueList.Back(); // -> Cue 3 again (holds)
        Thread.Sleep(90);
        cueList.Tick(TimeSpan.Zero);

        // Selection was never touched by CueList at all (it's a Core.Selection concern CueList
        // doesn't even hold a reference to beyond RecordCue's own read), and the Programmer value
        // set above is completely unrelated to any of this playback traffic.
        Assert.Empty(selection.Items);
        Assert.True(programmer.HasStoredValue(fixture.UniverseId, dimmerIndex, out var stillThere));
        Assert.Equal(111, stillThere);
    }

    private static CueStoreOptions AutoFollowOptions() =>
        new(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero), CueTriggerMode.AutoFollow, TimeSpan.Zero, CueStoreFilter.AllStage);

    /// <summary>The exact chain from the authoritative spec: Cue 1 = Manual, Cue 2 = AutoFollow,
    /// Cue 3 = Wait, Cue 4 = AutoFollow. A single initial GO must drive the whole chain to
    /// completion, each cue's own Trigger governing entry into itself.</summary>
    [Fact]
    public void FullChain_ManualThenAutoFollowThenWaitThenAutoFollow_AdvancesEndToEndFromOneGo()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        var wait = TimeSpan.FromMilliseconds(60);
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero); // Manual
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 2", 2, AutoFollowOptions());
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 3", 3, WaitOptions(wait));
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 4", 4, AutoFollowOptions());

        cueList.Go(); // the ONLY explicit GO in this whole test
        Assert.Equal(0, cueList.CurrentCueIndex); // Cue 1

        cueList.Tick(TimeSpan.Zero); // Cue 1 completes instantly (zero timing) -> Cue 2's AutoFollow fires
        Assert.Equal(1, cueList.CurrentCueIndex); // Cue 2

        cueList.Tick(TimeSpan.Zero); // anchor Cue 2's own completion instant
        Thread.Sleep(90);
        cueList.Tick(TimeSpan.Zero); // Cue 2 completes -> Cue 3's WAIT (60ms) has elapsed -> advance
        Assert.Equal(2, cueList.CurrentCueIndex); // Cue 3

        cueList.Tick(TimeSpan.Zero); // Cue 3 completes instantly -> Cue 4's AutoFollow fires
        Assert.Equal(3, cueList.CurrentCueIndex); // Cue 4
    }

    /// <summary>The WAIT timer must start only AFTER the preceding cue's own fade has fully
    /// completed - never running concurrently with it. Cue 1 has a real 200ms fade; Cue 2 is
    /// WAIT 80ms. Under the old (wrong) model, WaitTime shared the same clock as the fade, so it
    /// would have elapsed and fired well before the fade even finished.</summary>
    [Fact]
    public void Wait_NeverOverlapsPrecedingCuesOwnFade_TimerStartsOnlyAfterFadeCompletes()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();

        var fadeOptions = new CueStoreOptions(new CueTiming(TimeSpan.FromMilliseconds(200), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
            CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage);
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 1", 1, fadeOptions); // 200ms fade-in
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 2", 2, WaitOptions(TimeSpan.FromMilliseconds(80)));

        cueList.Go(); // -> Cue 1, fade starts
        cueList.Tick(TimeSpan.Zero); // fade not complete yet - no anchor set, no advance

        Thread.Sleep(150); // still mid-fade (150ms < 200ms fade)
        cueList.Tick(TimeSpan.Zero);
        Assert.Equal(0, cueList.CurrentCueIndex); // fade not complete - WAIT timer must not have started, let alone elapsed

        Thread.Sleep(100); // now past the 200ms fade (~250ms total), but only ~100ms since fade-completion -
        cueList.Tick(TimeSpan.Zero); // this tick anchors the WaitTime timer NOW, not earlier
        Assert.Equal(0, cueList.CurrentCueIndex); // WaitTime (80ms) hasn't elapsed since this anchor yet -
                                                    // if WAIT had shared the fade's clock (old bug), 80ms would
                                                    // already have elapsed well before the 200ms fade even finished

        Thread.Sleep(100); // comfortably past 80ms since the anchor
        cueList.Tick(TimeSpan.Zero);
        Assert.Equal(1, cueList.CurrentCueIndex); // now advances - WAIT was measured from fade completion, not from Go()
    }

    /// <summary>AutoFollow ignores WaitTime entirely, per the authoritative spec - a cue with
    /// TriggerMode=AutoFollow and a large WaitTime still fires on the very next tick after the
    /// preceding cue completes, never waiting for that WaitTime.</summary>
    [Fact]
    public void AutoFollow_IgnoresWaitTime_FiresImmediately_EvenWithLargeWaitTimeSet()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();

        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero); // Manual
        var options = new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
            CueTriggerMode.AutoFollow, TimeSpan.FromSeconds(30), CueStoreFilter.AllStage); // AutoFollow with a large WaitTime that must be ignored
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 2", 2, options);

        cueList.Go(); // -> Cue 1
        cueList.Tick(TimeSpan.Zero); // AutoFollow fires on the very next tick - the 30s WaitTime is never consulted

        Assert.Equal(1, cueList.CurrentCueIndex);
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

    // ---------- SHIFT+GO/SHIFT+BACK slice: instant, zero-time cue navigation. ----------

    /// <summary>Two cues with a REAL fade duration (2s TimeIn/TimeOut) and DISTINCT recorded
    /// values, so instant-vs-timed is meaningfully distinguishable (a zero-duration cue can't
    /// prove anything about whether fade math was actually skipped).</summary>
    private static (Patch patch, PatchedFixture fixture, CueList cueList) BuildTwoDistinctCuesRig()
    {
        var (patch, fixture) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();

        programmer.SetChannel(0, 0, 60);
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 1", 1,
            new CueStoreOptions(new CueTiming(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));

        programmer.SetChannel(0, 0, 220);
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 2", 2,
            new CueStoreOptions(new CueTiming(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));

        return (patch, fixture, cueList);
    }

    [Fact]
    public void ShiftGo_FromCue1ToCue2_ReachesTargetImmediately_NoTransitionRemainsActive_ValueFullyApplied()
    {
        var (_, _, cueList) = BuildTwoDistinctCuesRig();
        cueList.Go(instant: true); // -> Cue 1, instant (establishes a fully-settled starting point)
        Assert.True(cueList.TryGetChannelValue(0, 0, out var afterGo));
        Assert.Equal(60, afterGo); // Cue 1's own value, fully settled immediately

        cueList.Go(instant: true); // SHIFT+GO -> Cue 2, zero-time

        Assert.Equal(1, cueList.CurrentCueIndex); // reached Cue 2 immediately (same index Go() would reach)
        Assert.True(cueList.TryGetChannelValue(0, 0, out var value));
        Assert.Equal(220, value); // Cue 2's target value, fully applied - NOT interpolating from 60

        var (progress, remaining) = cueList.GetTransitionStatus();
        Assert.Equal(1.0, progress); // no transition remains active
        Assert.Equal(TimeSpan.Zero, remaining);

        var cueStatus = Assert.IsType<CueListPlaybackStatus>(cueList.GetStatus());
        Assert.Equal(TimeSpan.Zero, cueStatus.Overall.Remaining);
        Assert.Equal(cueStatus.Overall.Total, cueStatus.Overall.Elapsed); // fully complete
    }

    [Fact]
    public void ShiftBack_FromCue2ToCue1_ReachesTargetImmediately_NoTransitionRemainsActive()
    {
        var (_, _, cueList) = BuildTwoDistinctCuesRig();
        cueList.Go(); cueList.Go(); // -> Cue 2 (normal)
        Assert.Equal(1, cueList.CurrentCueIndex);

        cueList.Back(instant: true); // SHIFT+BACK -> Cue 1, zero-time

        Assert.Equal(0, cueList.CurrentCueIndex);
        Assert.True(cueList.TryGetChannelValue(0, 0, out var value));
        Assert.Equal(60, value); // Cue 1's target value, fully applied immediately

        var (progress, remaining) = cueList.GetTransitionStatus();
        Assert.Equal(1.0, progress);
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void NormalGo_StillUsesCueTiming_NeverInstant()
    {
        var (_, _, cueList) = BuildTwoDistinctCuesRig();
        cueList.Go(); // -> Cue 1
        cueList.Go(); // normal GO -> Cue 2 (2s fade)

        // Read immediately - a normal (non-instant) transition must NOT already be at the target;
        // it must still be interpolating from Cue 1's value (60) toward Cue 2's (220).
        Assert.True(cueList.TryGetChannelValue(0, 0, out var immediateValue));
        Assert.NotEqual(220, immediateValue);

        var (progress, _) = cueList.GetTransitionStatus();
        Assert.True(progress < 1.0); // transition still active
    }

    [Fact]
    public void NormalBack_BehaviorUnchanged_StillUsesCueTiming()
    {
        var (_, _, cueList) = BuildTwoDistinctCuesRig();
        cueList.Go(); cueList.Go(); // -> Cue 2
        cueList.Back(); // normal BACK -> Cue 1 (2s fade)

        Assert.True(cueList.TryGetChannelValue(0, 0, out var immediateValue));
        Assert.NotEqual(60, immediateValue); // not yet fully faded to Cue 1's target

        var (progress, _) = cueList.GetTransitionStatus();
        Assert.True(progress < 1.0);
    }

    [Fact]
    public void ShiftGo_LandingOnCuePrecedingWaitCue_DoesNotArmChain()
    {
        var (_, _, cueList) = BuildFourCueRig(TimeSpan.FromMilliseconds(30));
        cueList.Go(); cueList.Go(); // -> Cue 2

        cueList.Go(instant: true); // SHIFT+GO -> Cue 3, zero-time
        Assert.Equal(2, cueList.CurrentCueIndex);

        Thread.Sleep(60);
        cueList.Tick(TimeSpan.Zero);

        Assert.Equal(2, cueList.CurrentCueIndex); // held - SHIFT+GO never arms the chain
    }

    [Fact]
    public void ShiftBack_LandingOnCuePrecedingWaitCue_DoesNotArmChain()
    {
        var (_, _, cueList) = BuildFourCueRig(TimeSpan.FromMilliseconds(30));
        cueList.Go(); cueList.Go(); cueList.Go(); cueList.Go(); // walk to Cue 4 without relying on auto-advance
        Assert.Equal(3, cueList.CurrentCueIndex);

        cueList.Back(instant: true); // SHIFT+BACK -> Cue 3, zero-time
        Assert.Equal(2, cueList.CurrentCueIndex);

        Thread.Sleep(60);
        cueList.Tick(TimeSpan.Zero);

        Assert.Equal(2, cueList.CurrentCueIndex); // held - SHIFT+BACK never arms the chain
    }

    [Fact]
    public void GoAfterShiftNavigation_ResumesNormalTimedForwardPlayback()
    {
        var (_, _, cueList) = BuildTwoDistinctCuesRig();
        cueList.Go(instant: true); // SHIFT+GO straight onto Cue 1 (from nothing playing)
        Assert.Equal(0, cueList.CurrentCueIndex);
        var (progress, _) = cueList.GetTransitionStatus();
        Assert.Equal(1.0, progress); // instant, already complete

        cueList.Go(); // normal GO -> Cue 2, must use normal (2s) timing again, not stay "instant"
        Assert.True(cueList.TryGetChannelValue(0, 0, out var immediateValue));
        Assert.NotEqual(220, immediateValue); // still fading - instant mode did not "stick" past its own transition

        var (progressAfterNormalGo, _) = cueList.GetTransitionStatus();
        Assert.True(progressAfterNormalGo < 1.0);
    }

    [Fact]
    public void ShiftNavigation_NeverTouchesSelectionOrProgrammer()
    {
        var (patch, fixture, cueList) = BuildTwoDistinctCuesRig();
        var programmer = new Programmer();
        int dimmerIndex = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Dimmer)!);
        programmer.SetChannel(fixture.UniverseId, dimmerIndex, 77); // unrelated Programmer value
        var selection = EmptySelection;

        cueList.Go(); // -> Cue 1
        cueList.Go(instant: true); // SHIFT+GO -> Cue 2
        cueList.Back(instant: true); // SHIFT+BACK -> Cue 1

        Assert.Empty(selection.Items);
        Assert.True(programmer.HasStoredValue(fixture.UniverseId, dimmerIndex, out var stillThere));
        Assert.Equal(77, stillThere); // completely unrelated Programmer value, untouched
    }

    /// <summary>No stale timers/delayed transitions left behind: after an instant jump, repeated
    /// reads and ticks must keep reporting the exact same settled value/state - not drift, not
    /// resume some leftover fade, not re-trigger anything.</summary>
    [Fact]
    public void ShiftGo_LeavesNoStaleTimersOrDelayedTransition_AcrossRepeatedReadsAndTicks()
    {
        var (_, _, cueList) = BuildTwoDistinctCuesRig();
        cueList.Go(); // -> Cue 1
        cueList.Go(instant: true); // SHIFT+GO -> Cue 2

        for (int i = 0; i < 5; i++)
        {
            Thread.Sleep(20);
            cueList.Tick(TimeSpan.Zero);
            Assert.True(cueList.TryGetChannelValue(0, 0, out var value));
            Assert.Equal(220, value); // stays exactly at target, every time
            Assert.Equal(1, cueList.CurrentCueIndex); // never advances/changes on its own
        }
    }

    // ---------- Cue-timing slice: CueList.SetTiming actually drives playback ----------

    /// <summary>Command Surface acceptance criterion #6: a Cue's In/Out timing edited via
    /// SetTiming (what SetCueTimingCommand calls) is what GO/fade playback actually reads - not a
    /// second, unused field. Records a Cue with a long original fade, GOes to it (so it starts
    /// fading), overwrites its timing to a much shorter one mid-flight via SetTiming, then asserts
    /// the fade completes on the NEW duration, not the original one.</summary>
    [Fact]
    public void SetTiming_ChangesTheDurationTheActiveFadeActuallyUses()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 0);

        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();
        cueList.TryGetChannelValue(0, 0, out _); // settle at 0

        programmer.SetChannel(0, 0, 200);
        // Recorded with a long 10-minute fade - if SetTiming's new value were never actually
        // consumed, the fade below would still be sitting at ~0% after only 150ms.
        var cue2 = Record(cueList, patch, programmer, "Cue 2", 2, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        cueList.Go();

        // Immediately shorten the still-running transition's timing to 100ms via the exact same
        // API SetCueTimingCommand uses (CLAUDE.md §3/§4 - Application layer, but this Core test
        // exercises CueList.SetTiming directly, the one place the mutation actually lands).
        Assert.True(cueList.SetTiming(cue2, new CueTiming(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), TimeSpan.Zero, TimeSpan.Zero)));

        Thread.Sleep(150);
        Assert.True(cueList.TryGetChannelValue(0, 0, out var value));
        Assert.Equal(200, value); // fully settled on the NEW 100ms timing, not still ~0% of the original 10-minute one
    }

    /// <summary>SetTiming never touches TriggerMode/WaitTime (Cue Trigger Semantics, CLAUDE.md
    /// §9, are a completely separate concern from In/Out fade timing) and is a no-op returning
    /// false for a Cue no longer in the list - same shape as SetTriggerMode.</summary>
    [Fact]
    public void SetTiming_NeverTouchesTriggerModeOrWaitTime_AndNoOpsForARemovedCue()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();
        var cue = Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.SetTriggerMode(cue, CueTriggerMode.Wait);
        cue.WaitTime = TimeSpan.FromSeconds(7);

        bool applied = cueList.SetTiming(cue, new CueTiming(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), TimeSpan.Zero, TimeSpan.Zero));

        Assert.True(applied);
        Assert.Equal(CueTriggerMode.Wait, cue.TriggerMode);
        Assert.Equal(TimeSpan.FromSeconds(7), cue.WaitTime);
        Assert.Equal(TimeSpan.FromSeconds(3), cue.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(4), cue.Timing.TimeOut);

        cueList.RemoveCue(cue);
        Assert.False(cueList.SetTiming(cue, CueTiming.Default));
    }
}

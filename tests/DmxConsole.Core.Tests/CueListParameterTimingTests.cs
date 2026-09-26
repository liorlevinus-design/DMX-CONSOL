using System.Linq;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Core.Tests;

/// <summary>
/// Parameter TIME slice (CLAUDE.md §16/ROADMAP §9a) - the STORE and PLAYBACK half: capturing a
/// Programmer per-channel TimeIn/TimeOut override into a stored Cue's CueValue entries
/// (CueList.RecordCue/BuildCue), and Cue playback (TryGetChannelValue/CurrentTransitionHasCompletedUnlocked/
/// GetTransitionStatus) honoring a per-channel override over the Cue's own flat CueTiming, INCLUDING
/// correct completion/trigger detection (AutoFollow/Wait must wait for the true longest active
/// transition, never just the Cue-level baseline) once an override makes some channel slower.
/// Mirrors CueListTests.cs's own helpers/conventions.
/// </summary>
public class CueListParameterTimingTests
{
    private sealed class StubEffectiveOutputReader : IEffectiveOutputReader
    {
        public byte GetEffectiveValue(int universeId, int channelIndex) => 0;
        public OutputOwner? GetOwner(int universeId, int channelIndex) => null;
    }

    private static readonly FixtureSelection EmptySelection = new();
    private static readonly IEffectiveOutputReader Stub = new StubEffectiveOutputReader();

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
                Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 0 } },
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

    private static (Patch patch, PatchedFixture fixtureA, PatchedFixture fixtureB) BuildTwoFixturePatch()
    {
        var patch = new Patch();
        var profile = Dimmer1();
        var a = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        var b = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 5);
        patch.Add(a);
        patch.Add(b);
        return (patch, a, b);
    }

    private static Cue RecordWithTiming(CueList cueList, Patch patch, Programmer programmer, string name, double number,
        TimeSpan cueTimeIn, TimeSpan cueTimeOut) =>
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, name, number,
            new CueStoreOptions(new CueTiming(cueTimeIn, cueTimeOut, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));

    // =========================================================================================
    // Store (19-22)
    // =========================================================================================

    [Fact]
    public void RecordCue_CapturesPerParameterTiming()
    {
        var (patch, fixture) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 200);
        programmer.SetTimeIn(0, 0, TimeSpan.FromSeconds(1));
        programmer.SetTimeOut(0, 0, TimeSpan.FromSeconds(2));

        var cueList = new CueList();
        var cue = RecordWithTiming(cueList, patch, programmer, "Cue 1", 1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

        var stored = cue.Levels[(0, 0)];
        Assert.Equal(200, stored.AbsoluteValue);
        Assert.Equal(TimeSpan.FromSeconds(1), stored.TimeInOverride);
        Assert.Equal(TimeSpan.FromSeconds(2), stored.TimeOutOverride);
    }

    [Fact]
    public void RecordCue_DifferentFixtures_RetainDifferentTimingInStoredCue()
    {
        var (patch, fixtureA, fixtureB) = BuildTwoFixturePatch();
        var programmer = new Programmer();
        int idxA = fixtureA.AbsoluteIndex(fixtureA.Mode.Channels[0]);
        int idxB = fixtureB.AbsoluteIndex(fixtureB.Mode.Channels[0]);
        programmer.SetTimeOut(fixtureA.UniverseId, idxA, TimeSpan.FromSeconds(1));
        programmer.SetTimeOut(fixtureB.UniverseId, idxB, TimeSpan.FromSeconds(5));

        var cueList = new CueList();
        var cue = RecordWithTiming(cueList, patch, programmer, "Cue 1", 1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

        Assert.Equal(TimeSpan.FromSeconds(1), cue.Levels[(fixtureA.UniverseId, idxA)].TimeOutOverride);
        Assert.Equal(TimeSpan.FromSeconds(5), cue.Levels[(fixtureB.UniverseId, idxB)].TimeOutOverride);
    }

    [Fact]
    public void RecordCue_NoOverrideSet_StoresNullTiming_FallsBackImplicitly()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 100);

        var cueList = new CueList();
        var cue = RecordWithTiming(cueList, patch, programmer, "Cue 1", 1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

        var stored = cue.Levels[(0, 0)];
        Assert.Null(stored.TimeInOverride);
        Assert.Null(stored.TimeOutOverride);
    }

    /// <summary>Audit finding: CLAUDE.md §5's "STORE CUE - Programmer clear" (N1) rule is documented
    /// as decided, but neither CueList.RecordCue nor the command-line/panel Store paths actually
    /// clear the Programmer today (see this slice's own final report). This test pins down the
    /// CURRENT, pre-existing behavior (Programmer untouched by RecordCue) so this slice provably
    /// does not change it either way - it only ever ADDS timing capture, never a Programmer-clear
    /// side effect that wasn't there before.</summary>
    [Fact]
    public void RecordCue_DoesNotClearProgrammer_ExistingBehaviorUnchanged_Regression()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 150);
        programmer.SetTimeIn(0, 0, TimeSpan.FromSeconds(2));

        var cueList = new CueList();
        RecordWithTiming(cueList, patch, programmer, "Cue 1", 1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

        Assert.True(programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(150, value);
        Assert.True(programmer.TryGetTiming(0, 0, out var timeIn, out _));
        Assert.Equal(TimeSpan.FromSeconds(2), timeIn);
    }

    // =========================================================================================
    // Playback override/fallback (23-26)
    // =========================================================================================

    [Fact]
    public void TimeInOverride_SupersedesCueTimeIn_ForThatChannelOnly()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 0);

        var cueList = new CueList();
        RecordWithTiming(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();
        cueList.TryGetChannelValue(0, 0, out _); // settle at 0

        programmer.SetChannel(0, 0, 200); // going up -> TimeIn direction
        programmer.SetTimeIn(0, 0, TimeSpan.FromMilliseconds(100)); // much shorter than the Cue-level 10 minutes
        RecordWithTiming(cueList, patch, programmer, "Cue 2", 2, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        cueList.Go();

        Thread.Sleep(150);
        Assert.True(cueList.TryGetChannelValue(0, 0, out var value));
        Assert.Equal(200, value); // fully settled on the 100ms OVERRIDE, not ~0% of the 10-minute Cue-level fallback
    }

    [Fact]
    public void TimeOutOverride_SupersedesCueTimeOut_ForThatChannelOnly()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 200);

        var cueList = new CueList();
        RecordWithTiming(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();
        cueList.TryGetChannelValue(0, 0, out _); // settle at 200

        programmer.SetChannel(0, 0, 0); // going down -> TimeOut direction
        programmer.SetTimeOut(0, 0, TimeSpan.FromMilliseconds(100));
        RecordWithTiming(cueList, patch, programmer, "Cue 2", 2, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        cueList.Go();

        Thread.Sleep(150);
        Assert.True(cueList.TryGetChannelValue(0, 0, out var value));
        Assert.Equal(0, value);
    }

    [Fact]
    public void MissingOverride_FallsBackToCueLevelTiming()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 0);

        var cueList = new CueList();
        RecordWithTiming(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();
        cueList.TryGetChannelValue(0, 0, out _);

        programmer.SetChannel(0, 0, 200); // no timing override set at all
        RecordWithTiming(cueList, patch, programmer, "Cue 2", 2, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300));
        cueList.Go();

        Thread.Sleep(100);
        Assert.True(cueList.TryGetChannelValue(0, 0, out var mid));
        Assert.InRange(mid, 1, 199); // still fading on the Cue-level 300ms fallback, not already settled
    }

    [Fact]
    public void DifferentFixtures_CompleteAtDifferentTimes()
    {
        var (patch, fixtureA, fixtureB) = BuildTwoFixturePatch();
        var programmer = new Programmer();
        int idxA = fixtureA.AbsoluteIndex(fixtureA.Mode.Channels[0]);
        int idxB = fixtureB.AbsoluteIndex(fixtureB.Mode.Channels[0]);

        var cueList = new CueList();
        RecordWithTiming(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();
        cueList.TryGetChannelValue(fixtureA.UniverseId, idxA, out _);
        cueList.TryGetChannelValue(fixtureB.UniverseId, idxB, out _);

        programmer.SetChannel(fixtureA.UniverseId, idxA, 200);
        programmer.SetTimeIn(fixtureA.UniverseId, idxA, TimeSpan.FromMilliseconds(80)); // fast
        programmer.SetChannel(fixtureB.UniverseId, idxB, 200);
        programmer.SetTimeIn(fixtureB.UniverseId, idxB, TimeSpan.FromMilliseconds(400)); // slow
        RecordWithTiming(cueList, patch, programmer, "Cue 2", 2, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        cueList.Go();

        Thread.Sleep(150); // past A's 80ms, well before B's 400ms
        Assert.True(cueList.TryGetChannelValue(fixtureA.UniverseId, idxA, out var a));
        Assert.True(cueList.TryGetChannelValue(fixtureB.UniverseId, idxB, out var b));
        Assert.Equal(200, a); // A already fully settled
        Assert.True(b < 200); // B still fading
    }

    // =========================================================================================
    // Cue completion/trigger semantics (27-30)
    // =========================================================================================

    /// <summary>The critical trigger-semantics fix: a per-channel TimeIn override LONGER than the
    /// Cue-level baseline must delay the completion signal Tick() uses for AutoFollow/Wait/chain-
    /// advance - never let the Cue-level baseline alone decide "complete" once an override exists.</summary>
    [Fact]
    public void CueCompletion_WaitsForTheLongestActiveParameterTransition_NotJustCueLevelBaseline()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();

        var cueList = new CueList();
        RecordWithTiming(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero); // Manual, instant baseline
        programmer.SetChannel(0, 0, 200);
        programmer.SetTimeIn(0, 0, TimeSpan.FromMilliseconds(250)); // override much longer than Cue 2's own flat 0
        var options = new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
            CueTriggerMode.AutoFollow, TimeSpan.Zero, CueStoreFilter.AllStage);
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 2", 2, options); // AutoFollow, flat timing = 0

        cueList.Go(); // -> Cue 1 (instant, zero timing, arms the chain)
        cueList.Tick(TimeSpan.Zero); // Cue 1 completes instantly -> Cue 2's AutoFollow fires
        Assert.Equal(1, cueList.CurrentCueIndex); // now playing Cue 2, whose OWN transition has the 250ms override

        // A third cue after Cue 2, also AutoFollow, so we can observe whether Cue 2 is treated as
        // "complete" (and therefore auto-advances into Cue 3) before or after the real 250ms override.
        var afterOptions = new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
            CueTriggerMode.AutoFollow, TimeSpan.Zero, CueStoreFilter.AllStage);
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 3", 3, afterOptions);

        cueList.Tick(TimeSpan.Zero); // immediately after entering Cue 2 - its own flat CueTiming is 0,
        // but the 250ms override must still be honored: must NOT already report complete/advance.
        Assert.Equal(1, cueList.CurrentCueIndex); // still on Cue 2 - override correctly delays completion

        Thread.Sleep(300); // past the 250ms override
        cueList.Tick(TimeSpan.Zero);
        Assert.Equal(2, cueList.CurrentCueIndex); // NOW Cue 2 is truly complete -> advanced into Cue 3
    }

    [Fact]
    public void AutoFollow_BeginsOnlyAfterTrueCompletion_IncludingOverride()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();

        // Cue 0: instant baseline at 0, so Cue 1's fade (below) has something to fade FROM.
        RecordWithTiming(cueList, patch, programmer, "Cue 0", 0, TimeSpan.Zero, TimeSpan.Zero);
        programmer.SetChannel(0, 0, 200);
        programmer.SetTimeIn(0, 0, TimeSpan.FromMilliseconds(200)); // Cue 1's own override, baked in at record time
        RecordWithTiming(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero); // flat Timing = 0
        var manualThenAutoFollow = new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
            CueTriggerMode.AutoFollow, TimeSpan.Zero, CueStoreFilter.AllStage);
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 2", 2, manualThenAutoFollow);

        cueList.Go(); // -> Cue 0 (instant, settles at 0)
        cueList.Go(); // -> Cue 1, whose own TimeIn override is 200ms (Cue 1's flat Timing is 0)
        cueList.Tick(TimeSpan.Zero);
        Assert.Equal(1, cueList.CurrentCueIndex); // must NOT advance yet - override still running

        Thread.Sleep(260);
        cueList.Tick(TimeSpan.Zero);
        Assert.Equal(2, cueList.CurrentCueIndex); // advances only once the override has truly elapsed
    }

    [Fact]
    public void Wait_BeginsAfterTrueCompletion_IncludingOverride_NeverOverlapsIt()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();

        RecordWithTiming(cueList, patch, programmer, "Cue 0", 0, TimeSpan.Zero, TimeSpan.Zero);
        programmer.SetChannel(0, 0, 200);
        programmer.SetTimeIn(0, 0, TimeSpan.FromMilliseconds(200)); // Cue 1's own override, baked in at record time
        RecordWithTiming(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero); // flat Timing = 0
        var waitOptions = new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
            CueTriggerMode.Wait, TimeSpan.FromMilliseconds(80), CueStoreFilter.AllStage);
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 2", 2, waitOptions);

        cueList.Go(); // -> Cue 0 (instant, settles at 0)
        cueList.Go(); // -> Cue 1
        cueList.Tick(TimeSpan.Zero); // override not complete yet - no anchor

        Thread.Sleep(120); // past 120ms - if WAIT (80ms) had run concurrently with the 200ms override
        cueList.Tick(TimeSpan.Zero); // (old-bug style), it would already have fired by now.
        Assert.Equal(1, cueList.CurrentCueIndex); // must still be on Cue 1 - override hadn't completed at 120ms

        Thread.Sleep(200); // now past the 200ms override; this tick anchors WaitTime NOW
        cueList.Tick(TimeSpan.Zero);
        Assert.Equal(1, cueList.CurrentCueIndex); // WaitTime (80ms) hasn't elapsed since this anchor yet

        Thread.Sleep(100); // comfortably past 80ms since the anchor
        cueList.Tick(TimeSpan.Zero);
        Assert.Equal(2, cueList.CurrentCueIndex); // advances - measured from TRUE completion, not from Go()
    }

    [Fact]
    public void GoBackGoTo_Unregressed_WithOverridesPresent()
    {
        var (patch, _) = BuildPatch();
        var programmer = new Programmer();
        var cueList = new CueList();

        RecordWithTiming(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        programmer.SetChannel(0, 0, 200);
        programmer.SetTimeIn(0, 0, TimeSpan.FromMilliseconds(50));
        RecordWithTiming(cueList, patch, programmer, "Cue 2", 2, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        cueList.Go(); // -> Cue 1
        cueList.Go(); // -> Cue 2 (normal GO, still respects overrides but doesn't need to finish for GO itself)
        Assert.Equal(1, cueList.CurrentCueIndex);

        cueList.Back(); // -> Cue 1
        Assert.Equal(0, cueList.CurrentCueIndex);

        var cue2 = cueList.Cues.Single(c => c.Number == 2);
        cueList.GoToCue(cue2); // direct jump
        Assert.Equal(1, cueList.CurrentCueIndex);
    }
}

using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Core.Tests;

/// <summary>Covers the priority-first, then-MergePolicy merge algorithm in
/// DmxOutputEngine.ComputeUniverse, and the per-channel revision granularity in
/// Executor/CueList's IMergeAwareLayer implementation - Step F.</summary>
public class MergeTieBreakTests
{
    private sealed class StubEffectiveOutputReader : IEffectiveOutputReader
    {
        public byte GetEffectiveValue(int universeId, int channelIndex) => 0;
    }

    private static readonly FixtureSelection EmptySelection = new();
    private static readonly IEffectiveOutputReader Stub = new StubEffectiveOutputReader();

    private static Cue Record(CueList cueList, Patch patch, Programmer programmer, string name, double number,
        TimeSpan timeIn, TimeSpan timeOut) =>
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, name, number,
            new CueStoreOptions(new CueTiming(timeIn, timeOut, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));

    /// <summary>Dimmer (Intensity, Htp) at offset 0, Pan (Position, Ltp) at offset 1.</summary>
    private static FixtureProfile DimmerAndPan() => new()
    {
        Id = "test-dimmer-pan",
        Manufacturer = "Test",
        Model = "DimmerPan",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "2ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 },
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 1 },
                },
            },
        },
    };

    private static (Patch Patch, DmxOutputEngine Engine) BuildRig()
    {
        var patch = new Patch();
        var profile = DimmerAndPan();
        patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1));
        return (patch, new DmxOutputEngine(patch));
    }

    private static Executor BuildExecutor(Patch patch, int number, int priority, byte dimmer, byte pan)
    {
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, dimmer);
        programmer.SetChannel(0, 1, pan);

        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();

        var executor = new Executor(number, patch) { Priority = priority };
        executor.Assign(cueList);
        return executor;
    }

    private static byte[] Tick(DmxOutputEngine engine)
    {
        byte[]? published = null;
        engine.UniverseOutputReady += (_, data) => published = data;
        engine.Tick();
        return published!;
    }

    [Fact]
    public void HigherPriority_WinsOverHigherValue_EvenUnderHtp()
    {
        var (patch, engine) = BuildRig();
        var a = BuildExecutor(patch, 1, priority: 300, dimmer: 30, pan: 0);
        var b = BuildExecutor(patch, 2, priority: 200, dimmer: 80, pan: 0);
        engine.AddLayer(a);
        engine.AddLayer(b);

        var published = Tick(engine);

        Assert.Equal(30, published[0]); // A wins at 30 despite B's higher raw value - priority gates HTP too
    }

    [Fact]
    public void Htp_ChoosesHighestValue_OnlyAmongEqualHighestPriority()
    {
        var (patch, engine) = BuildRig();
        var a = BuildExecutor(patch, 1, priority: 300, dimmer: 30, pan: 0);
        var b = BuildExecutor(patch, 2, priority: 300, dimmer: 80, pan: 0);
        engine.AddLayer(a);
        engine.AddLayer(b);

        var published = Tick(engine);

        Assert.Equal(80, published[0]); // ordinary HTP max, evaluated only within the top-priority group
    }

    [Fact]
    public void EqualPriority_Ltp_HigherRevisionWins()
    {
        var (patch, engine) = BuildRig();
        var a = BuildExecutor(patch, 1, priority: 300, dimmer: 0, pan: 50);
        var b = BuildExecutor(patch, 2, priority: 300, dimmer: 0, pan: 120); // recorded/Go'd after A - newer revision
        engine.AddLayer(a);
        engine.AddLayer(b);

        var published = Tick(engine);

        Assert.Equal(120, published[1]); // B's more recent instruction wins the Ltp tie
    }

    [Fact]
    public void RepeatedTicks_DoNotStealOwnership()
    {
        var (patch, engine) = BuildRig();
        var a = BuildExecutor(patch, 1, priority: 300, dimmer: 0, pan: 50);
        var b = BuildExecutor(patch, 2, priority: 300, dimmer: 0, pan: 120);
        engine.AddLayer(a);
        engine.AddLayer(b);

        for (int i = 0; i < 5; i++)
        {
            var published = Tick(engine);
            Assert.Equal(120, published[1]); // B keeps winning - no revision bump just from being read again
        }
    }

    [Fact]
    public void ReTakingOwnership_AfterARealChange()
    {
        var (patch, engine) = BuildRig();
        var a = BuildExecutor(patch, 1, priority: 300, dimmer: 0, pan: 50);
        var b = BuildExecutor(patch, 2, priority: 300, dimmer: 0, pan: 120);
        engine.AddLayer(a);
        engine.AddLayer(b);

        Assert.Equal(120, Tick(engine)[1]); // B wins initially

        // A genuine handle-level semantic event on A: re-Assign is a wholesale "this handle now
        // puts out something new" change (unlike a FaderLevel tweak that doesn't cross the
        // on/off gate, which correctly does NOT count as a real change for a non-Intensity
        // channel - see FaderLevelChange_UpdatesRevision_OnlyForTheClassItActuallyAffects above).
        var newProgrammer = new Programmer();
        newProgrammer.SetChannel(0, 1, 50);
        var newCueList = new CueList();
        Record(newCueList, patch, newProgrammer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        newCueList.Go();
        a.Assign(newCueList);

        Assert.Equal(50, Tick(engine)[1]); // A retakes ownership of the Position channel
    }

    [Fact]
    public void LowerPriority_CannotRetakeOwnership_MerelyByNewerRevision()
    {
        var (patch, engine) = BuildRig();
        var a = BuildExecutor(patch, 1, priority: 300, dimmer: 0, pan: 50); // old revision, high priority
        var b = BuildExecutor(patch, 2, priority: 200, dimmer: 0, pan: 120); // fresh revision, low priority
        engine.AddLayer(a);
        engine.AddLayer(b);

        b.FaderLevel = 0.99; // give B an even newer revision - still must not matter

        var published = Tick(engine);

        Assert.Equal(50, published[1]); // A (higher priority) still wins regardless of B's fresher revision
    }

    [Fact]
    public void FaderLevelChange_UpdatesRevision_OnlyForTheClassItActuallyAffects()
    {
        var (patch, _) = BuildRig();
        var executor = new Executor(1, patch) { Priority = 200 };
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 100); // Dimmer (Intensity)
        programmer.SetChannel(0, 1, 50);  // Pan (Position)
        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();
        executor.Assign(cueList);

        executor.TryGetRevision(0, 0, out var dimmerBefore);
        executor.TryGetRevision(0, 1, out var panBefore);

        executor.FaderLevel = 0.5; // both channels still contribute (0.5 > 0) - gate does not flip

        executor.TryGetRevision(0, 0, out var dimmerAfter);
        executor.TryGetRevision(0, 1, out var panAfter);

        Assert.True(dimmerAfter > dimmerBefore); // Intensity's numeric scaling really did change
        Assert.Equal(panBefore, panAfter);       // Position's raw contribution did NOT change - no bump

        executor.FaderLevel = 0; // now the on/off gate flips for non-Intensity too
        executor.TryGetRevision(0, 1, out var panAfterGateFlip);
        Assert.True(panAfterGateFlip > panBefore); // contribution genuinely suppressed - a real change
    }

    [Fact]
    public void GoChangingOnlyOneChannel_DoesNotAffectAnotherChannelsRevision()
    {
        var (patch, _) = BuildRig();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 60); // Dimmer
        programmer.SetChannel(0, 1, 10); // Pan
        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1, TimeSpan.Zero, TimeSpan.Zero);
        cueList.Go();

        cueList.TryGetRevision(0, 0, out var dimmerRevisionAfterFirstGo);

        // Go to a cue that only touches Pan, leaving Dimmer's revision alone. (Today RecordCue
        // always full-snapshots every patched channel - this test constructs a sparse Cue
        // directly and plays it via the public GoToCue, to simulate what a future sparse/
        // Tracking-aware cue would look like, per the plan's explicit "don't implement Tracking,
        // but keep the contract compatible with it" scope.)
        var secondCueLevels = new Dictionary<(int, int), CueValue> { [(0, 1)] = CueValue.Absolute(ChannelType.Pan, 90) };
        var zeroTiming = new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        var secondCue = new Cue { Number = 2, Name = "Cue 2 (Pan only)", Timing = zeroTiming, Levels = secondCueLevels };
        cueList.Cues.Add(secondCue);
        cueList.GoToCue(secondCue);

        cueList.TryGetRevision(0, 0, out var dimmerRevisionAfterSecondGo);
        Assert.Equal(dimmerRevisionAfterFirstGo, dimmerRevisionAfterSecondGo); // untouched by the Pan-only transition
    }

    [Fact]
    public void ARealNewMove_RetakesEqualPriorityLatestOwnership()
    {
        var (patch, engine) = BuildRig();
        var a = BuildExecutor(patch, 1, priority: 300, dimmer: 0, pan: 50);
        var b = BuildExecutor(patch, 2, priority: 300, dimmer: 0, pan: 120); // recorded after A - currently owns Pan
        engine.AddLayer(a);
        engine.AddLayer(b);

        Assert.Equal(120, Tick(engine)[1]); // B currently owns the Position (Ltp) channel

        // Give A a genuinely new instruction on that same channel - it must retake ownership,
        // proving the mechanism isn't "stuck" once it has lost a tie.
        var aSecondCue = new Cue { Number = 2, Name = "A - new Pan", Timing = new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero), Levels = new Dictionary<(int, int), CueValue> { [(0, 1)] = CueValue.Absolute(ChannelType.Pan, 200) } };
        ((CueList)a.Source!).Cues.Add(aSecondCue);
        ((CueList)a.Source!).GoToCue(aSecondCue);

        Assert.Equal(200, Tick(engine)[1]);
    }
}

using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Core.Tests;

public class ExecutorTests
{
    private sealed class StubEffectiveOutputReader : IEffectiveOutputReader
    {
        public byte GetEffectiveValue(int universeId, int channelIndex) => 0;
        public OutputOwner? GetOwner(int universeId, int channelIndex) => null;
    }

    private static readonly FixtureSelection EmptySelection = new();
    private static readonly IEffectiveOutputReader Stub = new StubEffectiveOutputReader();

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

    private static (Patch Patch, PatchedFixture Fixture) BuildPatch()
    {
        var patch = new Patch();
        var profile = DimmerAndPan();
        var fixture = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        patch.Add(fixture);
        return (patch, fixture);
    }

    private static CueList BuildCueList(Patch patch, byte dimmer, byte pan)
    {
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, dimmer);
        programmer.SetChannel(0, 1, pan);
        var zeroOptions = new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
            CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage);
        var cueList = new CueList();
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, "Cue 1", 1, zeroOptions);
        cueList.Go();
        return cueList;
    }

    [Fact]
    public void NoSource_IsInactive_NeverContributes()
    {
        var executor = new Executor(1);
        Assert.False(executor.IsActive);
        Assert.False(executor.TryGetChannelValue(0, 0, out _));
    }

    [Fact]
    public void FullLevel_PassesThroughUnchanged_ForIntensityAndNonIntensity()
    {
        var (patch, _) = BuildPatch();
        var cueList = BuildCueList(patch, dimmer: 200, pan: 77);
        var executor = new Executor(1, patch);
        executor.Assign(cueList);

        Assert.True(executor.TryGetChannelValue(0, 0, out var dimmer));
        Assert.True(executor.TryGetChannelValue(0, 1, out var pan));
        Assert.Equal(200, dimmer);
        Assert.Equal(77, pan);
    }

    [Fact]
    public void HalfLevel_ScalesIntensity_LeavesNonIntensityUnscaled()
    {
        var (patch, _) = BuildPatch();
        var cueList = BuildCueList(patch, dimmer: 200, pan: 77);
        var executor = new Executor(1, patch) { FaderLevel = 0.5 };
        executor.Assign(cueList);

        Assert.True(executor.TryGetChannelValue(0, 0, out var dimmer));
        Assert.True(executor.TryGetChannelValue(0, 1, out var pan));
        Assert.Equal(100, dimmer); // 200 * 0.5
        Assert.Equal(77, pan);     // unchanged - Position is never numerically scaled
    }

    [Fact]
    public void ZeroLevel_SuppressesNonIntensity_ScalesIntensityToZero()
    {
        var (patch, _) = BuildPatch();
        var cueList = BuildCueList(patch, dimmer: 200, pan: 77);
        var executor = new Executor(1, patch) { FaderLevel = 0 };
        executor.Assign(cueList);

        Assert.True(executor.TryGetChannelValue(0, 0, out var dimmer));
        Assert.Equal(0, dimmer);
        Assert.False(executor.TryGetChannelValue(0, 1, out _)); // fully suppressed, not just zeroed
    }

    [Fact]
    public void NoChannelTypeLookup_TreatsEveryChannelAsNonIntensity_NeverCrashes()
    {
        var (patch, _) = BuildPatch();
        var cueList = BuildCueList(patch, dimmer: 200, pan: 77);
        var executor = new Executor(1, channelTypeLookup: null) { FaderLevel = 0.5 };
        executor.Assign(cueList);

        Assert.True(executor.TryGetChannelValue(0, 0, out var dimmer));
        Assert.Equal(200, dimmer); // treated as non-Intensity -> passthrough, not scaled
    }

    [Fact]
    public void FlashAdd_ForcesFullLevel_RegardlessOfFaderLevel_RevertsWhenCleared()
    {
        var (patch, _) = BuildPatch();
        var cueList = BuildCueList(patch, dimmer: 200, pan: 77);
        var executor = new Executor(1, patch) { FaderLevel = 0 };
        executor.Assign(cueList);

        Assert.True(executor.TrySetFlash(FlashMode.Add));
        Assert.True(executor.TryGetChannelValue(0, 0, out var dimmerFlashed));
        Assert.Equal(200, dimmerFlashed);
        Assert.True(executor.TryGetChannelValue(0, 1, out var panFlashed));
        Assert.Equal(77, panFlashed);

        Assert.True(executor.TrySetFlash(FlashMode.None));
        Assert.True(executor.TryGetChannelValue(0, 0, out var dimmerAfter));
        Assert.Equal(0, dimmerAfter);
        Assert.False(executor.TryGetChannelValue(0, 1, out _));
    }

    [Fact]
    public void TrySetFlash_Swap_ReturnsFalse_LeavesFlashUnchanged()
    {
        var executor = new Executor(1);
        Assert.False(executor.TrySetFlash(FlashMode.Swap));
        Assert.Equal(FlashMode.None, executor.Flash);
    }

    [Fact]
    public void Assign_AtRuntime_SwapsContribution_WithoutRecreatingExecutor()
    {
        var (patch, _) = BuildPatch();
        var executor = new Executor(1, patch);
        executor.Assign(BuildCueList(patch, dimmer: 50, pan: 10));
        Assert.True(executor.TryGetChannelValue(0, 0, out var first));
        Assert.Equal(50, first);

        executor.Assign(BuildCueList(patch, dimmer: 150, pan: 20));
        Assert.True(executor.TryGetChannelValue(0, 0, out var second));
        Assert.Equal(150, second);
    }

    [Fact]
    public void Id_StaysIdentical_AcrossNumberAndNameChanges()
    {
        var executor = new Executor(1);
        var originalId = executor.Id;

        executor.Number = 42;
        executor.Name = "Front Wash";

        Assert.Equal(originalId, executor.Id);
    }

    [Fact]
    public void IntensityClassification_DoesNotDependOnCurrentCueStoredData()
    {
        // Simulates - without implementing - a future Tracking scenario: the resolved playback
        // byte for the Dimmer channel comes from a source that has NO entry for it in whatever
        // it considers its "current" cue (only Pan is stored) - Executor must still classify and
        // scale the Dimmer channel correctly as Intensity, because that classification comes
        // from the Patch (stable), never from the source's own stored data.
        var (patch, _) = BuildPatch();
        var stubSource = new StubPlaybackSource(dimmerValue: 100, panValue: 30);
        var executor = new Executor(1, patch) { FaderLevel = 0.5 };
        executor.Assign(stubSource);

        Assert.True(executor.TryGetChannelValue(0, 0, out var dimmer));
        Assert.Equal(50, dimmer); // still scaled as Intensity (100 * 0.5), even though the stub
                                  // "doesn't store" this channel in whatever it considers current data
    }

    /// <summary>Minimal IPlaybackSource stub: contributes fixed values, deliberately independent
    /// of any "current cue" concept - proves Executor's Intensity classification is Patch-backed,
    /// not source-backed.</summary>
    private sealed class StubPlaybackSource : IPlaybackSource
    {
        private readonly byte _dimmerValue;
        private readonly byte _panValue;

        public StubPlaybackSource(byte dimmerValue, byte panValue)
        {
            _dimmerValue = dimmerValue;
            _panValue = panValue;
        }

        public string Name => "Stub";
        public int Priority => 200;
        public bool IsActive => true;

        public bool TryGetChannelValue(int universeId, int channelIndex, out byte value)
        {
            // Deliberately has no notion of "current cue" at all - just resolved output, as a
            // future Tracking-resolved value would look. Executor's Intensity classification
            // must come from the Patch, never from anything this stub tracks internally.
            if (channelIndex == 0) { value = _dimmerValue; return true; }
            if (channelIndex == 1) { value = _panValue; return true; }
            value = 0;
            return false;
        }

        public PlaybackStatus GetStatus() => new CueListPlaybackStatus(null, null, true, false,
            new TimingProgress(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
            new TimingProgress(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
            new TimingProgress(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
    }
}

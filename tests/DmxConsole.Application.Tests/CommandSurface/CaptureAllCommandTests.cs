using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Tests.CommandSurface;

/// <summary>docs/COMMAND_SURFACE_KEY_SPEC.md §8 - CAPTURE ALL: "Effective Live State -&gt;
/// Editor". Most of these tests use a small fake IEffectiveOutputReader rather than a real
/// CueList fade, deliberately: CueList's fade interpolation is wall-clock based
/// (DateTime.UtcNow) with no injectable clock, and adding one is out of this slice's scope
/// (explicitly: "do not duplicate playback math", "keep Slice 4 isolated"). CaptureAllCommand
/// itself depends on nothing but IEffectiveOutputReader/Programmer/Patch - it has zero knowledge
/// of Cues, fades, or timing - so a controlled fake reader IS the correct unit-test boundary for
/// proving its own exactness, including an exact "47%, neither the Cue A nor Cue B value"
/// mid-fade-shaped reading. Tests that need to prove real engine integration (Undo, Release
/// revealing an underlying value again) use a real ConsoleContext/DmxOutputEngine/CueList with an
/// INSTANT (zero-time) Cue - deterministic, no sleep, no timing race - since only the
/// "mid-fade value itself" claim needs an interpolated source, not the surrounding plumbing.</summary>
public class CaptureAllCommandTests
{
    private sealed class FakeEffectiveOutputReader : IEffectiveOutputReader
    {
        private readonly Dictionary<(int, int), byte> _values = new();
        private readonly Dictionary<(int, int), OutputOwner?> _owners = new();

        public void SetActive(int universe, int channel, byte value, OutputOwner owner)
        {
            _values[(universe, channel)] = value;
            _owners[(universe, channel)] = owner;
        }

        public byte GetEffectiveValue(int universeId, int channelIndex) =>
            _values.TryGetValue((universeId, channelIndex), out var v) ? v : (byte)0;

        public OutputOwner? GetOwner(int universeId, int channelIndex) =>
            _owners.TryGetValue((universeId, channelIndex), out var o) ? o : null;
    }

    private static readonly OutputOwner ExecutorOwner = new("executor:1", "Exec 1 / Cue B", OwnerKind.Executor, null, 1);
    private static readonly OutputOwner EffectOwner = new("effect:1", "Rainbow", OwnerKind.Effect, null, null);

    private static FixtureProfile MovingHead() => new()
    {
        Id = "test-moving-head", Manufacturer = "Test", Model = "MovingHead",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "3ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 },
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 1 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2 },
                },
            },
        },
    };

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, PatchedFixture Fixture, FakeEffectiveOutputReader Reader) BuildFakeRig()
    {
        var patch = new Patch();
        var profile = MovingHead();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        patch.Add(fixture);
        var reader = new FakeEffectiveOutputReader();
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            reader, new PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        return (context, dispatcher, undoRedo, fixture, reader);
    }

    // 1 + 2: captures the actual (mid-fade-shaped) effective value, never a Cue's source/target.
    [Fact]
    public void CapturesTheEffectiveValue_NeitherCueSourceNorTarget()
    {
        var (context, dispatcher, _, fixture, reader) = BuildFakeRig();
        int dimmerIdx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Dimmer)!);
        // Cue A = 20% (51), Cue B = 80% (204) - the engine's real merge would currently be
        // reporting 47% (120) mid-fade. CaptureAllCommand must read exactly this, not 51 or 204.
        const byte midFadeValue = 120;
        reader.SetActive(0, dimmerIdx, midFadeValue, ExecutorOwner);

        var result = dispatcher.Dispatch(new CaptureAllCommand(context.Patch.Fixtures.ToList()));

        Assert.True(result.Success);
        Assert.True(context.Programmer.HasStoredValue(0, dimmerIdx, out var captured));
        Assert.Equal(midFadeValue, captured);
        Assert.NotEqual((byte)51, captured);
        Assert.NotEqual((byte)204, captured);
    }

    // 3: semantic non-intensity parameters (Position) are captured too.
    [Fact]
    public void CapturesNonIntensityParameters_Position()
    {
        var (context, dispatcher, _, fixture, reader) = BuildFakeRig();
        int panIdx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!);
        reader.SetActive(0, panIdx, 77, ExecutorOwner);

        dispatcher.Dispatch(new CaptureAllCommand(context.Patch.Fixtures.ToList()));

        Assert.True(context.Programmer.HasStoredValue(0, panIdx, out var captured));
        Assert.Equal(77, captured);
    }

    // 4: does not require non-zero Intensity - a fixture live only via Position (Dimmer
    // untouched/no owner) still has its live Position parameter captured.
    [Fact]
    public void DoesNotRequireNonZeroIntensity_CapturesLivePositionOnAZeroIntensityFixture()
    {
        var (context, dispatcher, _, fixture, reader) = BuildFakeRig();
        int dimmerIdx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Dimmer)!);
        int panIdx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!);
        // Dimmer: no owner at all (nothing contributing - a bare default, effective value 0).
        // Pan: actively owned at 90, regardless of the fixture's Intensity being effectively 0.
        reader.SetActive(0, panIdx, 90, ExecutorOwner);

        dispatcher.Dispatch(new CaptureAllCommand(context.Patch.Fixtures.ToList()));

        Assert.False(context.Programmer.HasStoredValue(0, dimmerIdx, out _)); // no owner - never captured
        Assert.True(context.Programmer.HasStoredValue(0, panIdx, out var pan));
        Assert.Equal(90, pan);
    }

    // A channel with no active owner (bare default) must never be captured - proves CAPTURE ALL
    // doesn't just dump every patched fixture's defaults into the Editor.
    [Fact]
    public void UnownedChannel_NeverCaptured()
    {
        var (context, dispatcher, _, fixture, _) = BuildFakeRig();
        int tiltIdx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Tilt)!);
        // Nothing set active for Tilt - GetOwner returns null, GetEffectiveValue returns 0.

        dispatcher.Dispatch(new CaptureAllCommand(context.Patch.Fixtures.ToList()));

        Assert.False(context.Programmer.HasStoredValue(0, tiltIdx, out _));
    }

    // 5: does not change current selection.
    [Fact]
    public void DoesNotChangeSelection()
    {
        var (context, dispatcher, _, fixture, reader) = BuildFakeRig();
        var otherFixture = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 10);
        context.Patch.Add(otherFixture);
        context.Selection.Add(fixture); // deliberately NOT selecting otherFixture
        int dimmerIdx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Dimmer)!);
        reader.SetActive(0, dimmerIdx, 100, ExecutorOwner);
        var otherDimmerIdx = otherFixture.AbsoluteIndex(otherFixture.FindChannel(ChannelType.Dimmer)!);
        reader.SetActive(otherFixture.UniverseId, otherDimmerIdx, 200, ExecutorOwner);

        dispatcher.Dispatch(new CaptureAllCommand(context.Patch.Fixtures.ToList()));

        // Selection is untouched even though CAPTURE ALL captured a fixture that was NOT selected -
        // it operates on the whole patch, never the selection.
        Assert.Single(context.Selection.Items);
        Assert.Equal(fixture, context.Selection.Items[0]);
        Assert.True(context.Programmer.HasStoredValue(otherFixture.UniverseId, otherDimmerIdx, out _));
    }

    // 8: Undo restores the exact pre-capture Editor state, including "had no value at all".
    [Fact]
    public void Undo_RestoresPreCaptureEditorState()
    {
        var (context, dispatcher, undoRedo, fixture, reader) = BuildFakeRig();
        int dimmerIdx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Dimmer)!);
        int panIdx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!);
        context.Programmer.SetChannel(0, dimmerIdx, 33); // Editor already owned Dimmer before capture
        reader.SetActive(0, dimmerIdx, 200, ExecutorOwner); // capture will overwrite it to 200
        reader.SetActive(0, panIdx, 90, ExecutorOwner); // Pan had no prior Editor value at all

        dispatcher.Dispatch(new CaptureAllCommand(context.Patch.Fixtures.ToList()));
        Assert.True(context.Programmer.HasStoredValue(0, dimmerIdx, out var afterCapture));
        Assert.Equal(200, afterCapture);
        Assert.True(context.Programmer.HasStoredValue(0, panIdx, out _));

        var outcome = undoRedo.Undo();

        Assert.True(outcome.Performed);
        Assert.True(context.Programmer.HasStoredValue(0, dimmerIdx, out var restored));
        Assert.Equal(33, restored); // back to its pre-capture Editor value, not cleared
        Assert.False(context.Programmer.HasStoredValue(0, panIdx, out _)); // back to "no Editor value at all"
    }

    // 9: an existing Editor value is UPDATED to the current effective value, not skipped.
    [Fact]
    public void ExistingEditorValue_IsUpdatedToCurrentEffectiveValue_NotSkipped()
    {
        var (context, dispatcher, _, fixture, reader) = BuildFakeRig();
        int dimmerIdx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Dimmer)!);
        context.Programmer.SetChannel(0, dimmerIdx, 10);
        reader.SetActive(0, dimmerIdx, 250, ExecutorOwner);

        dispatcher.Dispatch(new CaptureAllCommand(context.Patch.Fixtures.ToList()));

        Assert.True(context.Programmer.HasStoredValue(0, dimmerIdx, out var value));
        Assert.Equal(250, value); // updated, not left at the stale 10
    }

    // 10: capture from multiple, DIFFERENT playback sources each respects the already-resolved
    // effective output - CaptureAllCommand never cares which kind of source is winning.
    [Fact]
    public void CapturesCorrectly_AcrossDifferentOwnerKinds_ExecutorAndEffect()
    {
        var (context, dispatcher, _, fixture, reader) = BuildFakeRig();
        int dimmerIdx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Dimmer)!);
        int panIdx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!);
        reader.SetActive(0, dimmerIdx, 60, ExecutorOwner);
        reader.SetActive(0, panIdx, 130, EffectOwner);

        dispatcher.Dispatch(new CaptureAllCommand(context.Patch.Fixtures.ToList()));

        Assert.True(context.Programmer.HasStoredValue(0, dimmerIdx, out var dimmer));
        Assert.Equal(60, dimmer);
        Assert.True(context.Programmer.HasStoredValue(0, panIdx, out var pan));
        Assert.Equal(130, pan);
    }

    // 12: self-terminating grammar - never requires ENTER.
    [Fact]
    public void ComposerGrammar_SelfTerminates_NoEnterRequired()
    {
        var (context, dispatcher, _, _, _) = BuildFakeRig();
        var composer = new CommandComposer(context);

        var composition = composer.Push(CommandToken.Simple(CommandTokenKind.CaptureAll));

        Assert.True(composition.IsComplete);
        Assert.NotNull(composition.ReadyOperation);
        var result = dispatcher.Dispatch(composition.ReadyOperation!);
        Assert.True(result.Success);
    }

    // --- Real-engine integration (deterministic: instant/zero-time Cue, no wall-clock race) ---

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, PatchedFixture Fixture, DmxOutputEngine Engine) BuildRealRig()
    {
        var patch = new Patch();
        var profile = MovingHead();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        patch.Add(fixture);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        return (context, dispatcher, undoRedo, fixture, engine);
    }

    // 6: does not stop or modify playback state - a real running CueList/Executor stays exactly
    // as it was (still active, same current cue) after CAPTURE ALL.
    [Fact]
    public void DoesNotStopOrModifyPlaybackState()
    {
        var (context, dispatcher, _, fixture, engine) = BuildRealRig();
        var cueList = new CueList();
        var record = cueList.RecordCue(context.Patch, new Programmer(), context.Selection, engine, "Cue 1", 1,
            new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));
        var executor = context.Executors.Add(1);
        executor.Assign(cueList);
        engine.AddLayer(context.Programmer);
        engine.AddLayer(executor);
        cueList.Go();
        engine.Tick();
        Assert.True(cueList.IsActive);
        var cueBefore = cueList.CurrentCue;

        dispatcher.Dispatch(new CaptureAllCommand(context.Patch.Fixtures.ToList()));

        Assert.True(cueList.IsActive); // unchanged
        Assert.Same(cueBefore, cueList.CurrentCue); // unchanged
        Assert.False(cueList.IsPaused); // unchanged
    }

    // 11: RELEASE after capture reveals the underlying playback value again - proves no conflict
    // with the already-implemented Release model.
    [Fact]
    public void ReleaseAfterCapture_RevealsUnderlyingPlaybackValueAgain()
    {
        var (context, dispatcher, _, fixture, engine) = BuildRealRig();
        var cueList = new CueList();
        int dimmerIdx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Dimmer)!);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, dimmerIdx, 128); // grab the value that will be recorded
        // Instant cue (zero timing) - deterministic snap to target, no wall-clock fade to race.
        cueList.RecordCue(context.Patch, context.Programmer, context.Selection, engine, "Cue 1", 1,
            new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));
        context.Programmer.ClearChannel(0, dimmerIdx); // release the grab - only the Cue holds 128 now
        var executor = context.Executors.Add(1);
        executor.Assign(cueList);
        engine.AddLayer(context.Programmer);
        engine.AddLayer(executor);
        cueList.Go();
        engine.Tick();
        Assert.Equal((byte)128, engine.GetEffectiveValue(0, dimmerIdx)); // Cue is winning at 128

        dispatcher.Dispatch(new CaptureAllCommand(context.Patch.Fixtures.ToList()));
        engine.Tick();
        Assert.True(context.Programmer.HasStoredValue(0, dimmerIdx, out var captured));
        Assert.Equal(128, captured);
        Assert.Equal(OwnerKind.Programmer, engine.GetOwner(0, dimmerIdx)!.Kind); // Editor now owns it

        var releaseResult = dispatcher.Dispatch(new ReleaseCommand(context.Selection.Items.ToList(), null));
        engine.Tick();

        Assert.True(releaseResult.Success);
        Assert.False(context.Programmer.HasStoredValue(0, dimmerIdx, out _)); // Editor ownership gone
        Assert.Equal((byte)128, engine.GetEffectiveValue(0, dimmerIdx)); // Cue's own value reappears
        Assert.Equal(OwnerKind.Executor, engine.GetOwner(0, dimmerIdx)!.Kind); // provenance reverts to the Executor
    }
}

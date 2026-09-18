using DmxConsole.Application.Commands.Playback;
using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Application.Macros;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Tests.Macros;

/// <summary>docs/COMMAND_SURFACE_KEY_SPEC.md MACROS §4/§7/§8/§9 - MACRO N playback. Every step
/// replays through the SAME CommandDispatcher.Dispatch/DispatchAction a live operator press would
/// use, so Undo/non-undoable semantics are never reinvented - proven directly against
/// UndoRedoService here, not assumed.</summary>
public class MacroPlaybackServiceTests
{
    private static FixtureProfile Dimmer() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private sealed record Rig(ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, MacroRecorder Recorder, MacroPlaybackService Player, PatchedFixture Fixture);

    private static Rig BuildRig()
    {
        var patch = new Patch();
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(), engine, new PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        var recorder = new MacroRecorder(dispatcher, context.Macros);
        var player = new MacroPlaybackService(dispatcher, context.Macros, recorder);

        var fixture = new PatchedFixture(Dimmer(), Dimmer().Modes[0], 0, 1) { Number = 1 };
        context.Patch.Add(fixture);

        return new Rig(context, dispatcher, undoRedo, recorder, player, fixture);
    }

    private static void Record(Rig rig, int slot, params IConsoleCommand[] commands)
    {
        rig.Recorder.Start(slot);
        foreach (var command in commands) rig.Dispatcher.Dispatch(command);
        rig.Recorder.Stop();
    }

    // 7: an empty slot returns a clear, structured result - never silently does nothing.
    [Fact]
    public void Play_EmptySlot_ReturnsClearFailedResult()
    {
        var rig = BuildRig();

        var result = rig.Player.Play(1);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Play_InvalidSlot_IsRejectedHonestly()
    {
        var rig = BuildRig();

        Assert.False(rig.Player.Play(0).Success);
        Assert.False(rig.Player.Play(9).Success);
    }

    // 5: MACRO 1 replays operations correctly.
    [Fact]
    public void Play_ReplaysRecordedCommands_InOrder()
    {
        var rig = BuildRig();
        Record(rig, 1,
            new AddFixtureToSelectionCommand(rig.Fixture),
            new AdjustIntensityCommand(new List<PatchedFixture> { rig.Fixture }, AdjustOperation.Absolute, 50));

        // Undo any live-recording side effects doesn't apply here (Stop doesn't undo) - reset state to prove Play() alone reproduces it.
        rig.Context.Selection.Clear();
        rig.Context.Programmer.ClearAll();

        var result = rig.Player.Play(1);

        Assert.True(result.Success);
        Assert.Contains(rig.Fixture, rig.Context.Selection.Items);
        Assert.True(rig.Context.Programmer.HasStoredValue(rig.Fixture.UniverseId, 0, out var value));
        Assert.Equal((byte)Math.Round(50 / 100.0 * 255.0), value);
        Assert.Equal(2, result.ChildResults.Count);
    }

    // 11: Macro execution preserves normal Undo semantics - each step is its own Undo entry, not
    // one giant Macro transaction.
    [Fact]
    public void Play_EachStepEntersUndoHistoryIndividually()
    {
        var rig = BuildRig();
        var fixture2 = new PatchedFixture(Dimmer(), Dimmer().Modes[0], 0, 20) { Number = 2 };
        rig.Context.Patch.Add(fixture2);

        Record(rig, 1,
            new AdjustIntensityCommand(new List<PatchedFixture> { rig.Fixture }, AdjustOperation.Absolute, 50),
            new AdjustIntensityCommand(new List<PatchedFixture> { fixture2 }, AdjustOperation.Absolute, 80));

        rig.Context.Programmer.ClearAll();
        rig.Player.Play(1);

        Assert.True(rig.Context.Programmer.HasStoredValue(fixture2.UniverseId, fixture2.AbsoluteIndex(fixture2.Mode.Channels[0]), out _));

        var firstUndo = rig.UndoRedo.Undo(); // undoes only the SECOND recorded step (last dispatched)
        Assert.True(firstUndo.Performed);
        Assert.False(rig.Context.Programmer.HasStoredValue(fixture2.UniverseId, fixture2.AbsoluteIndex(fixture2.Mode.Channels[0]), out _));
        Assert.True(rig.Context.Programmer.HasStoredValue(rig.Fixture.UniverseId, 0, out _)); // first step's effect still stands

        var secondUndo = rig.UndoRedo.Undo(); // undoes the FIRST recorded step
        Assert.True(secondUndo.Performed);
        Assert.False(rig.Context.Programmer.HasStoredValue(rig.Fixture.UniverseId, 0, out _));
    }

    // 12: a runtime IConsoleAction step preserves non-undoable behavior during playback.
    [Fact]
    public void Play_ActionStep_NeverEntersUndoHistory()
    {
        var rig = BuildRig();
        rig.Recorder.Start(1);
        rig.Dispatcher.DispatchAction(new ReleaseAllPlaybacksAction());
        rig.Recorder.Stop();

        var result = rig.Player.Play(1);

        Assert.True(result.Success);
        Assert.False(rig.UndoRedo.Undo().Performed);
    }

    // 9: Macro playback during Learn is safely rejected in v1.
    [Fact]
    public void Play_WhileRecording_IsRejected()
    {
        var rig = BuildRig();
        Record(rig, 1, new AddFixtureToSelectionCommand(rig.Fixture));
        rig.Recorder.Start(2); // recording a DIFFERENT slot

        var result = rig.Player.Play(1);

        Assert.False(result.Success);
        Assert.Contains("recording", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // 10: self-recursion is impossible - a defensive re-entrancy guard rejects Play() being
    // invoked again while a Macro is already playing back. In normal v1 operation this can never
    // even arise - MacroRecorder rejects Macro playback while recording (Play_WhileRecording_IsRejected
    // above), so no Macro can ever legitimately come to CONTAIN a "play a macro" step. This test
    // bypasses that recording-time prevention on purpose (building the Macro directly via the
    // public MacroBank/Macro API, not through MacroRecorder) specifically to prove the SEPARATE,
    // independent _isPlaying guard inside MacroPlaybackService itself also holds - defense in
    // depth, not "one mechanism happens to prevent it".
    [Fact]
    public void Play_ReenteredWhileAlreadyPlaying_IsRejected_NoStackOverflow()
    {
        var rig = BuildRig();
        var macro = new Macro(1);
        var recursive = new RecursivePlaybackCommand(() => rig.Player.Play(1));
        macro.Steps.Add(MacroStep.ForCommand(recursive));
        rig.Context.Macros.Set(1, macro);

        var outcome = rig.Player.Play(1);

        Assert.True(outcome.Success); // the outer Play() itself completes normally
        Assert.Single(outcome.ChildResults);
        Assert.False(outcome.ChildResults[0].Success); // the re-entrant, nested Play() call was rejected
        Assert.Contains("already playing", outcome.ChildResults[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecursivePlaybackCommand : IConsoleCommand, IReplayableCommand
    {
        private readonly Func<CommandResult> _reenter;
        public RecursivePlaybackCommand(Func<CommandResult> reenter) => _reenter = reenter;

        public CommandResult Execute(ConsoleContext context) => _reenter();
        public void Undo(ConsoleContext context) { }
        public IConsoleCommand CreateFreshInstance() => new RecursivePlaybackCommand(_reenter);
    }
}

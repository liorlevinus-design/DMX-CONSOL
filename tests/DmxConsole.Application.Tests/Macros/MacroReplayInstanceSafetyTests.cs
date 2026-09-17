using DmxConsole.Application.Commands.Groups;
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

/// <summary>
/// docs/COMMAND_SURFACE_KEY_SPEC.md MACROS follow-up "REPLAY INSTANCE SAFETY". Many
/// IConsoleCommand implementations hold mutable per-execution Undo snapshot state
/// (ProgrammerChannelCommandBase's own captured "before" values, SelectionCommandBase's
/// _previousSelection) - the same command instance must never be dispatched (and therefore never
/// enter the Undo stack) more than once, or two Undo-stack entries end up sharing one mutable
/// snapshot and silently corrupt each other's Undo. These tests prove the specific scenario that
/// exposed the original bug: play the same Macro more than once, with state changing between
/// plays, then Undo back through every play in reverse - each Undo must restore exactly that
/// play's own captured "before" state, never some other play's.
/// </summary>
public class MacroReplayInstanceSafetyTests
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

    private static byte Percent(double percent) => (byte)Math.Round(percent / 100.0 * 255.0);

    private static void SetInitial(Rig rig, double percent) =>
        rig.Context.Programmer.SetChannel(rig.Fixture.UniverseId, 0, Percent(percent));

    private static byte CurrentValue(Rig rig)
    {
        Assert.True(rig.Context.Programmer.HasStoredValue(rig.Fixture.UniverseId, 0, out var value));
        return value;
    }

    // The exact scenario from the follow-up request:
    //   Initial state = 10 -> Play Macro 1 -> 50 -> change to 80 -> Play Macro 1 -> 50
    //   -> Undo (restores 80, play #2's own "before") -> Undo (restores 10, play #1's own "before")
    [Fact]
    public void PlaySameMacroTwice_ThenUndoTwice_EachUndoRestoresTheCorrectPerPlayState()
    {
        var rig = BuildRig();
        SetInitial(rig, 10);
        rig.Recorder.Start(1);
        rig.Dispatcher.Dispatch(new AdjustIntensityCommand(new List<PatchedFixture> { rig.Fixture }, AdjustOperation.Absolute, 50));
        rig.Recorder.Stop();
        SetInitial(rig, 10); // undo recording's own side effect - start playback from a clean baseline

        var play1 = rig.Player.Play(1);
        Assert.True(play1.Success);
        Assert.Equal(Percent(50), CurrentValue(rig));

        SetInitial(rig, 80); // state changes between plays

        var play2 = rig.Player.Play(1);
        Assert.True(play2.Success);
        Assert.Equal(Percent(50), CurrentValue(rig));

        var undo1 = rig.UndoRedo.Undo();
        Assert.True(undo1.Performed);
        Assert.Equal(Percent(80), CurrentValue(rig)); // play #2's own captured "before" - never play #1's

        var undo2 = rig.UndoRedo.Undo();
        Assert.True(undo2.Performed);
        Assert.Equal(Percent(10), CurrentValue(rig)); // play #1's own captured "before" - the true original
    }

    [Fact]
    public void PlaySameMacro_ThreeOrMoreTimes_EachUndoRestoresItsOwnState()
    {
        var rig = BuildRig();
        SetInitial(rig, 10);
        rig.Recorder.Start(1);
        rig.Dispatcher.Dispatch(new AdjustIntensityCommand(new List<PatchedFixture> { rig.Fixture }, AdjustOperation.Absolute, 50));
        rig.Recorder.Stop();
        SetInitial(rig, 10);

        rig.Player.Play(1); // before=10 -> after=50
        SetInitial(rig, 20);
        rig.Player.Play(1); // before=20 -> after=50
        SetInitial(rig, 30);
        rig.Player.Play(1); // before=30 -> after=50
        Assert.Equal(Percent(50), CurrentValue(rig));

        Assert.True(rig.UndoRedo.Undo().Performed);
        Assert.Equal(Percent(30), CurrentValue(rig)); // 3rd play's own before-state

        Assert.True(rig.UndoRedo.Undo().Performed);
        Assert.Equal(Percent(20), CurrentValue(rig)); // 2nd play's own before-state

        Assert.True(rig.UndoRedo.Undo().Performed);
        Assert.Equal(Percent(10), CurrentValue(rig)); // 1st play's own before-state
    }

    // A Macro with multiple commands (a CompositeCommand, exactly what "Fixture 1 THRU 5 AT 70"
    // produces via CommandComposer), played twice, then sequential Undo - the composite's own
    // CreateFreshInstance() must recursively rebuild EVERY child fresh, not just the outer shell.
    [Fact]
    public void MacroWithMultipleCommands_PlayedTwice_ThenSequentialUndo_RestoresEachExecutionCorrectly()
    {
        var rig = BuildRig();
        var fixture2 = new PatchedFixture(Dimmer(), Dimmer().Modes[0], 0, 20) { Number = 2 };
        rig.Context.Patch.Add(fixture2);
        int idx2 = fixture2.AbsoluteIndex(fixture2.Mode.Channels[0]);

        SetInitial(rig, 10);
        rig.Context.Programmer.SetChannel(fixture2.UniverseId, idx2, Percent(10));

        rig.Recorder.Start(1);
        var composite = new Commands.CompositeCommand(new IConsoleCommand[]
        {
            new AdjustIntensityCommand(new List<PatchedFixture> { rig.Fixture }, AdjustOperation.Absolute, 50),
            new AdjustIntensityCommand(new List<PatchedFixture> { fixture2 }, AdjustOperation.Absolute, 60),
        });
        rig.Dispatcher.Dispatch(composite);
        rig.Recorder.Stop();

        SetInitial(rig, 10);
        rig.Context.Programmer.SetChannel(fixture2.UniverseId, idx2, Percent(10));

        rig.Player.Play(1); // play #1: before (10,10) -> after (50,60)
        SetInitial(rig, 25);
        rig.Context.Programmer.SetChannel(fixture2.UniverseId, idx2, Percent(35));
        rig.Player.Play(1); // play #2: before (25,35) -> after (50,60)

        Assert.Equal(Percent(50), CurrentValue(rig));
        Assert.True(rig.Context.Programmer.HasStoredValue(fixture2.UniverseId, idx2, out var v2));
        Assert.Equal(Percent(60), v2);

        Assert.True(rig.UndoRedo.Undo().Performed); // undoes play #2 as ONE composite Undo entry
        Assert.Equal(Percent(25), CurrentValue(rig));
        Assert.True(rig.Context.Programmer.HasStoredValue(fixture2.UniverseId, idx2, out var v2AfterFirstUndo));
        Assert.Equal(Percent(35), v2AfterFirstUndo);

        Assert.True(rig.UndoRedo.Undo().Performed); // undoes play #1
        Assert.Equal(Percent(10), CurrentValue(rig));
        Assert.True(rig.Context.Programmer.HasStoredValue(fixture2.UniverseId, idx2, out var v2AfterSecondUndo));
        Assert.Equal(Percent(10), v2AfterSecondUndo);
    }

    // The original recording-time command instance is never re-dispatched - only fresh instances
    // MacroPlaybackService itself creates ever reach CommandDispatcher.Dispatch.
    [Fact]
    public void OriginalRecordingTimeInstance_IsNeverRedispatched()
    {
        var rig = BuildRig();
        var original = new TrackingCommand();
        rig.Recorder.Start(1);
        rig.Dispatcher.Dispatch(original);
        var stopResult = rig.Recorder.Stop();

        Assert.Same(original, stopResult.Macro!.Steps[0].Command); // the template IS the original, for inspection only

        rig.Player.Play(1);
        rig.Player.Play(1);

        Assert.Equal(1, original.ExecuteCallCount); // the original was executed once, ever (at record time)
    }

    // Every playback creates a fresh, distinct command instance.
    [Fact]
    public void EveryPlayback_CreatesADistinctFreshCommandInstance()
    {
        var rig = BuildRig();
        var dispatchedInstances = new List<IConsoleCommand>();

        rig.Dispatcher.CommandExecuted += cmd => dispatchedInstances.Add(cmd);

        rig.Recorder.Start(1);
        rig.Dispatcher.Dispatch(new TrackingCommand());
        rig.Recorder.Stop();

        dispatchedInstances.Clear(); // ignore the recording-time dispatch itself

        rig.Player.Play(1);
        rig.Player.Play(1);
        rig.Player.Play(1);

        Assert.Equal(3, dispatchedInstances.Count);
        Assert.Equal(3, dispatchedInstances.Distinct().Count()); // three genuinely distinct objects, never the same reference twice
    }

    // Actions continue to preserve their normal non-undoable semantics across repeated playback -
    // no fresh-instance machinery needed/applied to Actions (they hold no per-execution state).
    [Fact]
    public void ActionSteps_AcrossRepeatedPlayback_RemainNonUndoable()
    {
        var rig = BuildRig();
        rig.Recorder.Start(1);
        rig.Dispatcher.DispatchAction(new ReleaseAllPlaybacksAction());
        rig.Recorder.Stop();

        rig.Player.Play(1);
        rig.Player.Play(1);
        rig.Player.Play(1);

        Assert.False(rig.UndoRedo.Undo().Performed); // nothing was ever pushed onto the Undo stack
    }

    // An operation that cannot be safely recreated for replay is explicitly reported as skipped,
    // never recorded (and thus never silently played back unsafely).
    [Fact]
    public void NonReplayableCommand_IsExplicitlyReportedAsSkipped_NeverRecorded()
    {
        var rig = BuildRig();
        var group = new FixtureGroup("Test Group", new[] { rig.Fixture }, number: 1);
        rig.Context.Groups.Groups.Add(group);

        rig.Recorder.Start(1);
        var createResult = rig.Dispatcher.Dispatch(new RemoveGroupCommand(group)); // does NOT implement IReplayableCommand
        Assert.True(createResult.Success);
        rig.Dispatcher.Dispatch(new AddFixtureToSelectionCommand(rig.Fixture)); // DOES implement it - for contrast
        var stopResult = rig.Recorder.Stop();

        Assert.NotNull(stopResult.Warning);
        Assert.Contains("RemoveGroupCommand", stopResult.Warning);
        Assert.Contains("not macro-safe", stopResult.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Single(stopResult.Macro!.Steps); // only the replayable AddFixtureToSelectionCommand was recorded
        Assert.IsType<AddFixtureToSelectionCommand>(stopResult.Macro.Steps[0].Command);
    }

    // MacroStep.ForCommand itself refuses to wrap a non-replayable command - defense in depth,
    // not solely relying on MacroRecorder's own check.
    [Fact]
    public void MacroStep_ForCommand_RejectsNonReplayableCommandDirectly()
    {
        var nonReplayable = new NonReplayableCommand();

        Assert.Throws<ArgumentException>(() => MacroStep.ForCommand(nonReplayable));
    }

    private sealed class TrackingCommand : IConsoleCommand, IReplayableCommand
    {
        public int ExecuteCallCount { get; private set; }

        public CommandResult Execute(ConsoleContext context)
        {
            ExecuteCallCount++;
            return new CommandResult { ActionType = ConsoleActionType.ToggleFixture };
        }

        public void Undo(ConsoleContext context) { }

        public IConsoleCommand CreateFreshInstance() => new TrackingCommand();
    }

    private sealed class NonReplayableCommand : IConsoleCommand
    {
        public CommandResult Execute(ConsoleContext context) => new() { ActionType = ConsoleActionType.ToggleFixture };
        public void Undo(ConsoleContext context) { }
    }
}

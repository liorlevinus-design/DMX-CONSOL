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

/// <summary>docs/COMMAND_SURFACE_KEY_SPEC.md MACROS §3/§11/§12 - the LEARN MACRO workflow.
/// MacroRecorder attaches to CommandDispatcher's own CommandExecuted/ActionExecuted events, so
/// these tests dispatch through the SAME dispatcher every real UI surface uses - no special
/// "recording mode" API on the commands/actions themselves.</summary>
public class MacroRecorderTests
{
    private static FixtureProfile Dimmer() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private sealed record Rig(ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, MacroRecorder Recorder, PatchedFixture Fixture);

    private static Rig BuildRig()
    {
        var patch = new Patch();
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(), engine, new PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        var recorder = new MacroRecorder(dispatcher, context.Macros);

        var fixture = new PatchedFixture(Dimmer(), Dimmer().Modes[0], 0, 1) { Number = 1 };
        context.Patch.Add(fixture);

        return new Rig(context, dispatcher, undoRedo, recorder, fixture);
    }

    // 1: LEARN MACRO + MACRO 1 starts recording Macro 1.
    [Fact]
    public void Start_BeginsRecordingIntoTheGivenSlot()
    {
        var rig = BuildRig();

        var result = rig.Recorder.Start(1);

        Assert.True(result.Success);
        Assert.True(rig.Recorder.IsRecording);
        Assert.Equal(1, rig.Recorder.RecordingSlot);
    }

    // 2 + 3: commands execute normally while recording, and recorded operations preserve order.
    [Fact]
    public void CommandsExecuteNormally_AndAreRecordedInOrder()
    {
        var rig = BuildRig();
        rig.Recorder.Start(1);

        var addResult = rig.Dispatcher.Dispatch(new AddFixtureToSelectionCommand(rig.Fixture));
        var atResult = rig.Dispatcher.Dispatch(new AdjustIntensityCommand(new List<PatchedFixture> { rig.Fixture }, AdjustOperation.Absolute, 50));

        Assert.True(addResult.Success);
        Assert.True(atResult.Success);
        Assert.Contains(rig.Fixture, rig.Context.Selection.Items); // executed normally, not deferred
        Assert.True(rig.Context.Programmer.HasStoredValue(rig.Fixture.UniverseId, 0, out var value));
        Assert.Equal((byte)Math.Round(50 / 100.0 * 255.0), value);

        var stopResult = rig.Recorder.Stop();

        Assert.NotNull(stopResult.Macro);
        Assert.Equal(2, stopResult.Macro!.Steps.Count);
        Assert.IsType<AddFixtureToSelectionCommand>(stopResult.Macro.Steps[0].Command);
        Assert.IsType<AdjustIntensityCommand>(stopResult.Macro.Steps[1].Command);
    }

    // 4: LEARN MACRO again stops/saves.
    [Fact]
    public void Stop_SavesTheMacroAndEndsRecording()
    {
        var rig = BuildRig();
        rig.Recorder.Start(2);
        rig.Dispatcher.Dispatch(new AddFixtureToSelectionCommand(rig.Fixture));

        var result = rig.Recorder.Stop();

        Assert.True(result.Success);
        Assert.False(rig.Recorder.IsRecording);
        Assert.Null(rig.Recorder.RecordingSlot);
        Assert.NotNull(rig.Context.Macros.FindBySlot(2));
        Assert.Single(rig.Context.Macros.FindBySlot(2)!.Steps);
    }

    // A failed dispatch is never recorded - nothing happened, nothing to replay.
    [Fact]
    public void FailedDispatch_IsNeverRecorded()
    {
        var rig = BuildRig();
        rig.Recorder.Start(1);

        var notInList = new DmxConsole.Core.Selection.FixtureGroup("Ghost", Array.Empty<PatchedFixture>()); // never added to rig.Context.Groups
        var failedResult = rig.Dispatcher.Dispatch(new RemoveGroupCommand(notInList));
        Assert.False(failedResult.Success);

        rig.Dispatcher.Dispatch(new AddFixtureToSelectionCommand(rig.Fixture)); // a genuine success, for contrast

        var stop = rig.Recorder.Stop();
        Assert.Single(stop.Macro!.Steps); // only the successful command was recorded
        Assert.IsType<AddFixtureToSelectionCommand>(stop.Macro.Steps[0].Command);
    }

    // Zero-step recording is still saved as a valid, empty Macro - never silently discarded.
    [Fact]
    public void Stop_WithNoStepsRecorded_StillSavesAnEmptyMacro()
    {
        var rig = BuildRig();
        rig.Recorder.Start(3);

        var result = rig.Recorder.Stop();

        Assert.True(result.Success);
        Assert.NotNull(rig.Context.Macros.FindBySlot(3));
        Assert.Empty(rig.Context.Macros.FindBySlot(3)!.Steps);
    }

    // 8: recording into an existing, non-empty Macro slot does not silently overwrite.
    [Fact]
    public void Start_IntoNonEmptySlot_IsRejectedWithoutConfirmation()
    {
        var rig = BuildRig();
        rig.Recorder.Start(1);
        rig.Dispatcher.Dispatch(new AddFixtureToSelectionCommand(rig.Fixture));
        rig.Recorder.Stop();
        var originalMacro = rig.Context.Macros.FindBySlot(1);

        var secondStart = rig.Recorder.Start(1);

        Assert.False(secondStart.Success);
        Assert.False(rig.Recorder.IsRecording);
        Assert.NotNull(secondStart.Macro);
        Assert.Same(originalMacro, rig.Context.Macros.FindBySlot(1)); // untouched
    }

    [Fact]
    public void Start_IntoNonEmptySlot_WithConfirmOverwrite_ReplacesIt()
    {
        var rig = BuildRig();
        rig.Recorder.Start(1);
        rig.Dispatcher.Dispatch(new AddFixtureToSelectionCommand(rig.Fixture));
        rig.Recorder.Stop();

        var confirmStart = rig.Recorder.Start(1, confirmOverwrite: true);
        Assert.True(confirmStart.Success);
        Assert.True(rig.Recorder.IsRecording);
        rig.Recorder.Stop();

        Assert.Empty(rig.Context.Macros.FindBySlot(1)!.Steps); // the new (empty) recording replaced the old one
    }

    // Starting a second recording while one is already active is rejected.
    [Fact]
    public void Start_WhileAlreadyRecording_IsRejected()
    {
        var rig = BuildRig();
        rig.Recorder.Start(1);

        var secondStart = rig.Recorder.Start(2);

        Assert.False(secondStart.Success);
        Assert.Equal(1, rig.Recorder.RecordingSlot); // original recording untouched
    }

    // 12: canceling a recording discards it entirely and restores the previous definition unchanged.
    [Fact]
    public void Cancel_DiscardsRecording_LeavesPreviousMacroUnchanged()
    {
        var rig = BuildRig();
        rig.Recorder.Start(1);
        rig.Dispatcher.Dispatch(new AddFixtureToSelectionCommand(rig.Fixture));
        rig.Recorder.Stop();
        var originalMacro = rig.Context.Macros.FindBySlot(1);

        rig.Recorder.Start(1, confirmOverwrite: true);
        rig.Dispatcher.Dispatch(new AdjustIntensityCommand(new List<PatchedFixture> { rig.Fixture }, AdjustOperation.Absolute, 80));
        var cancelResult = rig.Recorder.Cancel();

        Assert.True(cancelResult.Success);
        Assert.False(rig.Recorder.IsRecording);
        Assert.Same(originalMacro, rig.Context.Macros.FindBySlot(1)); // completely unchanged
    }

    // Recording stops observing the dispatcher once stopped/canceled - later dispatches are not
    // retroactively appended.
    [Fact]
    public void AfterStop_LaterDispatchesAreNotRecorded()
    {
        var rig = BuildRig();
        rig.Recorder.Start(1);
        rig.Dispatcher.Dispatch(new AddFixtureToSelectionCommand(rig.Fixture));
        var stopped = rig.Recorder.Stop();

        rig.Dispatcher.Dispatch(new ClearSelectionCommand());

        Assert.Single(stopped.Macro!.Steps); // the ClearSelectionCommand above is NOT in it
    }

    // 12: IConsoleAction is recorded distinctly from IConsoleCommand and never enters Undo history.
    [Fact]
    public void ActionExecuted_WhileRecording_IsRecordedAndNeverEntersUndoHistory()
    {
        var rig = BuildRig();
        rig.Recorder.Start(1);

        rig.Dispatcher.DispatchAction(new ReleaseAllPlaybacksAction());
        var stop = rig.Recorder.Stop();

        Assert.Single(stop.Macro!.Steps);
        Assert.Equal(MacroStepKind.Action, stop.Macro.Steps[0].Kind);
        Assert.IsType<ReleaseAllPlaybacksAction>(stop.Macro.Steps[0].Action);
        Assert.False(rig.UndoRedo.Undo().Performed); // nothing undoable happened
    }
}

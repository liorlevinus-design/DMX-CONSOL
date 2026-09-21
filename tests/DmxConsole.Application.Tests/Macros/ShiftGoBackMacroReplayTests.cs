using DmxConsole.Application.Commands.Playback;
using DmxConsole.Application.Macros;
using DmxConsole.Core.Engine;
using Xunit;

namespace DmxConsole.Application.Tests.Macros;

/// <summary>
/// SHIFT+GO/SHIFT+BACK slice, requirement 10: a Macro-recorded SHIFT+GO/SHIFT+BACK must replay
/// through the SAME shared playback path (GoAction/BackAction -> Executor -> CueList) and
/// preserve instant-navigation semantics on replay - never a second, simplified "macro playback"
/// mechanism. MacroPlaybackService replays the exact stored Action instance (Actions hold no
/// per-execution mutable state), so the `instant` flag baked into the GoAction/BackAction at
/// record time is exactly what runs again here - nothing macro-specific was added to make this
/// work.
/// </summary>
public class ShiftGoBackMacroReplayTests
{
    private static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, MacroRecorder Recorder, MacroPlaybackService Player, Executor Executor, CueList CueList) BuildRig()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(1);
        var executor = context.Executors.Add();
        var cueList = new CueList();
        var programmer = new Programmer();
        var options = CueStoreOptions.Default;
        cueList.RecordCue(context.Patch, programmer, context.Selection, context.EffectiveOutput, "Cue 1", 1, options);
        cueList.RecordCue(context.Patch, programmer, context.Selection, context.EffectiveOutput, "Cue 2", 2, options);
        executor.Assign(cueList);

        var recorder = new MacroRecorder(dispatcher, context.Macros);
        var player = new MacroPlaybackService(dispatcher, context.Macros, recorder);
        return (context, dispatcher, undoRedo, recorder, player, executor, cueList);
    }

    [Fact]
    public void RecordedShiftGo_ReplaysAsAnInstantJump_ThroughTheSharedPlaybackPath()
    {
        var (_, dispatcher, _, recorder, player, executor, cueList) = BuildRig();
        dispatcher.DispatchAction(new GoAction(executor)); // -> Cue 1 (normal, sets a starting point)

        recorder.Start(1);
        dispatcher.DispatchAction(new GoAction(executor, instant: true)); // SHIFT+GO, recorded
        recorder.Stop();

        // Undo the recorded move back to a known state, then replay the Macro.
        Assert.Equal(1, cueList.CurrentCueIndex);
        cueList.Back(); // plain Core-level rewind, not through Undo (GoAction is non-undoable anyway)
        Assert.Equal(0, cueList.CurrentCueIndex);

        var result = player.Play(1);

        Assert.True(result.Success);
        Assert.Equal(1, cueList.CurrentCueIndex); // replay reached Cue 2 again
        var (progress, remaining) = cueList.GetTransitionStatus();
        Assert.Equal(1.0, progress); // replay preserved INSTANT semantics - no fade in progress
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void RecordedShiftBack_ReplaysAsAnInstantJump_ThroughTheSharedPlaybackPath()
    {
        var (_, dispatcher, _, recorder, player, executor, cueList) = BuildRig();
        dispatcher.DispatchAction(new GoAction(executor));
        dispatcher.DispatchAction(new GoAction(executor)); // -> Cue 2

        recorder.Start(1);
        dispatcher.DispatchAction(new BackAction(executor, instant: true)); // SHIFT+BACK, recorded
        recorder.Stop();

        Assert.Equal(0, cueList.CurrentCueIndex);
        cueList.Go(); // rewind back to Cue 2 for the replay to have somewhere to move from
        Assert.Equal(1, cueList.CurrentCueIndex);

        var result = player.Play(1);

        Assert.True(result.Success);
        Assert.Equal(0, cueList.CurrentCueIndex); // replay reached Cue 1 again
        var (progress, remaining) = cueList.GetTransitionStatus();
        Assert.Equal(1.0, progress); // instant semantics preserved on replay
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void RecordedShiftGo_NeverEntersUndoHistory_OnReplayEither()
    {
        var (_, dispatcher, undoRedo, recorder, player, executor, _) = BuildRig();
        recorder.Start(1);
        dispatcher.DispatchAction(new GoAction(executor, instant: true));
        recorder.Stop();

        player.Play(1);

        Assert.False(undoRedo.Undo().Performed); // Actions never enter Undo history - true at record time AND replay
    }
}

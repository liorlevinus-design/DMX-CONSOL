using DmxConsole.Application.Commands.Executors;
using DmxConsole.Application.Commands.Playback;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Core.Engine;
using Xunit;

namespace DmxConsole.Application.Tests;

public class PlaybackActionTests
{
    private static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, Executor Executor) BuildWithExecutor()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(1);
        var executor = context.Executors.Add();
        var cueList = new CueList();
        var patch = context.Patch;
        var programmer = new Core.Engine.Programmer();
        var options = CueStoreOptions.Default;
        cueList.RecordCue(patch, programmer, context.Selection, context.EffectiveOutput, "Cue 1", 1, options);
        cueList.RecordCue(patch, programmer, context.Selection, context.EffectiveOutput, "Cue 2", 2, options);
        executor.Assign(cueList);
        return (context, dispatcher, undoRedo, executor);
    }

    private static IEnumerable<object[]> AllSevenActions(Executor executor) => new[]
    {
        new object[] { new GoAction(executor) },
        new object[] { new BackAction(executor) },
        new object[] { new StopAction(executor) },
        new object[] { new PauseAction(executor) },
        new object[] { new ResumeAction(executor) },
        new object[] { new FlashPressAction(executor) },
        new object[] { new FlashReleaseAction(executor) },
    };

    [Fact]
    public void GoAction_AdvancesTheAssignedCueList()
    {
        var (_, dispatcher, _, executor) = BuildWithExecutor();

        var result = dispatcher.DispatchAction(new GoAction(executor));

        Assert.True(result.Success);
        Assert.Equal(ConsoleActionType.Go, result.ActionType);
        Assert.True(((CueList)executor.Source!).IsActive);
    }

    [Fact]
    public void FlashPressThenRelease_TogglesFlashMode()
    {
        var (_, dispatcher, _, executor) = BuildWithExecutor();

        dispatcher.DispatchAction(new FlashPressAction(executor));
        Assert.Equal(FlashMode.Add, executor.Flash);

        dispatcher.DispatchAction(new FlashReleaseAction(executor));
        Assert.Equal(FlashMode.None, executor.Flash);
    }

    [Fact]
    public void UnassignedExecutor_ActionsFailGracefully_NeverThrow()
    {
        var executor = new Executor(1);
        var dispatcher = TestFixtures.BuildConsole(1).Dispatcher;

        var goResult = dispatcher.DispatchAction(new GoAction(executor));
        var pauseResult = dispatcher.DispatchAction(new PauseAction(executor));

        Assert.False(goResult.Success);
        Assert.False(pauseResult.Success);
    }

    [Fact]
    public void AllSevenPlaybackActions_NeverPushOntoTheUndoStack()
    {
        var (_, dispatcher, undoRedo, executor) = BuildWithExecutor();

        foreach (var args in AllSevenActions(executor))
        {
            var action = (Application.IConsoleAction)args[0];
            bool couldUndoBefore = undoRedo.CanUndo;

            dispatcher.DispatchAction(action);

            Assert.Equal(couldUndoBefore, undoRedo.CanUndo); // stack state unchanged either way
        }
    }

    [Fact]
    public void AllSevenPlaybackActions_NeverClearTheRedoStack()
    {
        var (context, dispatcher, undoRedo, executor) = BuildWithExecutor();

        // Populate one real undoable command, then Undo it so Redo has exactly one entry.
        var fixture = context.Patch.Fixtures[0];
        dispatcher.Dispatch(new ToggleFixtureCommand(fixture));
        undoRedo.Undo();
        Assert.True(undoRedo.CanRedo);

        foreach (var args in AllSevenActions(executor))
        {
            var action = (Application.IConsoleAction)args[0];
            dispatcher.DispatchAction(action);
            Assert.True(undoRedo.CanRedo); // still there after every single Action
        }

        undoRedo.Redo();
        Assert.Contains(fixture, context.Selection.Items); // Redo still reproduces the expected state
    }
}

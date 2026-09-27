using DmxConsole.Application.Commands;
using DmxConsole.Application.Commands.Cues;
using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Core.Engine;
using Xunit;

namespace DmxConsole.Application.Tests;

/// <summary>
/// N1 slice (CLAUDE.md §5): "after a successful STORE CUE, the entire Programmer is cleared
/// automatically" - now built on top of real, undoable Cue STORE/UPDATE/OVERWRITE commands
/// (StoreCueCommand for the create-only path, UpdateCueCommand for UPDATE/OVERWRITE), each wrapped
/// with ClearProgrammerCommand in one CompositeCommand so the Cue mutation and the Programmer clear
/// are ONE atomic, undoable transaction - never two, never partial.
/// </summary>
public class CueStoreCommandTests
{
    private static CompositeCommand StoreCueAndClear(CueList cueList, string name, double number, CueStoreOptions options) =>
        new(new IConsoleCommand[] { new StoreCueCommand(cueList, name, number, options), new ClearProgrammerCommand() });

    private static CompositeCommand UpdateCueAndClear(CueList cueList, Cue existing, string name, CueStoreOptions options, bool overwrite = false) =>
        new(new IConsoleCommand[] { new UpdateCueCommand(cueList, existing, name, options, overwrite), new ClearProgrammerCommand() });

    // ---------- STORE (new Cue) ----------

    [Fact]
    public void StoreCue_Success_CreatesCue_AndClearsProgrammer_ValuesKnockoutAndTiming()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(fixtureCount: 1);
        var fixture = context.Patch.Fixtures.Single();
        var cueList = new CueList();

        context.Programmer.SetChannel(0, 0, 200);
        context.Programmer.Knockout(0, 0);
        context.Programmer.SetTimeIn(0, 0, TimeSpan.FromSeconds(4));

        var result = dispatcher.Dispatch(StoreCueAndClear(cueList, "Look 1", 1, CueStoreOptions.Default));

        Assert.True(result.Success);
        Assert.NotNull(cueList.FindByNumber(1));
        Assert.False(context.Programmer.HasStoredValue(0, 0, out _));
        Assert.False(context.Programmer.IsKnockedOut(0, 0));
        Assert.False(context.Programmer.TryGetTiming(0, 0, out _, out _));

        // ONE Undo entry for the whole transaction.
        Assert.Equal(1, undoRedo.UndoCount);
    }

    [Fact]
    public void StoreCue_Undo_RemovesCue_AndRestoresFullProgrammerState_InOneUndo()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(fixtureCount: 1);
        var cueList = new CueList();

        context.Programmer.SetChannel(0, 0, 200);
        context.Programmer.Knockout(0, 0);
        context.Programmer.SetTimeIn(0, 0, TimeSpan.FromSeconds(4));
        context.Programmer.SetTimeOut(0, 0, TimeSpan.FromSeconds(2));

        dispatcher.Dispatch(StoreCueAndClear(cueList, "Look 1", 1, CueStoreOptions.Default));
        Assert.Equal(1, undoRedo.UndoCount);

        var proposal = undoRedo.PeekUndo()!;
        undoRedo.Undo(proposal.Options.Single(o => o.Risk == UndoRisk.Destructive).Id);

        Assert.Null(cueList.FindByNumber(1));
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(200, value);
        Assert.True(context.Programmer.IsKnockedOut(0, 0));
        Assert.True(context.Programmer.TryGetTiming(0, 0, out var timeIn, out var timeOut));
        Assert.Equal(TimeSpan.FromSeconds(4), timeIn);
        Assert.Equal(TimeSpan.FromSeconds(2), timeOut);
        Assert.Equal(0, undoRedo.UndoCount);
    }

    [Fact]
    public void StoreCue_Failure_WhenCueAlreadyExists_LeavesCueAndProgrammerUntouched_NothingOnUndoStack()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(fixtureCount: 1);
        var cueList = new CueList();
        cueList.RecordCue(context.Patch, context.Programmer, context.Selection, context.EffectiveOutput, "Existing", 1, CueStoreOptions.Default);

        context.Programmer.SetChannel(0, 0, 77);

        var result = dispatcher.Dispatch(StoreCueAndClear(cueList, "Attempted overwrite", 1, CueStoreOptions.Default));

        Assert.False(result.Success);
        Assert.Equal("Existing", cueList.FindByNumber(1)!.Name); // Cue untouched
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value)); // Programmer clear never ran
        Assert.Equal(77, value);
        Assert.Equal(0, undoRedo.UndoCount);
    }

    // ---------- UPDATE (existing Cue) ----------

    [Fact]
    public void UpdateCue_Success_ReplacesCueContent_AndClearsProgrammer()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(fixtureCount: 1);
        var cueList = new CueList();
        context.Programmer.SetChannel(0, 0, 100);
        var existing = cueList.RecordCue(context.Patch, context.Programmer, context.Selection, context.EffectiveOutput, "Old Name", 1, CueStoreOptions.Default);
        context.Programmer.ClearAll();

        context.Programmer.SetChannel(0, 0, 250);
        context.Programmer.Knockout(0, 0);
        context.Programmer.SetTimeIn(0, 0, TimeSpan.FromSeconds(3));

        var result = dispatcher.Dispatch(UpdateCueAndClear(cueList, existing, "New Name", CueStoreOptions.Default));

        Assert.True(result.Success);
        var updated = cueList.FindByNumber(1)!;
        Assert.Equal("New Name", updated.Name);
        Assert.Equal(ConsoleActionType.UpdateCue, result.ChildResults[0].ActionType);
        Assert.False(context.Programmer.HasStoredValue(0, 0, out _));
        Assert.False(context.Programmer.IsKnockedOut(0, 0));
        Assert.False(context.Programmer.TryGetTiming(0, 0, out _, out _));
        Assert.Equal(1, undoRedo.UndoCount);
    }

    [Fact]
    public void UpdateCue_Undo_RestoresExactPreviousCueContent_AndFullProgrammerState_InOneUndo()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(fixtureCount: 1);
        var cueList = new CueList();
        context.Programmer.SetChannel(0, 0, 100);
        var existing = cueList.RecordCue(context.Patch, context.Programmer, context.Selection, context.EffectiveOutput, "Old Name", 1, CueStoreOptions.Default);
        context.Programmer.ClearAll();

        // Programmer state at the moment of Update - must come back verbatim on Undo.
        context.Programmer.SetChannel(0, 0, 9);
        context.Programmer.SetTimeOut(0, 0, TimeSpan.FromSeconds(1.5));

        dispatcher.Dispatch(UpdateCueAndClear(cueList, existing, "New Name", CueStoreOptions.Default));
        Assert.Equal(1, undoRedo.UndoCount);

        undoRedo.Undo();

        var restored = cueList.FindByNumber(1)!;
        Assert.Same(existing, restored); // exact original Cue object put back, never a rebuilt copy
        Assert.Equal("Old Name", restored.Name);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(9, value);
        Assert.True(context.Programmer.TryGetTiming(0, 0, out _, out var timeOut));
        Assert.Equal(TimeSpan.FromSeconds(1.5), timeOut);
        Assert.Equal(0, undoRedo.UndoCount);
    }

    [Fact]
    public void UpdateCue_PreservesExistingMergeOutcome_FullReplacePerFilter_SameAsDirectCueListUpdateCue()
    {
        // Regression: UpdateCueCommand must reproduce CueList.UpdateCue's own outcome exactly -
        // proven by comparing a command-dispatched Update against a direct CueList.UpdateCue call
        // from separate, otherwise-identical starting states.
        var (contextA, dispatcherA, _) = TestFixtures.BuildConsole(fixtureCount: 1);
        var cueListA = new CueList();
        contextA.Programmer.SetChannel(0, 0, 55);
        var existingA = cueListA.RecordCue(contextA.Patch, contextA.Programmer, contextA.Selection, contextA.EffectiveOutput, "Old Name", 1, CueStoreOptions.Default);
        contextA.Programmer.ClearAll();
        contextA.Programmer.SetChannel(0, 0, 42);
        var viaCommand = dispatcherA.Dispatch(UpdateCueAndClear(cueListA, existingA, "Via Command", CueStoreOptions.Default));

        var (contextB, _, _) = TestFixtures.BuildConsole(fixtureCount: 1);
        var cueListB = new CueList();
        contextB.Programmer.SetChannel(0, 0, 55);
        var existingB = cueListB.RecordCue(contextB.Patch, contextB.Programmer, contextB.Selection, contextB.EffectiveOutput, "Old Name", 1, CueStoreOptions.Default);
        contextB.Programmer.ClearAll();
        contextB.Programmer.SetChannel(0, 0, 42);
        var viaDirectCall = cueListB.UpdateCue(existingB, contextB.Patch, contextB.Programmer, contextB.Selection, contextB.EffectiveOutput, "Via Command", CueStoreOptions.Default);

        Assert.True(viaCommand.Success);
        var updatedViaCommand = cueListA.FindByNumber(1)!;
        Assert.Equal(viaDirectCall!.Levels.Count, updatedViaCommand.Levels.Count);
        foreach (var (key, cueValue) in viaDirectCall.Levels)
        {
            Assert.True(updatedViaCommand.Levels.TryGetValue(key, out var otherValue));
            Assert.Equal(cueValue.Kind, otherValue.Kind);
            Assert.Equal(cueValue.ChannelType, otherValue.ChannelType);
            Assert.Equal(cueValue.AbsoluteValue, otherValue.AbsoluteValue);
            Assert.Equal(cueValue.PresetId, otherValue.PresetId);
        }
    }

    [Fact]
    public void UpdateCue_Failure_WhenCueNoLongerInList_LeavesProgrammerUntouched_NothingOnUndoStack()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(fixtureCount: 1);
        var cueList = new CueList();
        var existing = cueList.RecordCue(context.Patch, context.Programmer, context.Selection, context.EffectiveOutput, "Old Name", 1, CueStoreOptions.Default);
        cueList.RemoveCue(existing); // now stale

        context.Programmer.SetChannel(0, 0, 5);

        var result = dispatcher.Dispatch(UpdateCueAndClear(cueList, existing, "New Name", CueStoreOptions.Default));

        Assert.False(result.Success);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(5, value); // Programmer clear never ran
        Assert.Equal(0, undoRedo.UndoCount);
    }

    // ---------- OVERWRITE (new functionality; today identical outcome to UPDATE - see UpdateCueCommand's doc comment) ----------

    [Fact]
    public void OverwriteCue_Success_ReplacesCueContent_AndClearsProgrammer_ReportsOverwriteActionType()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(fixtureCount: 1);
        var cueList = new CueList();
        context.Programmer.SetChannel(0, 0, 100);
        var existing = cueList.RecordCue(context.Patch, context.Programmer, context.Selection, context.EffectiveOutput, "Old Name", 1, CueStoreOptions.Default);
        context.Programmer.ClearAll();

        context.Programmer.SetChannel(0, 0, 250);

        var result = dispatcher.Dispatch(UpdateCueAndClear(cueList, existing, "Overwritten", CueStoreOptions.Default, overwrite: true));

        Assert.True(result.Success);
        Assert.Equal(ConsoleActionType.OverwriteCue, result.ChildResults[0].ActionType);
        Assert.Equal("Overwritten", cueList.FindByNumber(1)!.Name);
        Assert.False(context.Programmer.HasStoredValue(0, 0, out _));
        Assert.Equal(1, undoRedo.UndoCount);
    }

    [Fact]
    public void OverwriteCue_Undo_RestoresExactPreviousCueContent_AndProgrammer_InOneUndo()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(fixtureCount: 1);
        var cueList = new CueList();
        context.Programmer.SetChannel(0, 0, 100);
        var existing = cueList.RecordCue(context.Patch, context.Programmer, context.Selection, context.EffectiveOutput, "Old Name", 1, CueStoreOptions.Default);
        context.Programmer.ClearAll();
        context.Programmer.SetChannel(0, 0, 17);

        dispatcher.Dispatch(UpdateCueAndClear(cueList, existing, "Overwritten", CueStoreOptions.Default, overwrite: true));
        Assert.Equal(1, undoRedo.UndoCount);

        undoRedo.Undo();

        Assert.Same(existing, cueList.FindByNumber(1));
        Assert.Equal("Old Name", cueList.FindByNumber(1)!.Name);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(17, value);
        Assert.Equal(0, undoRedo.UndoCount);
    }

    // ---------- CANCEL (no command ever built/dispatched) ----------

    [Fact]
    public void Cancel_NeverDispatched_LeavesCueAndProgrammerCompletelyUnchanged()
    {
        // CANCEL structurally means "no command is ever constructed or dispatched" - there is no
        // separate CancelCueStoreCommand; proving that a Programmer value and an un-mutated Cue
        // survive when nothing at all is dispatched is the whole of CANCEL's contract.
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(fixtureCount: 1);
        var cueList = new CueList();
        var existing = cueList.RecordCue(context.Patch, context.Programmer, context.Selection, context.EffectiveOutput, "Old Name", 1, CueStoreOptions.Default);
        context.Programmer.ClearAll();
        context.Programmer.SetChannel(0, 0, 33);

        // operator declines - nothing dispatched

        Assert.Same(existing, cueList.FindByNumber(1));
        Assert.Equal("Old Name", cueList.FindByNumber(1)!.Name);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(33, value);
        Assert.Equal(0, undoRedo.UndoCount);
    }

    // ---------- Parameter TIME regression (immediately-prior slice) ----------

    [Fact]
    public void StoreCue_CapturesPerChannelTimingOverride_BeforeProgrammerIsCleared()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 1);
        var cueList = new CueList();
        context.Programmer.SetChannel(0, 0, 128);
        context.Programmer.SetTimeIn(0, 0, TimeSpan.FromSeconds(7));
        context.Programmer.SetTimeOut(0, 0, TimeSpan.FromSeconds(3));

        dispatcher.Dispatch(StoreCueAndClear(cueList, "Timed", 1, CueStoreOptions.Default));

        var cue = cueList.FindByNumber(1)!;
        var entry = Assert.Single(cue.Levels.Values);
        Assert.Equal(TimeSpan.FromSeconds(7), entry.TimeInOverride);
        Assert.Equal(TimeSpan.FromSeconds(3), entry.TimeOutOverride);
    }
}

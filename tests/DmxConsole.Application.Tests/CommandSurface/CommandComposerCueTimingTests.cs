using DmxConsole.Application.CommandSurface;
using DmxConsole.Application.Commands;
using DmxConsole.Application.Commands.Cues;
using DmxConsole.Core.Engine;
using Xunit;

namespace DmxConsole.Application.Tests.CommandSurface;

/// <summary>
/// Cue-timing slice: "CUE &lt;n&gt; [THRU &lt;n&gt;] TIME &lt;value&gt;[/&lt;value&gt;] ENTER" is
/// the ONE implemented CUE-numeric grammar branch (everything else after a Cue number/range stays
/// an honest "not implemented" gap - see CommandComposer.ResolveCueCommand's own doc comment).
/// Never touches TriggerMode/WaitTime (Cue Trigger Semantics, CLAUDE.md §9) and never introduces
/// any AttributeClass-level timing storage (CLAUDE.md's "Known contradiction" callout) - only
/// CueTiming.TimeIn/TimeOut are ever written here, via the shared SetCueTimingCommand.
/// </summary>
public class CommandComposerCueTimingTests
{
    private static (Application.ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, CueList CueList) BuildRig()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(fixtureCount: 0);
        var cueList = new CueList();
        context.PrimaryCueList = cueList;
        return (context, dispatcher, undoRedo, cueList);
    }

    private static Cue RecordCue(CueList cueList, Application.ConsoleContext context, double number) =>
        cueList.RecordCue(context.Patch, context.Programmer, context.Selection, context.EffectiveOutput,
            $"Cue {number}", number, CueStoreOptions.Default);

    // ---------- Required syntax: scalar TIME ----------

    [Fact]
    public void CueTime_ScalarValue_SetsBothInAndOut()
    {
        var (context, dispatcher, _, cueList) = BuildRig();
        var cue = RecordCue(cueList, context, 1);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(10));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        Assert.IsType<SetCueTimingCommand>(final.ReadyOperation);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(10), cue.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(10), cue.Timing.TimeOut);
    }

    [Fact]
    public void CueTime_DecimalScalarValue_SupportsFractionalSeconds()
    {
        var (context, dispatcher, _, cueList) = BuildRig();
        var cue = RecordCue(cueList, context, 1);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(10.5));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(10.5), cue.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(10.5), cue.Timing.TimeOut);
    }

    // ---------- Required syntax: slash-split In/Out ----------

    [Fact]
    public void CueTime_SlashSplitValue_SetsInAndOutSeparately()
    {
        var (context, dispatcher, _, cueList) = BuildRig();
        var cue = RecordCue(cueList, context, 1);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(8));
        composer.Push(CommandToken.Simple(CommandTokenKind.Slash));
        composer.Push(CommandToken.Number(10));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(8), cue.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(10), cue.Timing.TimeOut);
    }

    // ---------- Required syntax: THRU range, scalar ----------

    [Fact]
    public void CueTime_ThruRange_ScalarValue_AppliesToEveryMatchedCue_AsOneAtomicUndo()
    {
        var (context, dispatcher, undoRedo, cueList) = BuildRig();
        var cue1 = RecordCue(cueList, context, 1);
        var cue2 = RecordCue(cueList, context, 2);
        var cue3 = RecordCue(cueList, context, 3);
        // Both outside the CUE 1 THRU 3 range below - must stay untouched.
        var cue5 = RecordCue(cueList, context, 5);
        var cue6 = RecordCue(cueList, context, 6);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(3));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(10));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        Assert.IsType<CompositeCommand>(final.ReadyOperation);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        Assert.Equal(TimeSpan.FromSeconds(10), cue1.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(10), cue2.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(10), cue3.Timing.TimeIn);
        // Outside the range - untouched.
        Assert.Equal(CueTiming.Default.TimeIn, cue5.Timing.TimeIn);
        Assert.Equal(CueTiming.Default.TimeIn, cue6.Timing.TimeIn);

        // One Undo step reverts the whole range - never N separate Undo entries (CLAUDE.md §14).
        var outcome = undoRedo.Undo();
        Assert.True(outcome.Performed);
        Assert.Equal(CueTiming.Default.TimeIn, cue1.Timing.TimeIn);
        Assert.Equal(CueTiming.Default.TimeIn, cue2.Timing.TimeIn);
        Assert.Equal(CueTiming.Default.TimeIn, cue3.Timing.TimeIn);
    }

    // ---------- Required syntax: THRU range, slash-split ----------

    [Fact]
    public void CueTime_ThruRange_SlashSplitValue_AppliesInOutToWholeRange()
    {
        var (context, dispatcher, _, cueList) = BuildRig();
        var cue1 = RecordCue(cueList, context, 1);
        var cue2 = RecordCue(cueList, context, 2);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(2));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(8));
        composer.Push(CommandToken.Simple(CommandTokenKind.Slash));
        composer.Push(CommandToken.Number(10));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        Assert.Equal(TimeSpan.FromSeconds(8), cue1.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(10), cue1.Timing.TimeOut);
        Assert.Equal(TimeSpan.FromSeconds(8), cue2.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(10), cue2.Timing.TimeOut);
    }

    // ---------- Trigger semantics untouched ----------

    [Fact]
    public void CueTime_NeverTouchesTriggerModeOrWaitTime()
    {
        var (context, dispatcher, _, cueList) = BuildRig();
        var cue = RecordCue(cueList, context, 1);
        cueList.SetTriggerMode(cue, CueTriggerMode.Wait);
        cue.WaitTime = TimeSpan.FromSeconds(4);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(10));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(CueTriggerMode.Wait, cue.TriggerMode);
        Assert.Equal(TimeSpan.FromSeconds(4), cue.WaitTime);
    }

    // ---------- Invalid syntax: at least 3 distinct cases ----------

    [Fact]
    public void CueTime_MissingValue_AtEnter_FailsExplicitly_NoMutation()
    {
        var (context, _, _, cueList) = BuildRig();
        var cue = RecordCue(cueList, context, 1);
        var originalTiming = cue.Timing;

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.Equal("TIME VALUE IS MISSING", final.Error);
        Assert.Null(final.ReadyOperation);
        Assert.Equal(originalTiming, cue.Timing);
    }

    [Fact]
    public void CueTime_DanglingSlash_WithNoOutValue_FailsExplicitly_NoMutation()
    {
        var (context, _, _, cueList) = BuildRig();
        var cue = RecordCue(cueList, context, 1);
        var originalTiming = cue.Timing;

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(8));
        composer.Push(CommandToken.Simple(CommandTokenKind.Slash));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Null(final.ReadyOperation);
        Assert.Equal(originalTiming, cue.Timing);
    }

    [Fact]
    public void CueTime_LeadingSlash_WithNoInValue_FailsExplicitly_NoMutation()
    {
        var (context, _, _, cueList) = BuildRig();
        var cue = RecordCue(cueList, context, 1);
        var originalTiming = cue.Timing;

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Simple(CommandTokenKind.Slash));
        var final = composer.Push(CommandToken.Number(10));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Null(final.ReadyOperation);
        Assert.Equal(originalTiming, cue.Timing);
    }

    [Fact]
    public void CueTime_DoubleSlash_MalformedSplit_FailsExplicitly_NoMutation()
    {
        var (context, _, _, cueList) = BuildRig();
        var cue = RecordCue(cueList, context, 1);
        var originalTiming = cue.Timing;

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(8));
        composer.Push(CommandToken.Simple(CommandTokenKind.Slash));
        composer.Push(CommandToken.Number(10));
        composer.Push(CommandToken.Simple(CommandTokenKind.Slash));
        var final = composer.Push(CommandToken.Number(12));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Null(final.ReadyOperation);
        Assert.Equal(originalTiming, cue.Timing);
    }

    [Fact]
    public void CueTime_ExtremelyLargeValue_FailsExplicitly_NoMutation_NeverThrows()
    {
        var (context, _, _, cueList) = BuildRig();
        var cue = RecordCue(cueList, context, 1);
        var originalTiming = cue.Timing;

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(999999999999999));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Null(final.ReadyOperation);
        Assert.Equal(originalTiming, cue.Timing);
    }

    [Fact]
    public void CueTime_ThruRange_NoCuesInRange_FailsExplicitly()
    {
        var (context, _, _, cueList) = BuildRig();
        RecordCue(cueList, context, 10);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(5));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(10));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Null(final.ReadyOperation);
    }

    [Fact]
    public void CueTime_SingleCueNotFound_FailsExplicitly()
    {
        var (context, _, _, cueList) = BuildRig();
        RecordCue(cueList, context, 2);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(10));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.Equal("Cue 1 not found.", final.Error);
    }

    // ---------- ExpectedNext (Command Surface UI gating - Cue-timing UI bug fix slice) ----------
    // These prove the exact ExpectedNext contents a UI surface (razor, keyboard, softkey) relies
    // on to enable/disable buttons - CommandComposer is the single source of truth for grammar
    // continuations, never re-guessed in CommandSurface.razor/CommandSurfaceViewModel.

    [Fact]
    public void ExpectedNext_AfterCueNumber_IncludesThruAndTiming()
    {
        var (context, _, _, cueList) = BuildRig();
        RecordCue(cueList, context, 1);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        var afterNumber = composer.Push(CommandToken.Number(1));

        Assert.Contains(CommandTokenKind.Thru, afterNumber.ExpectedNext);
        Assert.Contains(CommandTokenKind.Timing, afterNumber.ExpectedNext);
    }

    [Fact]
    public void ExpectedNext_AfterCueThruRange_IncludesTiming()
    {
        var (context, _, _, cueList) = BuildRig();
        RecordCue(cueList, context, 1);
        RecordCue(cueList, context, 5);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        var afterRange = composer.Push(CommandToken.Number(5));

        Assert.Contains(CommandTokenKind.Timing, afterRange.ExpectedNext);
    }

    [Fact]
    public void ExpectedNext_AfterTimeToken_IsNumberOnly()
    {
        var (context, _, _, cueList) = BuildRig();
        RecordCue(cueList, context, 1);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        var afterTime = composer.Push(CommandToken.Simple(CommandTokenKind.Timing));

        Assert.Equal(new[] { CommandTokenKind.Number }, afterTime.ExpectedNext);
    }

    [Fact]
    public void ExpectedNext_AfterSingleTimeValue_IncludesEnterAndSlash()
    {
        var (context, _, _, cueList) = BuildRig();
        RecordCue(cueList, context, 1);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        var afterValue = composer.Push(CommandToken.Number(8));

        Assert.Contains(CommandTokenKind.Enter, afterValue.ExpectedNext);
        Assert.Contains(CommandTokenKind.Slash, afterValue.ExpectedNext);
    }

    [Fact]
    public void ExpectedNext_AfterSlash_IsNumberOnly()
    {
        var (context, _, _, cueList) = BuildRig();
        RecordCue(cueList, context, 1);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(8));
        var afterSlash = composer.Push(CommandToken.Simple(CommandTokenKind.Slash));

        Assert.Equal(new[] { CommandTokenKind.Number }, afterSlash.ExpectedNext);
    }

    [Fact]
    public void ExpectedNext_AfterOutValue_IsEnterOnly_NoSecondSlash()
    {
        var (context, _, _, cueList) = BuildRig();
        RecordCue(cueList, context, 1);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(8));
        composer.Push(CommandToken.Simple(CommandTokenKind.Slash));
        var afterOut = composer.Push(CommandToken.Number(10));

        Assert.Equal(new[] { CommandTokenKind.Enter }, afterOut.ExpectedNext);
    }

    [Fact]
    public void CueTime_SomethingElseAfterCueNumber_IsHonestlyNotImplemented_NeverGuessed()
    {
        var (context, _, _, cueList) = BuildRig();
        RecordCue(cueList, context, 1);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(1));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Trigger));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Null(final.ReadyOperation);
    }
}

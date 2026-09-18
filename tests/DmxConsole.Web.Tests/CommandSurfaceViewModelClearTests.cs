using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;
using DmxConsole.Web.EditorToolBar;
using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>docs/COMMAND_SURFACE_KEY_SPEC.md §15 as redefined for this slice - CLEAR immediately
/// clears the CURRENT Fixture Selection and nothing else: no CLEAR CLEAR, no gesture step-back, no
/// command-line editing (that is Backspace), no Programmer/Editor or EditorContextStack mutation,
/// and never an Undo entry (it is dispatched as a non-undoable IConsoleAction).</summary>
public class CommandSurfaceViewModelClearTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, EditorContextStack EditorContext, CommandSurfaceViewModel Surface) BuildRig(int fixtureCount)
    {
        var patch = new Patch();
        var profile = Dimmer1();
        for (int i = 1; i <= fixtureCount; i++)
            patch.Add(new PatchedFixture(profile, profile.Modes[0], 0, i, $"F{i}"));
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        var editorContext = new EditorContextStack();
        var surface = new CommandSurfaceViewModel(context, dispatcher, editorContext);
        return (context, dispatcher, undoRedo, editorContext, surface);
    }

    /// <summary>CLEAR immediately clears the ENTIRE current selection in one press - it is not a
    /// per-gesture step-back. Three separate Fixture selection gestures (1, then 2, then 3 - no Plus
    /// needed while the cycle is open) must all disappear together.</summary>
    [Fact]
    public void Clear_ImmediatelyClearsTheWholeSelection_NotOneGestureAtATime()
    {
        var (context, _, _, _, surface) = BuildRig(3);

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);
        surface.PressDigit('2');
        surface.PressToken(CommandTokenKind.Enter);
        surface.PressDigit('3');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Equal(3, context.Selection.Items.Count);

        surface.PressClear();

        Assert.Empty(context.Selection.Items);
    }

    /// <summary>A whole Group gesture must also disappear with the one press that clears the
    /// selection - never fixture-by-fixture, and never "one gesture per CLEAR".</summary>
    [Fact]
    public void Clear_RemovesAWholeGroupGestureImmediately_AsOneUnit()
    {
        var (context, _, _, _, surface) = BuildRig(5);

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('3');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Single(context.Selection.Items);

        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 1));
        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 2));
        context.Groups.CreateFromSelection("G1", context.Selection, number: 1);
        context.Selection.Clear();
        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 3)); // Fixture 3's own gesture

        surface.PressToken(CommandTokenKind.Group);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Equal(3, context.Selection.Items.Count); // Fixture 3 + Group 1's two members

        surface.PressClear();

        Assert.Empty(context.Selection.Items);
    }

    /// <summary>CLEAR must not modify the CommandComposer and must not modify pending digits: the
    /// partially-typed command line survives CLEAR completely intact (Backspace is the single
    /// command-line editor).</summary>
    [Fact]
    public void Clear_LeavesCompositionAndPendingDigitsUntouched()
    {
        var (context, _, _, _, surface) = BuildRig(3);
        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 2));

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.At);
        surface.PressDigit('5');
        Assert.Equal("FIXTURE 1 AT 5", surface.DisplayPreview.ToUpperInvariant());
        var tokenKindsBefore = surface.Current.Tokens.Select(t => t.Kind).ToArray();

        surface.PressClear();

        Assert.Equal(tokenKindsBefore, surface.Current.Tokens.Select(t => t.Kind).ToArray());
        Assert.Equal("FIXTURE 1 AT 5", surface.DisplayPreview.ToUpperInvariant());
        Assert.Empty(context.Selection.Items); // the selection itself did clear
    }

    /// <summary>CLEAR touches Selection and nothing else: Programmer/Editor values and the shared
    /// EditorContextStack are both left exactly as they were ("CLEAR is not Release").</summary>
    [Fact]
    public void Clear_LeavesProgrammerAndEditorContextUntouched()
    {
        var (context, _, _, editorContext, surface) = BuildRig(2);

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Single(context.Selection.Items);
        Assert.False(editorContext.IsAtRoot); // PressToken(Fixture) entered the shared FIXTURE context
        int framesBefore = editorContext.Frames.Count;
        string labelBefore = editorContext.Current.Label;

        context.Programmer.SetChannel(0, 0, 200); // an Editor value CLEAR must never touch

        surface.PressClear();

        Assert.Empty(context.Selection.Items);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(200, value);
        Assert.Equal(framesBefore, editorContext.Frames.Count);
        Assert.Equal(labelBefore, editorContext.Current.Label);
    }

    /// <summary>CLEAR is not Undo: it never enters Undo history and never clears the Redo stack,
    /// because it is applied as a non-undoable IConsoleAction (CommandDispatcher.DispatchAction).</summary>
    [Fact]
    public void Clear_DoesNotEnterUndoHistory_AndDoesNotClearRedo()
    {
        var (context, _, undoRedo, _, surface) = BuildRig(3);

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.True(undoRedo.CanUndo);

        undoRedo.Undo(); // the selection command is now on the Redo stack and the Undo stack is empty
        Assert.True(undoRedo.CanRedo);
        Assert.False(undoRedo.CanUndo);

        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 2)); // a real selection to clear

        surface.PressClear();

        Assert.Empty(context.Selection.Items); // CLEAR did clear the selection
        Assert.False(undoRedo.CanUndo);        // ... without becoming an Undo entry
        Assert.True(undoRedo.CanRedo);         // ... and without clearing Redo
    }

    /// <summary>There is no CLEAR CLEAR: repeated presses are simply the same immediate clear again -
    /// no escalation, no error, and (critically) no effect on the Editor the way a second-press
    /// escalation would have had.</summary>
    [Fact]
    public void Clear_RepeatedPresses_HaveNoSecondPressBehavior()
    {
        var (context, _, _, _, surface) = BuildRig(3);

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Thru);
        surface.PressDigit('3');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Equal(3, context.Selection.Items.Count);
        context.Programmer.SetChannel(0, 0, 123); // an Editor value a CLEAR CLEAR escalation would have wiped

        surface.PressClear();
        Assert.Empty(context.Selection.Items);

        surface.PressClear();
        Assert.Empty(context.Selection.Items);
        Assert.Null(surface.DispatchError);

        surface.PressClear();
        Assert.Empty(context.Selection.Items);
        Assert.Null(surface.DispatchError);

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(123, value);
    }

    /// <summary>CLEAR is selection-only: a live Shift arm belongs to SHIFT's own key, so CLEAR must
    /// not silently consume it.</summary>
    [Fact]
    public void Clear_DoesNotModifyShiftArmed()
    {
        var (context, _, _, _, surface) = BuildRig(2);
        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 1));

        surface.PressShift();
        Assert.True(surface.ShiftArmed);

        surface.PressClear();

        Assert.Empty(context.Selection.Items); // CLEAR really ran
        Assert.True(surface.ShiftArmed);       // ... and left the Shift arm alone
    }

    /// <summary>CLEAR is selection-only: BOTH LEARN MACRO states - armed (waiting for a slot) and
    /// actively recording - must survive CLEAR untouched.</summary>
    [Fact]
    public void Clear_DoesNotModifyLearnMacroState()
    {
        var (context, _, _, _, surface) = BuildRig(2);

        // (a) armed, waiting for a MACRO key to pick the slot.
        surface.PressLearnMacro();
        Assert.True(surface.IsLearnArmed);

        surface.PressClear();

        Assert.True(surface.IsLearnArmed);
        Assert.False(surface.IsRecordingMacro);

        // (b) actively recording into slot 1.
        surface.PressMacroSlot(1);
        Assert.True(surface.IsRecordingMacro);
        Assert.Equal(1, surface.RecordingMacroSlot);
        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 1));

        surface.PressClear();

        Assert.Empty(context.Selection.Items); // CLEAR really ran
        Assert.True(surface.IsRecordingMacro);
        Assert.Equal(1, surface.RecordingMacroSlot);

        surface.CancelLearnMacro(); // the recording was genuinely live and is still cancellable
        Assert.False(surface.IsRecordingMacro);
        Assert.Null(surface.RecordingMacroSlot);
        Assert.Null(context.Macros.FindBySlot(1));
    }

    // DEFERRED (RELEASE-panel slice): "CLEAR does not explicitly alter RELEASE state" is
    // deliberately NOT tested here. The only observable consequence of that guarantee today is the
    // legacy two-press escalation RELEASE -> CLEAR -> ENTER clearing the ENTIRE Editor globally,
    // and that behavior is NOT authoritative - the agreed future semantics are:
    //   RELEASE -> ENTER (no family selected)    = release ALL Editor values for the CURRENT Selection
    //   RELEASE -> RELEASE (no family selected)  = Clear Entire Editor globally
    // Freezing the legacy result in a test here would have to be un-frozen (or would silently block)
    // that refactor. _releaseArmedForFullClear is also private with no clean state-level accessor, so
    // there is no way to assert "CLEAR left it alone" without asserting the legacy escalation itself.
    // Re-add a proper regression guard in the RELEASE-panel slice, against the semantics above.

    /// <summary>CLEAR must not be macro-recorded at all: while a Macro is recording, CLEAR still
    /// clears the selection but contributes NO step, so replaying that Macro can never wipe the
    /// operator's selection.</summary>
    [Fact]
    public void Clear_WhileRecordingAMacro_IsNotRecorded()
    {
        var (context, _, _, _, surface) = BuildRig(3);

        surface.PressLearnMacro(); // start macro learning
        surface.PressMacroSlot(1); // choose slot 1 -> actively recording
        Assert.True(surface.IsRecordingMacro);

        surface.PressToken(CommandTokenKind.Fixture); // one ordinary, recordable step
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Single(context.Selection.Items);

        surface.PressClear(); // must NOT be recorded
        Assert.Empty(context.Selection.Items); // ... but must still really clear

        surface.PressLearnMacro(); // stop/save

        var macro = context.Macros.FindBySlot(1);
        Assert.NotNull(macro);
        Assert.Single(macro!.Steps); // only the Fixture selection step - CLEAR added nothing
        Assert.DoesNotContain(macro.Steps, step => step.Action is ClearSelectionAction);
    }
}

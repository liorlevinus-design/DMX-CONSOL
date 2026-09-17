using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;
using DmxConsole.Web.EditorToolBar;
using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>docs/COMMAND_SURFACE_KEY_SPEC.md §15 - CLEAR's full priority hierarchy: digit
/// backspace (covered in CommandSurfaceViewModelTests), then last-gesture removal (atomic even
/// for a whole Group), then CLEAR CLEAR for the entire selection, and the explicit "CLEAR is
/// never Undo and never Release" boundary.</summary>
public class CommandSurfaceViewModelClearTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, CommandSurfaceViewModel Surface) BuildRig(int fixtureCount)
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
        var surface = new CommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
        return (context, dispatcher, undoRedo, surface);
    }

    /// <summary>Selects Fixture 1, then adds Fixture 2 as a SEPARATE gesture (each a distinct
    /// "1 Enter" / "+2 Enter" command) - CLEAR must remove only the second gesture (Fixture 2),
    /// leaving Fixture 1 selected, never touching Selection fixture-by-fixture via any other path.</summary>
    [Fact]
    public void Clear_OnEmptyLine_RemovesOnlyTheLastGesture_NotFixtureByFixture()
    {
        var (context, _, _, surface) = BuildRig(3);

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Single(context.Selection.Items);

        // A second, separate command (no Plus needed - ReplaceSelectionOnResolve is already
        // false after the first non-AT command, so a plain bare number here ADDS rather than
        // replacing) - this is its own distinct gesture.
        surface.PressDigit('2');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Equal(2, context.Selection.Items.Count);

        surface.PressClear();

        Assert.Single(context.Selection.Items);
        Assert.Equal(1, context.Selection.Items[0].Number);
    }

    /// <summary>A Group gesture must disappear as ONE unit on a single CLEAR - not one CLEAR
    /// per member fixture. Fixture 3 (selected via a prior, separate gesture) must survive.</summary>
    [Fact]
    public void Clear_RemovesAWholeGroupGesture_AsOneUnit_NotFixtureByFixture()
    {
        var (context, _, _, surface) = BuildRig(5);

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('3');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Single(context.Selection.Items);

        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 1));
        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 2));
        var group = context.Groups.CreateFromSelection("G1", context.Selection, number: 1);
        context.Selection.Clear();
        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 3)); // restore prior gesture's result

        surface.PressToken(CommandTokenKind.Group);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Equal(3, context.Selection.Items.Count); // Fixture 3 + Group 1's two members

        surface.PressClear(); // must remove BOTH group members in one press

        Assert.Single(context.Selection.Items);
        Assert.Equal(3, context.Selection.Items[0].Number);
    }

    [Fact]
    public void ClearClear_OnEmptyLine_ClearsTheEntireSelection()
    {
        var (context, _, _, surface) = BuildRig(3);

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Thru);
        surface.PressDigit('3');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Equal(3, context.Selection.Items.Count);

        surface.PressClear(); // first CLEAR: removes the (single, whole-range) gesture
        Assert.Empty(context.Selection.Items);

        // Re-select so the second CLEAR has something real to prove it clears (not a no-op
        // because the first CLEAR already emptied it via gesture-removal in this scenario).
        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('2');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Single(context.Selection.Items);

        surface.PressClear(); // CLEAR 1: removes the last gesture (Fixture 2)
        surface.PressClear(); // CLEAR 2 (consecutive, empty line both times): full ClearSelectionCommand

        Assert.Empty(context.Selection.Items);
    }

    [Fact]
    public void Clear_NeverPushesToUndoStack_AndNeverDispatchesRelease()
    {
        var (context, _, undoRedo, surface) = BuildRig(2);

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);
        context.Programmer.SetChannel(0, 0, 200); // an Editor value CLEAR must never touch

        surface.PressClear(); // gesture removal
        surface.PressClear(); // full selection clear (CLEAR CLEAR)

        // CLEAR is not Undo: Undo() must still revert the ORIGINAL selection command, not some
        // Clear-pushed command (CLEAR never enters the undo stack at all in this ViewModel - its
        // mutations go through Dispatch, which IS undoable, but CLEAR itself never calls Undo()).
        var outcome = undoRedo.Undo();
        Assert.True(outcome.Performed);

        // CLEAR is not Release: the Editor/Programmer value set above must survive both CLEARs -
        // only RELEASE (a completely different key) touches Programmer values.
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(200, value);
    }
}

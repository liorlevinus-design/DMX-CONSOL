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

/// <summary>docs/COMMAND_SURFACE_KEY_SPEC.md MACROS slice - the Command Surface's own LEARN
/// MACRO/MACRO 1-4 physical keys, exercised through CommandSurfaceViewModel exactly as the
/// touch buttons in CommandSurface.razor call them (see that component's @onclick bindings) -
/// proving item 17's "UI buttons route through the same ViewModel/Application path" directly,
/// since there is no separate business logic anywhere else for these keys to go through.</summary>
public class CommandSurfaceViewModelMacroTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private static (ConsoleContext Context, CommandSurfaceViewModel Surface) BuildRig()
    {
        var patch = new Patch();
        var profile = Dimmer1();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1) { Number = 1 };
        patch.Add(fixture);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var surface = new CommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
        return (context, surface);
    }

    private static void TypeDigits(CommandSurfaceViewModel surface, string digits)
    {
        foreach (char c in digits) surface.PressDigit(c);
    }

    // 1: LEARN MACRO + MACRO 1 starts recording Macro 1.
    [Fact]
    public void LearnMacro_ThenMacroSlot_StartsRecording()
    {
        var (_, surface) = BuildRig();

        surface.PressLearnMacro();
        Assert.True(surface.IsLearnArmed);

        surface.PressMacroSlot(1);

        Assert.False(surface.IsLearnArmed);
        Assert.True(surface.IsRecordingMacro);
        Assert.Equal(1, surface.RecordingMacroSlot);
        Assert.Null(surface.DispatchError);
    }

    // 2 + 3: commands entered via the Command Surface execute normally while recording, in order.
    [Fact]
    public void CommandsTyped_WhileRecording_ExecuteNormally_AndAreRecordedInOrder()
    {
        var (context, surface) = BuildRig();
        surface.PressLearnMacro();
        surface.PressMacroSlot(1);

        surface.PressToken(CommandTokenKind.Fixture);
        TypeDigits(surface, "1");
        surface.PressToken(CommandTokenKind.At);
        TypeDigits(surface, "50");
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Contains(context.Patch.Fixtures[0], context.Selection.Items); // executed normally
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal((byte)Math.Round(50 / 100.0 * 255.0), value);

        surface.PressLearnMacro(); // stop/save

        var macro = context.Macros.FindBySlot(1);
        Assert.NotNull(macro);
        Assert.Single(macro!.Steps); // one CompositeCommand (Fixture 1 AT 50), same as ordinary composed grammar
    }

    // 4: LEARN MACRO again stops/saves.
    [Fact]
    public void LearnMacro_WhileRecording_StopsAndSaves()
    {
        var (context, surface) = BuildRig();
        surface.PressLearnMacro();
        surface.PressMacroSlot(2);

        surface.PressLearnMacro();

        Assert.False(surface.IsRecordingMacro);
        Assert.Null(surface.RecordingMacroSlot);
        Assert.NotNull(context.Macros.FindBySlot(2));
    }

    // 5: MACRO 1 replays operations correctly.
    [Fact]
    public void MacroSlot_WithNoLearnArmed_PlaysBackRecordedOperations()
    {
        var (context, surface) = BuildRig();
        surface.PressLearnMacro();
        surface.PressMacroSlot(1);
        surface.PressToken(CommandTokenKind.Fixture);
        TypeDigits(surface, "1");
        surface.PressToken(CommandTokenKind.At);
        TypeDigits(surface, "50");
        surface.PressToken(CommandTokenKind.Enter);
        surface.PressLearnMacro();

        context.Selection.Clear();
        context.Programmer.ClearAll();

        surface.PressMacroSlot(1);

        Assert.Null(surface.DispatchError);
        Assert.Contains(context.Patch.Fixtures[0], context.Selection.Items);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal((byte)Math.Round(50 / 100.0 * 255.0), value);
    }

    // 6 + 18: SHIFT + MACRO 1 resolves to Macro 5 (and confirms the Shift mapping for the other three).
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 6)]
    [InlineData(3, 7)]
    [InlineData(4, 8)]
    public void ShiftPlusMacroSlot_ResolvesToSlotPlusFour(int baseSlot, int expectedSlot)
    {
        var (context, surface) = BuildRig();
        surface.PressLearnMacro();
        // Shift is armed for the slot-selecting press specifically - every key (including LEARN
        // MACRO itself) consumes/clears an already-armed Shift, so it is pressed AFTER arming
        // LEARN, matching how a real touch operator presses Shift immediately before the Macro key.
        surface.PressShift();

        surface.PressMacroSlot(baseSlot);

        Assert.Equal(expectedSlot, surface.RecordingMacroSlot);
        surface.PressLearnMacro();
        Assert.NotNull(context.Macros.FindBySlot(expectedSlot));
        Assert.Null(context.Macros.FindBySlot(baseSlot));
    }

    // 7: empty Macro returns a clear result, never silent no-op.
    [Fact]
    public void MacroSlot_Empty_ReturnsClearDispatchError()
    {
        var (_, surface) = BuildRig();

        surface.PressMacroSlot(3);

        Assert.NotNull(surface.DispatchError);
        Assert.Contains("empty", surface.DispatchError!, StringComparison.OrdinalIgnoreCase);
    }

    // 8: recording into an existing slot does not silently overwrite - requires an explicit
    // second press of the SAME slot to confirm.
    [Fact]
    public void LearnMacro_IntoExistingSlot_RequiresExplicitSecondPressToOverwrite()
    {
        var (context, surface) = BuildRig();
        surface.PressLearnMacro();
        surface.PressMacroSlot(1);
        surface.PressToken(CommandTokenKind.Fixture);
        TypeDigits(surface, "1");
        surface.PressToken(CommandTokenKind.Enter);
        surface.PressLearnMacro();
        var originalMacro = context.Macros.FindBySlot(1);

        surface.PressLearnMacro();
        surface.PressMacroSlot(1); // first press on a non-empty slot - rejected, not overwritten

        Assert.False(surface.IsRecordingMacro);
        Assert.Same(originalMacro, context.Macros.FindBySlot(1));
        Assert.NotNull(surface.DispatchError);

        surface.PressMacroSlot(1); // second press of the SAME slot - confirms overwrite

        Assert.True(surface.IsRecordingMacro);
        Assert.Equal(1, surface.RecordingMacroSlot);
        surface.PressLearnMacro();
        Assert.NotSame(originalMacro, context.Macros.FindBySlot(1));
    }

    // 9: Macro playback during Learn is safely rejected in v1.
    [Fact]
    public void MacroSlot_PlaybackAttempt_WhileRecordingAnotherSlot_IsRejected()
    {
        var (context, surface) = BuildRig();
        // Pre-record Macro 2 so there's something a "successful" playback attempt could have done.
        surface.PressLearnMacro();
        surface.PressMacroSlot(2);
        surface.PressToken(CommandTokenKind.Fixture);
        TypeDigits(surface, "1");
        surface.PressToken(CommandTokenKind.Enter);
        surface.PressLearnMacro();
        context.Selection.Clear();

        // Now start recording Macro 1.
        surface.PressLearnMacro();
        surface.PressMacroSlot(1);
        Assert.True(surface.IsRecordingMacro);
        Assert.Equal(1, surface.RecordingMacroSlot);

        // While Macro 1 is recording, MACRO 2 (not the arm-a-slot press - LEARN isn't armed once
        // recording has actually started) always means "play it back" - safely rejected rather
        // than silently succeeding or corrupting the in-progress recording.
        surface.PressMacroSlot(2);

        Assert.NotNull(surface.DispatchError);
        Assert.Contains("recording", surface.DispatchError!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.Selection.Items); // playback did NOT happen
        Assert.True(surface.IsRecordingMacro); // Macro 1's recording is unaffected
        Assert.Equal(1, surface.RecordingMacroSlot);
    }

    // 16: recording state is cleared correctly after stop/cancel.
    [Fact]
    public void CancelLearnMacro_DiscardsRecording_ClearsAllRecordingState()
    {
        var (context, surface) = BuildRig();
        surface.PressLearnMacro();
        surface.PressMacroSlot(1);
        surface.PressToken(CommandTokenKind.Fixture);
        TypeDigits(surface, "1");
        surface.PressToken(CommandTokenKind.Enter);

        surface.CancelLearnMacro();

        Assert.False(surface.IsRecordingMacro);
        Assert.False(surface.IsLearnArmed);
        Assert.Null(surface.RecordingMacroSlot);
        Assert.Null(context.Macros.FindBySlot(1)); // never saved
    }

    // A second LEARN MACRO press with no slot chosen yet cancels the arm.
    [Fact]
    public void LearnMacro_PressedTwice_WithNoSlotChosen_CancelsTheArm()
    {
        var (_, surface) = BuildRig();

        surface.PressLearnMacro();
        Assert.True(surface.IsLearnArmed);

        surface.PressLearnMacro();

        Assert.False(surface.IsLearnArmed);
        Assert.False(surface.IsRecordingMacro);
    }

    // 17: MACRO 1-4 buttons route through PressMacroSlot, the same path SHIFT+MACRO does -
    // exercised for all four base slots.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MacroSlot_AllFourBaseSlots_RecordAndPlayBackIndependently(int slot)
    {
        var (context, surface) = BuildRig();
        surface.PressLearnMacro();
        surface.PressMacroSlot(slot);
        Assert.Equal(slot, surface.RecordingMacroSlot);
        surface.PressToken(CommandTokenKind.Fixture);
        TypeDigits(surface, "1");
        surface.PressToken(CommandTokenKind.Enter);
        surface.PressLearnMacro();

        context.Selection.Clear();
        surface.PressMacroSlot(slot);

        Assert.Contains(context.Patch.Fixtures[0], context.Selection.Items);
    }
}

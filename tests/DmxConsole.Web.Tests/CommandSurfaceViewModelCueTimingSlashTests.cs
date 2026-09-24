using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Application.Commands;
using DmxConsole.Application.Commands.Cues;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;
using DmxConsole.Web.CommandSurface;
using DmxConsole.Web.EditorToolBar;
using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>
/// Cue TIME split syntax slice - makes the existing CommandComposer.ResolveCueTiming Slash token
/// reachable from two input surfaces: the physical keyboard ("/") and a CONTEXTUAL Command Surface
/// softkey (CommandSurfaceViewModel.CanPressToken). Neither surface adds any new grammar - both
/// converge on the exact same CommandSurfaceViewModel.PressToken(CommandTokenKind.Slash) call the
/// existing CommandComposerCueTimingTests already prove resolves correctly (CLAUDE.md §4: no
/// parallel parsers).
/// </summary>
public class CommandSurfaceViewModelCueTimingSlashTests
{
    private static (ConsoleContext Context, CommandDispatcher Dispatcher, CommandSurfaceViewModel Surface, CueList CueList) BuildRig()
    {
        var patch = new Patch();
        var engine = new DmxOutputEngine(patch);
        var executors = new ExecutorBank();
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), executors);
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var cueList = new CueList();
        context.PrimaryCueList = cueList;
        var surface = CommandSurfaceViewModelTestSupport.BuildCommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
        return (context, dispatcher, surface, cueList);
    }

    private static Cue RecordCue(CueList cueList, ConsoleContext context, double number) =>
        cueList.RecordCue(context.Patch, context.Programmer, context.Selection, context.EffectiveOutput,
            $"Cue {number}", number, CueStoreOptions.Default);

    /// <summary>Mirrors CommandSurface.razor's OnKeyDown exactly (see that method, CommandSurface.razor
    /// - the Razor handler is a thin, logic-free proxy to KeyboardCommandMap + this same dispatch),
    /// so this test drives the real keyboard-mapping table plus the real ViewModel, key by key.</summary>
    private static void PressPhysicalKey(CommandSurfaceViewModel surface, string key)
    {
        Assert.True(KeyboardCommandMap.TryResolve(key, out var binding), $"No keyboard binding for '{key}'.");

        if (binding.Digit is char digit)
        {
            if (digit == '.') surface.PressDecimalPoint();
            else surface.PressDigit(digit);
            return;
        }

        if (binding.TokenKind is { } kind)
        {
            if (kind == CommandTokenKind.Backspace) surface.PressBackspace();
            else if (kind == CommandTokenKind.Clear) surface.PressClear();
            else if (kind == CommandTokenKind.Release) surface.PressRelease();
            else if (kind == CommandTokenKind.CaptureAll) surface.PressCapture();
            else surface.PressToken(kind);
        }
    }

    private static void TypeOnKeyboard(CommandSurfaceViewModel surface, string keys)
    {
        foreach (var ch in keys) PressPhysicalKey(surface, ch.ToString());
    }

    // ---------- 1/2: physical keyboard "/" reaches the Slash token and resolves Cue timing ----------

    [Fact]
    public void PhysicalKeyboard_ForwardSlash_PressesTheSameSlashToken_AsPressToken()
    {
        var (context, _, surface, cueList) = BuildRig();
        RecordCue(cueList, context, 1);

        TypeOnKeyboard(surface, "c");
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Timing);
        surface.PressDigit('8');

        // Physical "/" - routes through KeyboardCommandMap exactly like every other key.
        PressPhysicalKey(surface, "/");

        Assert.Contains("8", surface.DisplayPreview);
        Assert.Contains("/", surface.DisplayPreview);
        Assert.Null(surface.DispatchError);
    }

    [Fact]
    public void PhysicalKeyboard_FullCueTimeSplitEntry_ReachesSetCueTimingCommand_AndSetsInOutSeparately()
    {
        var (context, _, surface, cueList) = BuildRig();
        var cue = RecordCue(cueList, context, 1);

        TypeOnKeyboard(surface, "c1");
        surface.PressToken(CommandTokenKind.Timing);
        TypeOnKeyboard(surface, "8");
        PressPhysicalKey(surface, "/");
        TypeOnKeyboard(surface, "10");
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Null(surface.DispatchError);
        Assert.Equal(TimeSpan.FromSeconds(8), cue.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(10), cue.Timing.TimeOut);
    }

    // ---------- 3: the on-screen contextual action pushes the identical Slash token ----------

    [Fact]
    public void ContextualSlashAction_PushesIdenticalSlashToken_AsKeyboardPath()
    {
        var (context, _, surface, cueList) = BuildRig();
        var cue = RecordCue(cueList, context, 1);

        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Timing);
        surface.PressDigit('8');

        Assert.True(surface.CanPressToken(CommandTokenKind.Slash));

        // The on-screen softkey's click handler is exactly this call (CommandSurface.razor) -
        // the identical PressToken(CommandTokenKind.Slash) the keyboard path uses.
        surface.PressToken(CommandTokenKind.Slash);
        surface.PressDigit('1');
        surface.PressDigit('0');
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Null(surface.DispatchError);
        Assert.Equal(TimeSpan.FromSeconds(8), cue.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(10), cue.Timing.TimeOut);
    }

    // ---------- 4: contextual visibility - appears only when syntactically valid ----------

    [Fact]
    public void CanPressSlash_IsFalse_BeforeAnyTimeTokenExists()
    {
        var (context, _, surface, cueList) = BuildRig();
        RecordCue(cueList, context, 1);

        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1'); // "CUE 1" only, digits still pending - no TIME token yet

        Assert.False(surface.CanPressToken(CommandTokenKind.Slash));
    }

    [Fact]
    public void CanPressSlash_IsFalse_AfterTimeTokenWithNoInValueYet()
    {
        var (context, _, surface, cueList) = BuildRig();
        RecordCue(cueList, context, 1);

        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Timing); // "CUE 1 TIME" - no In value typed yet

        Assert.False(surface.CanPressToken(CommandTokenKind.Slash));
    }

    [Fact]
    public void CanPressSlash_IsTrue_AfterInValueIsTyped_ButNotYetCommitted()
    {
        var (context, _, surface, cueList) = BuildRig();
        RecordCue(cueList, context, 1);

        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Timing);
        surface.PressDigit('8'); // still pending digits, not yet committed to the composer

        Assert.True(surface.CanPressToken(CommandTokenKind.Slash));
    }

    // ---------- 5: scalar TIME (no slash) is completely unchanged ----------

    [Fact]
    public void ScalarCueTime_StillSetsBothInAndOut_Unchanged()
    {
        var (context, _, surface, cueList) = BuildRig();
        var cue = RecordCue(cueList, context, 1);

        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Timing);
        surface.PressDigit('1');
        surface.PressDigit('0');
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Null(surface.DispatchError);
        Assert.Equal(TimeSpan.FromSeconds(10), cue.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(10), cue.Timing.TimeOut);
    }

    // ---------- 6: an unrelated existing contextual area is unaffected ----------

    [Fact]
    public void CanPressSlash_IsFalse_OnAnEmptyCommandLine_UnrelatedFixtureContextUnaffected()
    {
        var (_, _, surface, _) = BuildRig();

        Assert.False(surface.CanPressToken(CommandTokenKind.Slash));
        // Unrelated existing grammar (a bare Fixture number) is unaffected by CanPressToken's probe.
        surface.PressDigit('1');
        Assert.Contains("1", surface.DisplayPreview);
    }

    // ---------- 7: merely checking availability causes no command-state mutation ----------

    [Fact]
    public void CanPressToken_CheckingAvailability_DoesNotMutateComposerState()
    {
        var (context, _, surface, cueList) = BuildRig();
        RecordCue(cueList, context, 1);

        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Timing);
        surface.PressDigit('8');

        var previewBefore = surface.DisplayPreview;
        var errorBefore = surface.DispatchError;

        for (int i = 0; i < 5; i++) surface.CanPressToken(CommandTokenKind.Slash);

        Assert.Equal(previewBefore, surface.DisplayPreview);
        Assert.Equal(errorBefore, surface.DispatchError);

        // The composition is still exactly where it was - Slash still resolves the same way
        // whether or not CanPressToken was probed first.
        surface.PressToken(CommandTokenKind.Slash);
        surface.PressDigit('1');
        surface.PressDigit('0');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Null(surface.DispatchError);
    }
}

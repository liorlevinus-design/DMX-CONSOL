using System.IO;
using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
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
/// Live-testing bugfix round (Cue TIME end-to-end): two things were audited/fixed.
///
/// BUG 1 - the "/" key was made a CONTEXTUAL softkey (visible only when
/// CommandSurfaceViewModel.CanPressToken(Slash) reported the composer would currently accept it)
/// by the prior Cue-TIME-split slice. Product decision: "/" must be a PERMANENT, always-visible
/// key like every other syntax key on the Command Surface - never hidden/shown by composer state.
/// CommandSurface.razor's own `@if (Sfc.CanPressToken(CommandTokenKind.Slash))` gate around the
/// Slash button has been removed; the button now renders unconditionally, exactly like Plus/Minus/
/// At/Thru. Grammar validity is still enforced generically by CommandComposer.Push/Build - never a
/// second, UI-side validity check - so pressing "/" somewhere invalid must still fail cleanly with
/// no state mutation (this file's "invalid context" test proves that side).
///
/// BUG 2 - CUE TIME ENTER not committing, live. Root cause found by diffing the working tree
/// against HEAD (commit 63f6bd8): CommandSurface.razor's fixed "Time" key was HARDCODED
/// `disabled` ("Time - per-attribute/Cue timing entry is not implemented on the Command Surface
/// yet...") with zero wiring to CommandComposer's already-implemented, already-tested CUE TIME
/// grammar - and KeyboardCommandMap has (and still has) no physical-key binding for the Timing
/// token either. So at that commit there was literally NO way, touch or keyboard, to ever push a
/// Timing token onto the command line - "CUE 1 TIME 3 ENTER" could never even be composed, let
/// alone dispatched. That specific defect was already corrected earlier in this same working tree
/// (the Time key now reads `disabled="@(!Sfc.CanPressToken(CommandTokenKind.Timing))"`, converging
/// on the exact same CommandComposer grammar the fixed "/" key and every other key already use -
/// CLAUDE.md §4/§12). This file's CUE-TIME-ENTER tests are the live, CommandSurfaceViewModel-level
/// proof this fix actually dispatches SetCueTimingCommand end-to-end (not merely that the
/// CommandComposer unit tests in CommandComposerCueTimingTests.cs resolve correctly in isolation -
/// those never exercised the Razor-visible enable-state or the ViewModel's Push/dispatch tail).
/// </summary>
public class CommandSurfaceViewModelSlashPermanentTests
{
    private static (ConsoleContext Context, CommandDispatcher Dispatcher, CommandSurfaceViewModel Surface, CueList CueList, CueListViewModel CueListVm) BuildRig()
    {
        var patch = new Patch();
        var engine = new DmxOutputEngine(patch);
        var executors = new ExecutorBank();
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), executors);
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var cueList = new CueList();
        context.PrimaryCueList = cueList;

        // Mirrors production wiring (MainViewModel): CueListViewModel and CommandSurfaceViewModel
        // share the exact SAME CueList instance as context.PrimaryCueList - never a second,
        // detached CueList (that divergence was audited and ruled out as a production bug, but is
        // exactly the kind of "stale/different instance" mistake worth guarding against here).
        var groupsVm = new GroupsViewModel(context, dispatcher);
        var executor = new Executor(-1);
        var programmerVm = new ProgrammerViewModel(context, dispatcher, new System.Collections.ObjectModel.ObservableCollection<ChannelFaderViewModel>());
        var cueListVm = new CueListViewModel(context.Patch, context.Programmer, context.Selection,
            context.EffectiveOutput, cueList, dispatcher, executor, programmerVm);
        var surface = new CommandSurfaceViewModel(context, dispatcher, new EditorContextStack(), groupsVm, cueListVm, onPatchApplied: () => { }, programmerVm);

        return (context, dispatcher, surface, cueList, cueListVm);
    }

    private static Cue RecordCue(CueList cueList, ConsoleContext context, double number) =>
        cueList.RecordCue(context.Patch, context.Programmer, context.Selection, context.EffectiveOutput,
            $"Cue {number}", number, CueStoreOptions.Default);

    // ---------- BUG 1, requirement 1: "/" is a permanent key, not contextually gated ----------

    [Fact]
    public void SlashKey_IsRenderedUnconditionally_NotGatedOnCanPressToken()
    {
        // Source-level regression check (no bUnit in this test project - see
        // DmxConsole.Web.Tests.csproj): the prior slice's `@if (Sfc.CanPressToken(...Slash))`
        // guard around the Slash button must be gone, and the button markup itself must remain,
        // unconditional, exactly like every other fixed syntax key (Plus/Minus/At/Thru).
        var razorPath = FindRepoFile("src/DmxConsole.Web/Components/Shell/CommandSurface.razor");
        var source = File.ReadAllText(razorPath);

        Assert.DoesNotContain("Sfc.CanPressToken(CommandTokenKind.Slash)", source);
        Assert.Contains("PressToken(CommandTokenKind.Slash)", source);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        throw new FileNotFoundException($"Could not locate '{relativePath}' by walking up from {AppContext.BaseDirectory}.");
    }

    // ---------- BUG 1, requirement 2: "/" in an invalid syntax context fails cleanly ----------

    [Fact]
    public void Slash_AsVeryFirstToken_FailsCleanly_NoMutation_NoException()
    {
        var (context, _, surface, _, _) = BuildRig();

        var exception = Record.Exception(() => surface.PressToken(CommandTokenKind.Slash));

        Assert.Null(exception);
        // CommandLine.razor renders `Sfc.DispatchError ?? Sfc.Current.Error` (see that component) -
        // an incomplete-with-error composition (not a dispatched-and-failed ReadyOperation) reports
        // through Current.Error, exactly what the generic fallthrough at the end of
        // CommandSurfaceViewModel.Push does for any structurally invalid token.
        Assert.Null(surface.DispatchError);
        Assert.NotNull(surface.Current.Error);
        // No fixture Selection, no Programmer, no Cue mutation of any kind happened - a rejected
        // leading token is purely a command-line-composition error, never a state mutation.
        Assert.Empty(context.Selection.Items);
    }

    // ---------- BUG 1, requirement 3 (regression): keyboard and on-screen Slash share one token path ----------

    [Fact]
    public void KeyboardAndOnScreenSlash_StillEmitTheSameToken_Regression()
    {
        Assert.True(KeyboardCommandMap.TryResolve("/", out var binding));
        Assert.Equal(CommandTokenKind.Slash, binding.TokenKind);
    }

    // ---------- BUG 2, requirement 4: CUE 1 TIME 3 ENTER commits end-to-end ----------

    [Fact]
    public void CueTimeEnter_ScalarValue_CommitsEndToEnd_SetsInAndOutOnTheLiveCue()
    {
        var (context, _, surface, cueList, _) = BuildRig();
        var cue = RecordCue(cueList, context, 1);

        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Timing);
        surface.PressDigit('3');
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Null(surface.DispatchError);
        Assert.Equal(TimeSpan.FromSeconds(3), cue.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(3), cue.Timing.TimeOut);
    }

    // ---------- BUG 2, requirement 5: CUE 1 TIME 8/10 ENTER commits end-to-end, split In/Out ----------

    [Fact]
    public void CueTimeEnter_SplitValue_CommitsEndToEnd_SetsInAndOutSeparately()
    {
        var (context, _, surface, cueList, _) = BuildRig();
        var cue = RecordCue(cueList, context, 1);

        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Timing);
        surface.PressDigit('8');
        surface.PressToken(CommandTokenKind.Slash);
        surface.PressDigit('1');
        surface.PressDigit('0');
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Null(surface.DispatchError);
        Assert.Equal(TimeSpan.FromSeconds(8), cue.Timing.TimeIn);
        Assert.Equal(TimeSpan.FromSeconds(10), cue.Timing.TimeOut);
    }

    // ---------- BUG 2, requirement 6: the Cue-table refresh layer observes the change ----------

    [Fact]
    public void CueTimeEnter_RaisesCueListChanged_SoTheCueTablePanelRefreshes()
    {
        var (context, _, surface, cueList, cueListVm) = BuildRig();
        RecordCue(cueList, context, 1);

        // CueListViewModel (the ViewModel the Cues panel actually binds to - see its own
        // constructor doc comment) subscribes to exactly this CueList.Changed event and refreshes
        // its own observable state from it (OnCueListChanged -> RefreshStatus). Asserting the raw
        // event fires is the most direct, non-brittle proof available without bUnit that the Cues
        // panel would actually repaint - CueListVm.CueList is the SAME instance the panel reads.
        bool changedRaised = false;
        cueListVm.CueList.Changed += () => changedRaised = true;

        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Timing);
        surface.PressDigit('5');
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Null(surface.DispatchError);
        Assert.True(changedRaised, "CueList.Changed did not fire after a successful CUE TIME ENTER - the Cue table panel would show stale timing.");
        Assert.Same(cueList, cueListVm.CueList);
    }

    // ---------- BUG 2, requirement 7: no mutation before ENTER ----------

    [Fact]
    public void CueTime_WithoutEnter_LeavesCueTimingCompletelyUnchanged()
    {
        var (context, _, surface, cueList, _) = BuildRig();
        var cue = RecordCue(cueList, context, 1);
        var originalTiming = cue.Timing;

        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Timing);
        surface.PressDigit('8');
        // No Enter, no second value - just a bare In value sitting in the pending-digit buffer.

        Assert.Equal(originalTiming, cue.Timing);
        Assert.Null(surface.DispatchError);
    }

    [Fact]
    public void CueTime_SplitValue_WithoutEnter_LeavesCueTimingCompletelyUnchanged()
    {
        var (context, _, surface, cueList, _) = BuildRig();
        var cue = RecordCue(cueList, context, 1);
        var originalTiming = cue.Timing;

        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Timing);
        surface.PressDigit('8');
        surface.PressToken(CommandTokenKind.Slash);
        surface.PressDigit('1');
        surface.PressDigit('0');
        // Still no Enter.

        Assert.Equal(originalTiming, cue.Timing);
        Assert.Null(surface.DispatchError);
    }

    // ---------- BUG 2: the fixed TIME key's own enable state (the other half of the live gap) ----------

    [Fact]
    public void TimeKey_BecomesPressable_AssoonAsACueNumberIsPending_ProvingTheFixedKeyIsReachable()
    {
        var (context, _, surface, cueList, _) = BuildRig();
        RecordCue(cueList, context, 1);

        // Before HEAD's Time key was wired to CanPressToken, this was permanently false/disabled -
        // the actual end-to-end blocker for the reported live bug (see class doc comment).
        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1');

        Assert.True(surface.CanPressToken(CommandTokenKind.Timing));
    }
}

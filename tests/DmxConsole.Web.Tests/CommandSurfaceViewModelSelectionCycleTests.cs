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

/// <summary>
/// Selection Cycle stabilization slice - regression coverage for the 5 programming actions that
/// were found NOT to close the Selection Cycle (HOME, family RELEASE, parameter RELEASE, CAPTURE
/// ALL) despite item 8's authoritative list requiring them to. Each test proves the fix the same
/// way: select Fixture A, perform the action (Selection must remain visibly A - item 2), then
/// select a DIFFERENT Fixture B - if the cycle actually closed, B REPLACES A rather than
/// accumulating with it (item 3). AT-with-value/FULL/family-PRESET-recall were already correct
/// before this slice and are not re-tested here.
/// </summary>
public class CommandSurfaceViewModelSelectionCycleTests
{
    private static FixtureProfile MovingHead() => new()
    {
        Id = "test-mh", Manufacturer = "Test", Model = "MovingHead",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "3ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 0 },
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 1, DefaultValue = 128 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2, DefaultValue = 128 },
                },
            },
        },
    };

    private static (ConsoleContext Context, CommandSurfaceViewModel Surface, PatchedFixture A, PatchedFixture B) BuildRig()
    {
        var patch = new Patch();
        var profile = MovingHead();
        var a = new PatchedFixture(profile, profile.Modes[0], 0, 1) { Number = 1 };
        var b = new PatchedFixture(profile, profile.Modes[0], 0, 10) { Number = 2 };
        patch.Add(a);
        patch.Add(b);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var surface = CommandSurfaceViewModelTestSupport.BuildCommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
        return (context, surface, a, b);
    }

    private static void SelectFixture(CommandSurfaceViewModel surface, int number)
    {
        surface.PressToken(CommandTokenKind.Fixture);
        foreach (char c in number.ToString()) surface.PressDigit(c);
        surface.PressToken(CommandTokenKind.Enter);
    }

    private static void AssertCycleClosedThenReplaces(ConsoleContext context, CommandSurfaceViewModel surface, PatchedFixture a, PatchedFixture b)
    {
        Assert.Contains(a, context.Selection.Items); // item 2: the action leaves the current Selection selected
        Assert.True(context.SelectionCycle.StartFreshOnNextSelection); // the cycle is now closed

        SelectFixture(surface, b.Number);

        Assert.Equal(new[] { b }, context.Selection.Items); // item 3: B replaces A - never accumulates
    }

    [Fact]
    public void Home_ClosesTheSelectionCycle()
    {
        var (context, surface, a, b) = BuildRig();
        SelectFixture(surface, a.Number);

        surface.PressToken(CommandTokenKind.Home);

        AssertCycleClosedThenReplaces(context, surface, a, b);
    }

    [Fact]
    public void FamilyRelease_ClosesTheSelectionCycle()
    {
        var (context, surface, a, b) = BuildRig();
        SelectFixture(surface, a.Number);
        context.Programmer.SetChannel(a.UniverseId, a.AbsoluteIndex(a.FindChannel(ChannelType.Pan)!), 100);

        surface.PressToken(CommandTokenKind.Position);
        surface.PressToken(CommandTokenKind.Release);

        AssertCycleClosedThenReplaces(context, surface, a, b);
    }

    [Fact]
    public void ParameterRelease_ClosesTheSelectionCycle()
    {
        var (context, surface, a, b) = BuildRig();
        SelectFixture(surface, a.Number);
        context.Programmer.SetChannel(a.UniverseId, a.AbsoluteIndex(a.FindChannel(ChannelType.Pan)!), 100);

        surface.PressParameter(ChannelType.Pan);
        surface.PressToken(CommandTokenKind.Release);

        AssertCycleClosedThenReplaces(context, surface, a, b);
    }

    [Fact]
    public void CaptureAll_ClosesTheSelectionCycle()
    {
        var (context, surface, a, b) = BuildRig();
        SelectFixture(surface, a.Number);

        surface.PressCaptureAll();

        // CAPTURE ALL deliberately never touches Selection itself (still true after this fix) -
        // only the cycle-closing flag changes.
        Assert.Contains(a, context.Selection.Items);
        Assert.True(context.SelectionCycle.StartFreshOnNextSelection);

        SelectFixture(surface, b.Number);

        Assert.Equal(new[] { b }, context.Selection.Items);
    }

    // ---------- RELEASE-panel slice §A: "selection expression completion != programming
    // execution completion". CommandComposer.Resolve already ties EndsSelectionCycle to
    // atValue!=null (see Application.Tests.CommandSurface.SelectionCycleComposerTests.
    // At_ClosesSelectionCycle_SelectionOnlyDoesNot for the composer-only proof) - these tests
    // audit the FULL round trip through CommandSurfaceViewModel.Push, the thing an operator
    // actually presses through, for the exact scenarios the spec names. ----------

    /// <summary>"FIXTURE 1 THRU 20 ENTER" (no AT): a pure selection expression. ENTER only
    /// completes the SELECTION, it is not a programming execution - the cycle must stay open, AND
    /// the operator-visible Task line must keep representing the selection rather than looking
    /// idle (§A - it must NOT read the same as "nothing has happened"/right after a CLEAR).</summary>
    [Fact]
    public void PureSelectionRange_Enter_KeepsSelectionCycleOpen_AndTaskLineStillRepresentsIt()
    {
        var (context, surface, a, b) = BuildRig();

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Thru);
        surface.PressDigit('2');
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Equal(new[] { a, b }, context.Selection.Items);
        Assert.False(context.SelectionCycle.StartFreshOnNextSelection); // cycle remains OPEN
        // The Task line must NOT have cleared back to idle merely because ENTER was pressed -
        // it must still visibly represent the fixture selection that was just made.
        Assert.NotEqual(string.Empty, surface.DisplayPreview);
        Assert.Equal("FIXTURE 1 THRU 2", surface.DisplayPreview.ToUpperInvariant());
    }

    /// <summary>A pure selection's Task-line echo is superseded (not literally cleared) the moment
    /// the operator starts composing something new - it never leaks into or gets prepended to the
    /// next gesture's own display.</summary>
    [Fact]
    public void PureSelectionRange_Enter_TaskLineEcho_IsSupersededByTheNextGesture_NeverAppendedTo()
    {
        var (_, surface, a, _) = BuildRig();

        SelectFixture(surface, a.Number);
        Assert.Equal("FIXTURE 1", surface.DisplayPreview.ToUpperInvariant());

        surface.PressToken(CommandTokenKind.Group); // a brand new gesture begins

        Assert.Equal("GROUP", surface.DisplayPreview.ToUpperInvariant()); // not "FIXTURE 1 GROUP"
    }

    /// <summary>Consecutive pure-selection gestures accumulate while the cycle stays open - no
    /// AT/HOME/RELEASE/etc. has executed yet, so nothing should have closed it in between.</summary>
    [Fact]
    public void PureSelection_ConsecutiveGestures_AccumulateWhileCycleIsOpen()
    {
        var (context, surface, a, b) = BuildRig();

        SelectFixture(surface, a.Number);
        Assert.False(context.SelectionCycle.StartFreshOnNextSelection);
        Assert.Equal(new[] { a }, context.Selection.Items);

        SelectFixture(surface, b.Number); // a second bare selection gesture, cycle still open

        Assert.False(context.SelectionCycle.StartFreshOnNextSelection);
        Assert.Equal(new[] { a, b }, context.Selection.Items); // accumulated, NOT replaced
    }

    /// <summary>"FIXTURE 1 THRU 20 AT 30 ENTER": a programming execution. Intensity is written to
    /// Programmer, Selection remains visibly selected, the command line returns to idle, and the
    /// cycle closes so the NEXT Fixture/Group selection starts fresh (replaces).</summary>
    [Fact]
    public void SelectionWithAt_Enter_WritesProgrammer_AndClosesSelectionCycle()
    {
        var (context, surface, a, b) = BuildRig();

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Thru);
        surface.PressDigit('2');
        surface.PressToken(CommandTokenKind.At);
        surface.PressDigit('3');
        surface.PressDigit('0');
        surface.PressToken(CommandTokenKind.Enter);

        var dimmerIndex = a.AbsoluteIndex(a.FindChannel(ChannelType.Dimmer)!);
        Assert.True(context.Programmer.HasStoredValue(a.UniverseId, dimmerIndex, out var value));
        Assert.Equal((byte)Math.Round(0.30 * 255), value);

        Assert.Empty(surface.Current.Tokens); // command line returned to idle
        // Distinct from Current.Tokens: DisplayPreview is what the operator actually sees, and it
        // must ALSO be truly idle here - not showing a leftover selection-context echo from
        // earlier in the same gesture (see PureSelectionRange_Enter_...TaskLineStillRepresentsIt
        // for the pure-selection case where it must NOT be idle).
        Assert.Equal(string.Empty, surface.DisplayPreview);
        AssertCycleClosedThenReplaces(context, surface, a, b);
    }
}

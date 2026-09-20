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
        var surface = new CommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
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
}

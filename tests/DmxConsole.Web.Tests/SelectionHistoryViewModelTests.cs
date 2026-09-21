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
/// Selection History rule, end to end through the real UI-facing ViewModels (not just the
/// Application-layer commands - see SelectionHistoryTests for that layer). Proves the two GUI/
/// keypad-level gaps the audit found are actually closed by the centralized
/// CommandDispatcher.Dispatch fix, with zero per-ViewModel plumbing: SelectionViewModel (the
/// classic fixture-button/Group-panel path used by SelectionBar.razor) never called
/// RememberSelection anywhere, and its Odd/Even RelayCommands bypassed even the one call site
/// that did exist (GroupsViewModel.Apply) entirely by dispatching directly. Both are fixed purely
/// by SelectionCommandBase.ProducesSelectionSnapshot + CommandDispatcher - no changes were needed
/// in SelectionViewModel.cs itself.
/// </summary>
public class SelectionHistoryViewModelTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, CommandSurfaceViewModel Surface, SelectionViewModel SelectionVm) BuildRig(int fixtureCount)
    {
        var patch = new Patch();
        var profile = Dimmer1();
        for (int i = 1; i <= fixtureCount; i++) patch.Add(new PatchedFixture(profile, profile.Modes[0], 0, i) { Number = i });
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var surface = CommandSurfaceViewModelTestSupport.BuildCommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
        var selectionVm = new SelectionViewModel(context, dispatcher);
        return (context, dispatcher, surface, selectionVm);
    }

    /// <summary>The Selection History rule's own first example, driven through the real Command
    /// Surface keypad methods (PressToken/PressDigit/PressClear) rather than raw composer tokens.</summary>
    [Fact]
    public void CommandSurface_FixtureRangeThenOddThenClear_FixtureRecall_RecallsTheOddFilteredSet()
    {
        var (context, _, surface, _) = BuildRig(fixtureCount: 10);

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Thru);
        surface.PressDigit('1'); surface.PressDigit('0');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Equal(Enumerable.Range(1, 10), context.Selection.Items.Select(f => f.Number));

        surface.PressToken(CommandTokenKind.Odd);
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, context.Selection.Items.Select(f => f.Number));

        surface.PressClear();
        Assert.Empty(context.Selection.Items);
        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, context.SelectionCycle.LastSelection.Select(f => f.Number));

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDecimalPoint(); // "." = Recall while a DmxAddress isn't expected
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, context.Selection.Items.Select(f => f.Number));
    }

    /// <summary>GUI fixture selection (SelectionBar.razor's TapFixtureNumberCommand) - the
    /// previously-broken source: SelectionViewModel never called RememberSelection anywhere.</summary>
    [Fact]
    public void GuiFixtureClicks_UpdateLastSelection_InClickOrder()
    {
        var (context, _, _, selectionVm) = BuildRig(fixtureCount: 10);

        selectionVm.TapFixtureNumberCommand.Execute(context.Patch.FindByNumber(7)!);
        selectionVm.TapFixtureNumberCommand.Execute(context.Patch.FindByNumber(2)!);
        selectionVm.TapFixtureNumberCommand.Execute(context.Patch.FindByNumber(9)!);

        Assert.Equal(new[] { 7, 2, 9 }, context.SelectionCycle.LastSelection.Select(f => f.Number));
    }

    /// <summary>GUI Odd/Even (SelectionBar.razor's Odd/Even buttons) - the second previously-broken
    /// source: these RelayCommands dispatch directly, bypassing even the one manual
    /// RememberSelection call site GroupsViewModel.Apply had.</summary>
    [Fact]
    public void GuiOddEvenButtons_UpdateLastSelection_WithoutGoingThroughDispatchSelectionGesture()
    {
        var (context, dispatcher, _, selectionVm) = BuildRig(fixtureCount: 10);
        dispatcher.Dispatch(new DmxConsole.Application.Commands.Selection.SelectRangeCommand(1, 10));

        selectionVm.SelectOddCommand.Execute(null);

        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, context.SelectionCycle.LastSelection.Select(f => f.Number));
    }

    /// <summary>Group panel Apply (GroupsViewModel.Apply) - already correct before this slice via
    /// its own manual RememberSelection call, now covered by the same centralized mechanism -
    /// regression guard proving the removal of that manual call didn't break it.</summary>
    [Fact]
    public void GroupsViewModelApply_StillUpdatesLastSelection_AfterCentralization()
    {
        var (context, dispatcher, _, selectionVm) = BuildRig(fixtureCount: 10);
        dispatcher.Dispatch(new DmxConsole.Application.Commands.Selection.SelectRangeCommand(1, 3));
        var group = context.Groups.CreateFromSelection("Front", context.Selection, number: 1);
        dispatcher.Dispatch(new DmxConsole.Application.Commands.Selection.ClearSelectionCommand());

        var groupsVm = new GroupsViewModel(context, dispatcher);
        groupsVm.ApplyCommand.Execute(group);

        Assert.Equal(new[] { 1, 2, 3 }, context.SelectionCycle.LastSelection.Select(f => f.Number));
    }
}

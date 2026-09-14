using DmxConsole.Application.Commands.Groups;
using DmxConsole.Application.Commands.Presets;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Core;
using Xunit;

namespace DmxConsole.Application.Tests;

public class UndoRiskTests
{
    [Fact]
    public void SafeCommand_Undo_PerformsImmediately_NoConfirmationNeeded()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(2);
        var fixture = context.Patch.Fixtures[0];
        dispatcher.Dispatch(new ToggleFixtureCommand(fixture));
        Assert.Contains(fixture, context.Selection.Items);

        var outcome = undoRedo.Undo();

        Assert.True(outcome.Performed);
        Assert.Empty(context.Selection.Items);
    }

    [Fact]
    public void CreateGroupCommand_Undo_WithoutConfirmation_IsBlocked_GroupSurvives()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(2);
        dispatcher.Dispatch(new ToggleFixtureCommand(context.Patch.Fixtures[0]));
        dispatcher.Dispatch(new CreateGroupCommand("Fronts"));
        Assert.Single(context.Groups.Groups);

        var outcome = undoRedo.Undo(); // no confirmedOptionId - must not auto-run a Destructive option

        Assert.False(outcome.Performed);
        Assert.NotNull(outcome.Proposal);
        Assert.Contains(outcome.Proposal!.Options, o => o.Risk == UndoRisk.Destructive);
        Assert.Single(context.Groups.Groups); // NOT removed
    }

    [Fact]
    public void CreateGroupCommand_Undo_WithConfirmation_RemovesTheGroup()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(2);
        dispatcher.Dispatch(new ToggleFixtureCommand(context.Patch.Fixtures[0]));
        dispatcher.Dispatch(new CreateGroupCommand("Fronts"));

        var proposal = undoRedo.PeekUndo();
        var destructiveOption = proposal!.Options.Single(o => o.Risk == UndoRisk.Destructive);

        var outcome = undoRedo.Undo(destructiveOption.Id);

        Assert.True(outcome.Performed);
        Assert.Empty(context.Groups.Groups);
    }

    [Fact]
    public void StorePresetCommand_NewPreset_UndoIsDestructive_UpdateIsSafe()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(1);
        var fixture = context.Patch.Fixtures[0];
        context.Programmer.SetChannel(0, 0, 128);

        dispatcher.Dispatch(new StorePresetCommand(context.Presets, new[] { fixture }, AttributeClass.Intensity, "Half", 1));
        var newProposal = undoRedo.PeekUndo();
        Assert.Contains(newProposal!.Options, o => o.Risk == UndoRisk.Destructive);

        // Confirm the destructive undo to get back to a clean baseline, then store an UPDATE.
        undoRedo.Undo(newProposal.Options.Single(o => o.Risk == UndoRisk.Destructive).Id);
        Assert.Empty(context.Presets.Presets);

        var preset = dispatcher.Dispatch(new StorePresetCommand(context.Presets, new[] { fixture }, AttributeClass.Intensity, "Half", 1)).Preset!;
        context.Programmer.SetChannel(0, 0, 200);
        dispatcher.Dispatch(new StorePresetCommand(context.Presets, new[] { fixture }, AttributeClass.Intensity, preset.Name, preset.Number, preset));

        var updateProposal = undoRedo.PeekUndo();
        Assert.All(updateProposal!.Options, o => Assert.Equal(UndoRisk.Safe, o.Risk));

        var outcome = undoRedo.Undo(); // Safe - no id needed
        Assert.True(outcome.Performed);
        Assert.Single(context.Presets.Presets); // still exists, just reverted
    }
}

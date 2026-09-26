using DmxConsole.Application.Commands.Selection;
using DmxConsole.Core;
using Xunit;

namespace DmxConsole.Application.Tests;

/// <summary>CLAUDE.md §16 (PSEL-4) - ConsoleContext.ParameterSelection lifecycle: exactly one
/// shared instance, independent of Fixture Selection and Programmer, cleared by CLEAR
/// (ClearSelectionAction) alongside Fixture Selection and SelectionCycle - never derived from
/// either of the other two.</summary>
public class ParameterSelectionLifecycleTests
{
    [Fact]
    public void ConsoleContext_ExposesTheSameParameterSelectionInstance_ToMultipleConsumers()
    {
        var (context, _, _) = TestFixtures.BuildConsole(1);

        // Two independent "consumers" reading the property - both must observe the same instance
        // and the same mutations, proving there is exactly one authoritative source of truth.
        var consumerA = context.ParameterSelection;
        var consumerB = context.ParameterSelection;

        Assert.Same(consumerA, consumerB);

        consumerA.Select(ChannelType.Pan);

        Assert.True(consumerB.Contains(ChannelType.Pan));
        Assert.Same(consumerA, context.ParameterSelection);
    }

    [Fact]
    public void ClearSelectionAction_ClearsParameterSelection()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(1);
        context.ParameterSelection.Select(ChannelType.Pan);
        context.ParameterSelection.Select(ChannelType.Tilt);

        var result = dispatcher.DispatchAction(new ClearSelectionAction());

        Assert.True(result.Success);
        Assert.True(context.ParameterSelection.IsEmpty);
    }

    [Fact]
    public void ChangingFixtureSelection_LeavesParameterSelectionIntact()
    {
        var (context, _, _) = TestFixtures.BuildConsole(3);
        context.ParameterSelection.Select(ChannelType.Pan);
        context.ParameterSelection.Select(ChannelType.Tilt);

        context.Selection.Add(context.Patch.FindByNumber(1)!);
        context.Selection.Add(context.Patch.FindByNumber(2)!);
        context.Selection.Clear();
        context.Selection.Add(context.Patch.FindByNumber(3)!);

        Assert.Equal(new[] { ChannelType.Pan, ChannelType.Tilt }, context.ParameterSelection.Items);
    }

    [Fact]
    public void ParameterSelectionOperations_NeverMutateProgrammerState()
    {
        var (context, _, _) = TestFixtures.BuildConsole(1);
        context.Programmer.SetChannel(0, 0, 123);

        context.ParameterSelection.Select(ChannelType.Pan);
        context.ParameterSelection.Select(ChannelType.Dimmer);
        context.ParameterSelection.Remove(ChannelType.Pan);
        context.ParameterSelection.Clear();

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(123, value);
    }

    [Fact]
    public void ClearSelectionAction_StillClearsFixtureSelectionAndSelectionCycle_Unchanged()
    {
        // Regression: existing CLEAR behavior on Fixture Selection / SelectionCycle must be
        // unaffected by adding the new Parameter Selection clearing.
        var (context, dispatcher, _) = TestFixtures.BuildConsole(2);
        context.Selection.Add(context.Patch.FindByNumber(1)!);
        context.SelectionCycle.MarkExecutionCompleted();

        var result = dispatcher.DispatchAction(new ClearSelectionAction());

        Assert.True(result.Success);
        Assert.Empty(context.Selection.Items);
        Assert.False(context.SelectionCycle.StartFreshOnNextSelection);
    }
}

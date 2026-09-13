using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using Xunit;

namespace DmxConsole.Application.Tests;

public class ProgrammerCommandTests
{
    private static FixtureProfile DimmerAndColor() => new()
    {
        Id = "test-dimmer-color",
        Manufacturer = "Test",
        Model = "DimmerColor",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "4ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 0 },
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 1 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 2 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 3 },
                },
            },
        },
    };

    private static (Core.Fixtures.Patch Patch, PatchedFixture Fixture) BuildPatch()
    {
        var patch = new Core.Fixtures.Patch();
        var profile = DimmerAndColor();
        var fixture = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        patch.Add(fixture);
        return (patch, fixture);
    }

    private static (Core.Engine.Programmer Programmer, ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo) BuildConsole(PatchedFixture fixture, Core.Fixtures.Patch patch)
    {
        var programmer = new Core.Engine.Programmer();
        var context = new ConsoleContext(patch, programmer, new Core.Selection.FixtureSelection(), new Core.Selection.GroupManager());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        return (programmer, context, dispatcher, undoRedo);
    }

    [Fact]
    public void ReleaseCommand_ClearsAllChannels_AndUndoRestoresThem()
    {
        var (patch, fixture) = BuildPatch();
        var (programmer, context, dispatcher, undoRedo) = BuildConsole(fixture, patch);
        var dimmer = fixture.FindChannel(ChannelType.Dimmer)!;
        var red = fixture.FindChannel(ChannelType.ColorRed)!;
        programmer.SetChannel(0, fixture.AbsoluteIndex(dimmer), 100);
        programmer.SetChannel(0, fixture.AbsoluteIndex(red), 200);

        var result = dispatcher.Dispatch(new ReleaseCommand(new[] { fixture }));

        Assert.Equal(ConsoleActionType.Release, result.ActionType);
        Assert.Contains(fixture, result.AffectedFixtures);
        Assert.False(programmer.HasStoredValue(0, fixture.AbsoluteIndex(dimmer), out _));
        Assert.False(programmer.HasStoredValue(0, fixture.AbsoluteIndex(red), out _));

        undoRedo.Undo();
        Assert.True(programmer.HasStoredValue(0, fixture.AbsoluteIndex(dimmer), out var d));
        Assert.Equal(100, d);
        Assert.True(programmer.HasStoredValue(0, fixture.AbsoluteIndex(red), out var r));
        Assert.Equal(200, r);
    }

    [Fact]
    public void ReleaseCommand_WithAttributeFilter_OnlyClearsThatAttribute_ReportsClearAttribute()
    {
        var (patch, fixture) = BuildPatch();
        var (programmer, context, dispatcher, _) = BuildConsole(fixture, patch);
        var dimmer = fixture.FindChannel(ChannelType.Dimmer)!;
        var red = fixture.FindChannel(ChannelType.ColorRed)!;
        programmer.SetChannel(0, fixture.AbsoluteIndex(dimmer), 100);
        programmer.SetChannel(0, fixture.AbsoluteIndex(red), 200);

        var result = dispatcher.Dispatch(new ReleaseCommand(new[] { fixture }, AttributeClass.Color));

        Assert.Equal(ConsoleActionType.ClearAttribute, result.ActionType);
        Assert.Equal(new[] { AttributeClass.Color }, result.AffectedAttributes);
        Assert.True(programmer.HasStoredValue(0, fixture.AbsoluteIndex(dimmer), out _)); // untouched
        Assert.False(programmer.HasStoredValue(0, fixture.AbsoluteIndex(red), out _));   // cleared
    }

    [Fact]
    public void KnockoutCommand_SuppressesChannel_AndUndoRestoresContribution()
    {
        var (patch, fixture) = BuildPatch();
        var (programmer, context, dispatcher, undoRedo) = BuildConsole(fixture, patch);
        var dimmer = fixture.FindChannel(ChannelType.Dimmer)!;
        int idx = fixture.AbsoluteIndex(dimmer);
        programmer.SetChannel(0, idx, 150);

        var result = dispatcher.Dispatch(new KnockoutCommand(new[] { fixture }));

        Assert.True(result.Success);
        Assert.False(programmer.TryGetChannelValue(0, idx, out _));
        Assert.True(programmer.HasStoredValue(0, idx, out var stored));
        Assert.Equal(150, stored);

        undoRedo.Undo();
        Assert.True(programmer.TryGetChannelValue(0, idx, out var restored));
        Assert.Equal(150, restored);
    }

    [Fact]
    public void KnockoutCommand_OnChannelWithNoValue_IsNoOp()
    {
        var (patch, fixture) = BuildPatch();
        var (_, _, dispatcher, _) = BuildConsole(fixture, patch);

        var result = dispatcher.Dispatch(new KnockoutCommand(new[] { fixture }));

        Assert.Empty(result.AffectedFixtures);
    }

    [Fact]
    public void RestoreCommand_UndoesToKnockedOutState()
    {
        var (patch, fixture) = BuildPatch();
        var (programmer, context, dispatcher, undoRedo) = BuildConsole(fixture, patch);
        var dimmer = fixture.FindChannel(ChannelType.Dimmer)!;
        int idx = fixture.AbsoluteIndex(dimmer);
        programmer.SetChannel(0, idx, 150);
        programmer.Knockout(0, idx);

        dispatcher.Dispatch(new RestoreCommand(new[] { fixture }));
        Assert.True(programmer.TryGetChannelValue(0, idx, out _));

        undoRedo.Undo();
        Assert.True(programmer.IsKnockedOut(0, idx));
    }

    [Fact]
    public void AdjustIntensityCommand_Relative_AddsPercentagePoints()
    {
        var (patch, fixture) = BuildPatch();
        var (programmer, context, dispatcher, _) = BuildConsole(fixture, patch);
        var dimmer = fixture.FindChannel(ChannelType.Dimmer)!;
        int idx = fixture.AbsoluteIndex(dimmer);
        const byte startValue = 178; // ~70%
        programmer.SetChannel(0, idx, startValue);

        dispatcher.Dispatch(new AdjustIntensityCommand(new[] { fixture }, AdjustOperation.Relative, 20));

        Assert.True(programmer.TryGetChannelValue(0, idx, out var value));
        double expectedPercent = (startValue / 255.0 * 100.0) + 20; // additive percentage points, not multiplicative
        Assert.Equal((byte)Math.Round(expectedPercent / 100.0 * 255.0), value);
    }

    [Fact]
    public void AdjustIntensityCommand_Relative_ClampsAtUpperBound()
    {
        var (patch, fixture) = BuildPatch();
        var (programmer, context, dispatcher, _) = BuildConsole(fixture, patch);
        var dimmer = fixture.FindChannel(ChannelType.Dimmer)!;
        int idx = fixture.AbsoluteIndex(dimmer);
        programmer.SetChannel(0, idx, 255);

        dispatcher.Dispatch(new AdjustIntensityCommand(new[] { fixture }, AdjustOperation.Relative, 20));

        Assert.True(programmer.TryGetChannelValue(0, idx, out var value));
        Assert.Equal(255, value);
    }

    [Fact]
    public void AdjustIntensityCommand_Absolute_SetsExactPercent()
    {
        var (patch, fixture) = BuildPatch();
        var (programmer, context, dispatcher, _) = BuildConsole(fixture, patch);
        var dimmer = fixture.FindChannel(ChannelType.Dimmer)!;
        int idx = fixture.AbsoluteIndex(dimmer);
        programmer.SetChannel(0, idx, 10);

        dispatcher.Dispatch(new AdjustIntensityCommand(new[] { fixture }, AdjustOperation.Absolute, 50));

        Assert.True(programmer.TryGetChannelValue(0, idx, out var value));
        Assert.Equal((byte)Math.Round(50.0 / 100 * 255), value);
    }

    [Fact]
    public void AdjustIntensityCommand_Undo_RestoresExactPreviousValue()
    {
        var (patch, fixture) = BuildPatch();
        var (programmer, context, dispatcher, undoRedo) = BuildConsole(fixture, patch);
        var dimmer = fixture.FindChannel(ChannelType.Dimmer)!;
        int idx = fixture.AbsoluteIndex(dimmer);
        programmer.SetChannel(0, idx, 123);

        dispatcher.Dispatch(new AdjustIntensityCommand(new[] { fixture }, AdjustOperation.Relative, -30));
        undoRedo.Undo();

        Assert.True(programmer.TryGetChannelValue(0, idx, out var value));
        Assert.Equal(123, value);
    }

    [Fact]
    public void AdjustIntensityCommand_UntouchedChannel_StartsFromFixtureDefault()
    {
        var patch = new Core.Fixtures.Patch();
        var profile = new FixtureProfile
        {
            Id = "test-default",
            Manufacturer = "Test",
            Model = "Default",
            Modes = new[]
            {
                new FixtureMode
                {
                    Name = "1ch",
                    Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 51 } }, // 20%
                },
            },
        };
        var fixture = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        patch.Add(fixture);
        var (programmer, context, dispatcher, _) = BuildConsole(fixture, patch);

        dispatcher.Dispatch(new AdjustIntensityCommand(new[] { fixture }, AdjustOperation.Relative, 10));

        Assert.True(programmer.TryGetChannelValue(0, 0, out var value));
        // Default 51/255 = 20% exactly; +10% = 30% -> same Math.Round the command itself uses.
        Assert.Equal((byte)Math.Round(30.0 / 100 * 255), value);
    }

    [Fact]
    public void CommandResult_PreviousAndNewValues_AreKeyedByFixtureAndChannelType()
    {
        var (patch, fixture) = BuildPatch();
        var (programmer, context, dispatcher, _) = BuildConsole(fixture, patch);
        var dimmer = fixture.FindChannel(ChannelType.Dimmer)!;
        programmer.SetChannel(0, fixture.AbsoluteIndex(dimmer), 100);

        var result = dispatcher.Dispatch(new AdjustIntensityCommand(new[] { fixture }, AdjustOperation.Absolute, 50));

        var key = (fixture.Id, ChannelType.Dimmer);
        Assert.Equal(100, result.PreviousValues[key]);
        Assert.Equal((byte)Math.Round(50.0 / 100 * 255), result.NewValues[key]);
    }
}

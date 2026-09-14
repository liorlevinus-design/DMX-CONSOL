using DmxConsole.Application.Commands.Presets;
using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using Xunit;

namespace DmxConsole.Application.Tests;

public class PresetCommandTests
{
    private static FixtureProfile RgbwPar() => new()
    {
        Id = "test-rgbw",
        Manufacturer = "Test",
        Model = "RGBW",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "4ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 0 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 1 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 2 },
                    new FixtureChannel { Name = "White", Type = ChannelType.ColorWhite, Offset = 3 },
                },
            },
        },
    };

    /// <summary>Only Pan - no Tilt - deliberately, to prove a Position preset applies safely to a fixture missing some of its channels.</summary>
    private static FixtureProfile PanOnlyMover() => new()
    {
        Id = "test-pan-only",
        Manufacturer = "Test",
        Model = "PanOnly",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "1ch",
                Channels = new[] { new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 } },
            },
        },
    };

    private static FixtureProfile PanTiltMover() => new()
    {
        Id = "test-pan-tilt",
        Manufacturer = "Test",
        Model = "PanTilt",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "2ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 1 },
                },
            },
        },
    };

    private static (Patch Patch, ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo) BuildConsole()
    {
        var patch = new Patch();
        var programmer = new Core.Engine.Programmer();
        var context = new ConsoleContext(patch, programmer, new Core.Selection.FixtureSelection(),
            new Core.Selection.GroupManager(), new Core.Engine.DmxOutputEngine(patch), new PresetLibrary(), new Core.Engine.ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        return (patch, context, dispatcher, undoRedo);
    }

    [Fact]
    public void StorePresetCommand_CreatesNewPreset_FromProgrammerValues()
    {
        var (patch, context, dispatcher, undoRedo) = BuildConsole();
        var profile = RgbwPar();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        patch.Add(fixture);
        context.Programmer.SetChannel(0, 0, 255); // Red
        context.Programmer.SetChannel(0, 2, 128); // Blue

        var result = dispatcher.Dispatch(new StorePresetCommand(context.Presets, new[] { fixture }, AttributeClass.Color, "Blue-ish", 1));

        Assert.True(result.Success);
        Assert.NotNull(result.Preset);
        Assert.Equal(255, result.Preset!.Values[ChannelType.ColorRed]);
        Assert.Equal(128, result.Preset.Values[ChannelType.ColorBlue]);
        Assert.Equal(0, result.Preset.Values[ChannelType.ColorGreen]); // default, untouched in Programmer
        Assert.Single(context.Presets.Presets);

        // Undo-ing a freshly Created Preset is Destructive (Step F's UndoRisk gate) - requires
        // the confirmed option id, not a bare Undo().
        var proposal = undoRedo.PeekUndo()!;
        undoRedo.Undo(proposal.Options.Single(o => o.Risk == UndoRisk.Destructive).Id);
        Assert.Empty(context.Presets.Presets);
    }

    [Fact]
    public void StorePresetCommand_Update_MergesWithoutOverwritingOtherChannelTypes()
    {
        var (patch, context, dispatcher, undoRedo) = BuildConsole();

        // Record from a Pan+Tilt fixture first.
        var moverProfile = PanTiltMover();
        var mover = new PatchedFixture(moverProfile, moverProfile.Modes[0], 0, 1);
        patch.Add(mover);
        context.Programmer.SetChannel(0, 0, 10); // Pan
        context.Programmer.SetChannel(0, 1, 20); // Tilt
        var preset = dispatcher.Dispatch(new StorePresetCommand(context.Presets, new[] { mover }, AttributeClass.Position, "Centre", 1)).Preset!;

        // Now update from a Pan-only fixture with a different Pan value - Tilt must survive untouched.
        var panOnlyProfile = PanOnlyMover();
        var panOnly = new PatchedFixture(panOnlyProfile, panOnlyProfile.Modes[0], 0, 3);
        patch.Add(panOnly);
        context.Programmer.SetChannel(0, 2, 99); // Pan on the pan-only fixture

        dispatcher.Dispatch(new StorePresetCommand(context.Presets, new[] { panOnly }, AttributeClass.Position, preset.Name, preset.Number, preset));

        Assert.Equal(99, preset.Values[ChannelType.Pan]); // updated
        Assert.Equal(20, preset.Values[ChannelType.Tilt]); // untouched from the first recording

        undoRedo.Undo();
        Assert.Equal(10, preset.Values[ChannelType.Pan]); // update fully reverted
        Assert.Equal(20, preset.Values[ChannelType.Tilt]);
    }

    [Fact]
    public void StorePresetCommand_Fails_WhenNoTargetHasMatchingAttributeChannels()
    {
        var (patch, context, dispatcher, _) = BuildConsole();
        var profile = RgbwPar(); // has no Position channels at all
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        patch.Add(fixture);

        var result = dispatcher.Dispatch(new StorePresetCommand(context.Presets, new[] { fixture }, AttributeClass.Position, "Nope", 1));

        Assert.False(result.Success);
        Assert.Empty(context.Presets.Presets);
    }

    [Fact]
    public void ApplyPresetCommand_WritesOnlyChannelsThePresetContains_AndUndoRestores()
    {
        var (patch, context, dispatcher, undoRedo) = BuildConsole();
        var profile = RgbwPar();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        patch.Add(fixture);
        context.Programmer.SetChannel(0, 1, 111); // pre-existing Green value

        var preset = new Preset { Class = AttributeClass.Color, Name = "Blue", Number = 1 };
        preset.Values[ChannelType.ColorRed] = 0;
        preset.Values[ChannelType.ColorBlue] = 255;
        context.Presets.Add(preset);

        var result = dispatcher.Dispatch(new ApplyPresetCommand(new[] { fixture }, preset));

        Assert.True(result.Success);
        Assert.Equal(preset, result.Preset);
        Assert.True(context.Programmer.TryGetChannelValue(0, 0, out var red));
        Assert.Equal(0, red);
        Assert.True(context.Programmer.TryGetChannelValue(0, 2, out var blue));
        Assert.Equal(255, blue);
        Assert.True(context.Programmer.TryGetChannelValue(0, 1, out var green)); // untouched by this preset
        Assert.Equal(111, green);

        undoRedo.Undo();
        Assert.False(context.Programmer.HasStoredValue(0, 0, out _));
        Assert.False(context.Programmer.HasStoredValue(0, 2, out _));
    }

    [Fact]
    public void ApplyPresetCommand_OnFixtureMissingSomeChannels_OnlyAppliesWhatExists()
    {
        var (patch, context, dispatcher, _) = BuildConsole();

        // Preset recorded from a Pan+Tilt fixture...
        var moverProfile = PanTiltMover();
        var mover = new PatchedFixture(moverProfile, moverProfile.Modes[0], 0, 1);
        patch.Add(mover);
        var preset = new Preset { Class = AttributeClass.Position, Name = "Centre", Number = 1 };
        preset.Values[ChannelType.Pan] = 128;
        preset.Values[ChannelType.Tilt] = 64;
        context.Presets.Add(preset);

        // ...applied to a Pan-only fixture: Tilt entry in the preset must simply be skipped, not throw.
        var panOnlyProfile = PanOnlyMover();
        var panOnly = new PatchedFixture(panOnlyProfile, panOnlyProfile.Modes[0], 0, 3);
        patch.Add(panOnly);

        var result = dispatcher.Dispatch(new ApplyPresetCommand(new[] { panOnly }, preset));

        Assert.True(result.Success);
        Assert.Contains(panOnly, result.AffectedFixtures);
        Assert.True(context.Programmer.TryGetChannelValue(0, 2, out var pan));
        Assert.Equal(128, pan);
    }

    [Fact]
    public void RemovePresetCommand_RemovesAndUndoReinserts()
    {
        var (_, context, dispatcher, undoRedo) = BuildConsole();
        var preset = new Preset { Class = AttributeClass.Beam, Name = "Open", Number = 1 };
        context.Presets.Add(preset);

        var result = dispatcher.Dispatch(new RemovePresetCommand(preset));

        Assert.True(result.Success);
        Assert.Empty(context.Presets.Presets);

        undoRedo.Undo();
        Assert.Contains(preset, context.Presets.Presets);
    }

    [Fact]
    public void RemovePresetCommand_FailsGracefully_WhenAlreadyRemoved()
    {
        var (_, context, dispatcher, _) = BuildConsole();
        var preset = new Preset { Class = AttributeClass.Beam, Name = "Open", Number = 1 };
        context.Presets.Add(preset);
        dispatcher.Dispatch(new RemovePresetCommand(preset));

        var result = dispatcher.Dispatch(new RemovePresetCommand(preset));

        Assert.False(result.Success);
    }
}

using DmxConsole.Application.CommandSurface;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Tests.CommandSurface;

/// <summary>docs/COMMAND_SURFACE_KEY_SPEC.md §6/§7/§9/§12 grammar: family-qualified/bare HOME,
/// family-qualified RELEASE, FULL, family PRESET recall, and Next/Last self-termination. Bare
/// RELEASE and RELEASE ENTER are NOT composer grammar (they're a CommandSurfaceViewModel-level
/// two-press gesture) - see CommandSurfaceViewModelTests.</summary>
public class CommandComposerFamilyActionTests
{
    private static FixtureProfile MovingHead() => new()
    {
        Id = "test-moving-head", Manufacturer = "Test", Model = "MovingHead",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "6ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 0 },
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 1, DefaultValue = 128 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2, DefaultValue = 128 },
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 3, DefaultValue = 0 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 4, DefaultValue = 0 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 5, DefaultValue = 0 },
                },
            },
        },
    };

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, PatchedFixture Fixture) BuildRig()
    {
        var patch = new Patch();
        var profile = MovingHead();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        patch.Add(fixture);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        return (context, dispatcher, undoRedo, fixture);
    }

    [Fact]
    public void FamilyHome_RestoresOnlyThatFamilysChannels_ForCurrentSelection()
    {
        var (context, dispatcher, _, fixture) = BuildRig();
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 1, 200); // Pan
        context.Programmer.SetChannel(0, 3, 200); // Red

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Position));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Home));

        Assert.True(final.IsComplete); // self-terminates - no Enter needed
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        context.Programmer.TryGetChannelValue(0, 1, out var pan);
        Assert.Equal(128, pan); // Pan restored to its own DefaultValue

        context.Programmer.TryGetChannelValue(0, 3, out var red);
        Assert.Equal(200, red); // Color untouched - Home was Position-only
    }

    [Fact]
    public void BareHome_RestoresEveryChannel_ForCurrentSelection()
    {
        var (context, dispatcher, _, fixture) = BuildRig();
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 1, 200); // Pan
        context.Programmer.SetChannel(0, 3, 200); // Red

        var composer = new CommandComposer(context);
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Home));

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        context.Programmer.TryGetChannelValue(0, 1, out var pan);
        context.Programmer.TryGetChannelValue(0, 3, out var red);
        Assert.Equal(128, pan);
        Assert.Equal(0, red);
    }

    [Fact]
    public void Home_IsOneUndoStep_AcrossEveryTouchedChannel()
    {
        var (context, dispatcher, undoRedo, fixture) = BuildRig();
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 1, 200);
        context.Programmer.SetChannel(0, 2, 210);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Position));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Home));
        dispatcher.Dispatch(final.ReadyOperation!);

        var outcome = undoRedo.Undo(); // one Undo() reverts BOTH Pan and Tilt
        Assert.True(outcome.Performed);
        context.Programmer.TryGetChannelValue(0, 1, out var pan);
        context.Programmer.TryGetChannelValue(0, 2, out var tilt);
        Assert.Equal(200, pan);
        Assert.Equal(210, tilt);
    }

    [Fact]
    public void Home_WithNoSelection_ReturnsIncomplete_NeverThrows()
    {
        var (context, _, _, _) = BuildRig();
        var composer = new CommandComposer(context);

        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Home));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
    }

    [Fact]
    public void FamilyRelease_RemovesOnlyThatFamilysEditorValues()
    {
        var (context, dispatcher, _, fixture) = BuildRig();
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 1, 200); // Pan
        context.Programmer.SetChannel(0, 3, 200); // Red

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Position));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Release));

        Assert.True(final.IsComplete); // self-terminates per §14's example list
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.False(context.Programmer.HasStoredValue(0, 1, out _)); // Pan released
        Assert.True(context.Programmer.HasStoredValue(0, 3, out var red)); // Color untouched
        Assert.Equal(200, red);
    }

    [Fact]
    public void FullAfterSelection_SetsIntensityTo100Percent_SelfTerminates_NoEnterNeeded()
    {
        var (context, dispatcher, _, fixture) = BuildRig();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Full));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        context.Programmer.TryGetChannelValue(0, 0, out var dimmer);
        Assert.Equal(255, dimmer);
    }

    [Fact]
    public void FamilyPreset_RecallsByNumber_AppliesOnlyMatchingChannels_RequiresEnter()
    {
        var (context, dispatcher, _, fixture) = BuildRig();
        context.Selection.Add(fixture);
        var preset = new Preset { Class = AttributeClass.Color, Number = 5, Name = "Blue" };
        preset.Values[ChannelType.ColorBlue] = 255;
        context.Presets.Add(preset);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Color));
        composer.Push(CommandToken.Simple(CommandTokenKind.Preset));
        var beforeEnter = composer.Push(CommandToken.Number(5));
        Assert.False(beforeEnter.IsComplete); // ends in a numeric token - needs Enter (§14)

        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));
        Assert.True(final.IsComplete);

        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        context.Programmer.TryGetChannelValue(0, 5, out var blue);
        Assert.Equal(255, blue);
    }

    [Fact]
    public void FamilyPreset_UnknownNumber_ReturnsError_NeverGuesses()
    {
        var (context, _, _, fixture) = BuildRig();
        context.Selection.Add(fixture);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Color));
        composer.Push(CommandToken.Simple(CommandTokenKind.Preset));
        composer.Push(CommandToken.Number(99));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
    }

    [Fact]
    public void Next_And_Previous_SelfTerminate_NoEnterNeeded()
    {
        var patch = new Patch();
        var profile = MovingHead();
        var f1 = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        var f2 = new PatchedFixture(profile, profile.Modes[0], 0, 20);
        patch.Add(f1);
        patch.Add(f2);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        context.Selection.Add(f1);

        var composer = new CommandComposer(context);
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Next));

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);
        Assert.Single(context.Selection.Items);
        Assert.Equal(f2.Number, context.Selection.Items[0].Number);
    }
}

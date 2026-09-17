using DmxConsole.Application.CommandSurface;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Tests.CommandSurface;

/// <summary>docs/COMMAND_SURFACE_KEY_SPEC.md §7 - PARAMETER RELEASE: a third, finer granularity
/// below RELEASE (all families) and FAMILY RELEASE (one family) - releases exactly one semantic
/// parameter, including every component of a multi-byte parameter (Pan+PanFine) as one atomic
/// unit, and leaves its family siblings (e.g. Tilt) untouched.</summary>
public class CommandComposerParameterReleaseTests
{
    private static FixtureProfile MovingHeadWithFinePan() => new()
    {
        Id = "test-fine-pan", Manufacturer = "Test", Model = "FinePan",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "5ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 },
                    new FixtureChannel { Name = "Pan Fine", Type = ChannelType.PanFine, Offset = 1 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2 },
                    new FixtureChannel { Name = "Zoom", Type = ChannelType.Zoom, Offset = 3 },
                    new FixtureChannel { Name = "Focus", Type = ChannelType.Focus, Offset = 4 },
                },
            },
        },
    };

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, PatchedFixture Fixture) BuildRig()
    {
        var patch = new Patch();
        var profile = MovingHeadWithFinePan();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        patch.Add(fixture);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        return (context, dispatcher, fixture);
    }

    /// <summary>1. PAN RELEASE releases Pan but leaves Tilt captured.</summary>
    [Fact]
    public void ParameterRelease_Pan_LeavesTiltCaptured()
    {
        var (context, dispatcher, fixture) = BuildRig();
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 200); // Pan
        context.Programmer.SetChannel(0, 2, 210); // Tilt (same Position family as Pan)

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Parameter(ChannelType.Pan));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Release));

        Assert.True(final.IsComplete); // self-terminates, like family RELEASE
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        Assert.False(context.Programmer.HasStoredValue(0, 0, out _)); // Pan released
        Assert.True(context.Programmer.HasStoredValue(0, 2, out var tilt)); // Tilt untouched
        Assert.Equal(210, tilt);
    }

    /// <summary>2. COLOR RELEASE (family) still releases the whole family - unchanged by
    /// PARAMETER RELEASE's addition, proving the two granularities coexist without interference.</summary>
    [Fact]
    public void FamilyRelease_StillReleasesTheWholeFamily_Unaffected()
    {
        var (context, dispatcher, fixture) = BuildRig();
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 3, 100); // Zoom (Beam)
        context.Programmer.SetChannel(0, 4, 120); // Focus (Beam)

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Beam));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Release));

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.False(context.Programmer.HasStoredValue(0, 3, out _));
        Assert.False(context.Programmer.HasStoredValue(0, 4, out _));
    }

    /// <summary>3. A multi-byte parameter (Pan = Pan+PanFine) releases as one complete semantic
    /// unit - addressing the coarse channel releases the fine companion too, atomically.</summary>
    [Fact]
    public void ParameterRelease_MultiByteParameter_ReleasesEveryComponent()
    {
        var (context, dispatcher, fixture) = BuildRig();
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 200); // Pan (coarse)
        context.Programmer.SetChannel(0, 1, 55);  // PanFine

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Parameter(ChannelType.Pan));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Release));
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.False(context.Programmer.HasStoredValue(0, 0, out _)); // Pan
        Assert.False(context.Programmer.HasStoredValue(0, 1, out _)); // PanFine - released too, atomically
    }

    /// <summary>Addressing via the FINE component resolves to the same complete parameter -
    /// PanFine RELEASE releases both Pan and PanFine, not just the fine byte.</summary>
    [Fact]
    public void ParameterRelease_AddressedByFineComponent_StillReleasesWholeParameter()
    {
        var (context, dispatcher, fixture) = BuildRig();
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 200);
        context.Programmer.SetChannel(0, 1, 55);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Parameter(ChannelType.PanFine));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Release));
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.False(context.Programmer.HasStoredValue(0, 0, out _));
        Assert.False(context.Programmer.HasStoredValue(0, 1, out _));
    }

    [Fact]
    public void ParameterRelease_SingleByteParameter_LeavesFamilySiblingsUntouched()
    {
        var (context, dispatcher, fixture) = BuildRig();
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 3, 100); // Zoom
        context.Programmer.SetChannel(0, 4, 120); // Focus (same Beam family as Zoom)

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Parameter(ChannelType.Zoom));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Release));
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.False(context.Programmer.HasStoredValue(0, 3, out _)); // Zoom released
        Assert.True(context.Programmer.HasStoredValue(0, 4, out var focus)); // Focus untouched
        Assert.Equal(120, focus);
    }

    [Fact]
    public void ParameterRelease_NoSelection_ReturnsIncomplete_NeverThrows()
    {
        var (context, _, _) = BuildRig();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Parameter(ChannelType.Zoom));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Release));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
    }
}

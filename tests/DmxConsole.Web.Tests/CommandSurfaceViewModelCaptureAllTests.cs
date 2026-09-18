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

/// <summary>docs/COMMAND_SURFACE_KEY_SPEC.md §8 - CAPTURE ALL's Command Surface entry point:
/// self-terminating, never touches Selection or SelectionCycleState (so a following CLEAR still
/// behaves exactly as if CAPTURE ALL had never been pressed).</summary>
public class CommandSurfaceViewModelCaptureAllTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private static (ConsoleContext Context, CommandSurfaceViewModel Surface) BuildRig()
    {
        var patch = new Patch();
        var profile = Dimmer1();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        patch.Add(fixture);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var surface = new CommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
        return (context, surface);
    }

    [Fact]
    public void PressCaptureAll_IsSelfTerminating_LeavesLineIdle_NoEnterNeeded()
    {
        var (_, surface) = BuildRig();

        surface.PressCaptureAll();

        Assert.Empty(surface.Current.Tokens); // back to idle immediately, no pending composition
        Assert.Null(surface.DispatchError);
    }

    [Fact]
    public void PressCaptureAll_DoesNotAlterSelection()
    {
        var (context, surface) = BuildRig();
        var fixture = context.Patch.Fixtures[0];
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 200);

        surface.PressCaptureAll();

        Assert.Single(context.Selection.Items);
        Assert.Equal(fixture, context.Selection.Items[0]);
    }

    /// <summary>The specific design risk this test guards against: PressCaptureAll bypasses the
    /// generic Push() completion path precisely so it never records a spurious SelectionCycleState
    /// gesture. If it didn't, a CLEAR pressed afterward could remove/alter a "gesture" that was
    /// never really a selection change, corrupting the CLEAR-hierarchy semantics already proven in
    /// CommandSurfaceViewModelClearTests.</summary>
    [Fact]
    public void PressCaptureAll_DoesNotRecordASelectionGesture_ClearStillBehavesNormallyAfter()
    {
        var (context, surface) = BuildRig();
        var fixture = context.Patch.Fixtures[0];

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Single(context.Selection.Items);

        surface.PressCaptureAll(); // must not push a gesture baseline onto the stack

        surface.PressClear(); // should remove the ORIGINAL "Fixture 1" gesture, not a phantom CaptureAll one

        Assert.Empty(context.Selection.Items);
    }

    [Fact]
    public void PressCaptureAll_MakesEditorOwnershipVisible_ViaProgrammerHasStoredValue()
    {
        var (context, surface) = BuildRig();
        var fixture = context.Patch.Fixtures[0];
        ((DmxOutputEngine)context.EffectiveOutput).AddLayer(context.Programmer);
        context.Programmer.SetChannel(0, 0, 90);
        ((DmxOutputEngine)context.EffectiveOutput).Tick();

        // Nothing was captured yet from a DIFFERENT source - this proves the direct, simplest
        // case: an already-Programmer-owned channel stays visible as an Editor value post-capture
        // (re-captured from itself), never silently dropped.
        surface.PressCaptureAll();

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(90, value);
    }
}

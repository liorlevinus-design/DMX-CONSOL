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
/// self-terminating, never touches Selection itself (so a following CLEAR still behaves exactly
/// as if CAPTURE ALL had never been pressed). Since the Selection Cycle stabilization slice,
/// CAPTURE ALL DOES mark the cycle closed (SelectionCycleState.MarkExecutionCompleted -
/// see CommandSurfaceViewModelSelectionCycleTests) - a programming action, per item 8 - even
/// though it never records a selection gesture and never mutates Selection.</summary>
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
        var surface = CommandSurfaceViewModelTestSupport.BuildCommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
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

    /// <summary>CLEAR (post-5de8472) is a single, stateless, Selection-only action - it always
    /// clears the WHOLE current Selection unconditionally, regardless of what CAPTURE ALL (or
    /// anything else) did before it. This test simply confirms CAPTURE ALL never puts Selection
    /// into some state that would make a following CLEAR behave any differently than always.</summary>
    [Fact]
    public void PressCaptureAll_ThenClear_ClearsTheWholeSelectionAsNormal()
    {
        var (context, surface) = BuildRig();
        var fixture = context.Patch.Fixtures[0];

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Single(context.Selection.Items);

        surface.PressCaptureAll();
        surface.PressClear();

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

    // ---------- Command Surface key-map completion slice: CAPTURE (bare) vs SHIFT+CAPTURE ----------

    /// <summary>Bare CAPTURE (no Shift) must never silently behave like Capture All - the
    /// authoritative key map distinguishes CAPTURE from SHIFT+CAPTURE explicitly, and an
    /// unimplemented gesture must say so rather than fake the nearest available behavior.</summary>
    [Fact]
    public void PressCapture_WithoutShift_DoesNotCaptureAll_ReportsNotImplemented()
    {
        var (context, surface) = BuildRig();
        var fixture = context.Patch.Fixtures[0];
        ((DmxOutputEngine)context.EffectiveOutput).AddLayer(context.Programmer);

        surface.PressCapture();

        Assert.NotNull(surface.DispatchError);
        Assert.False(context.Programmer.HasStoredValue(0, 0, out _)); // nothing captured
    }

    /// <summary>SHIFT+CAPTURE performs exactly what PressCaptureAll already does (same command,
    /// same effect), and consumes the Shift arm like every other Shift combination.</summary>
    [Fact]
    public void PressCapture_WithShift_PerformsCaptureAll_AndConsumesShift()
    {
        var (context, surface) = BuildRig();
        var fixture = context.Patch.Fixtures[0];
        ((DmxOutputEngine)context.EffectiveOutput).AddLayer(context.Programmer);
        context.Programmer.SetChannel(0, 0, 90);
        ((DmxOutputEngine)context.EffectiveOutput).Tick();

        surface.PressShift();
        Assert.True(surface.ShiftArmed);

        surface.PressCapture();

        Assert.False(surface.ShiftArmed); // consumed
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(90, value);
        Assert.Null(surface.DispatchError);
    }
}

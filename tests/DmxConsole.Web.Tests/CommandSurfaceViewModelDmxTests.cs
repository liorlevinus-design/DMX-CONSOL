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

/// <summary>DMX DIRECT ADDRESSING - the §8 dot-ambiguity resolution as the operator actually
/// experiences it through PressDigit/PressDecimalPoint: "." means Universe/Address separator
/// while a DmxAddress is expected next, and reverts to its ordinary decimal-point/Recall meaning
/// everywhere else - driven entirely by CommandComposition.ExpectedNext, never a Razor-level
/// string hack.</summary>
public class CommandSurfaceViewModelDmxTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, CommandSurfaceViewModel Surface) BuildRig()
    {
        var patch = new Patch();
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var surface = new CommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
        return (context, dispatcher, surface);
    }

    private static void TypeDigits(CommandSurfaceViewModel surface, string digits)
    {
        foreach (char c in digits) surface.PressDigit(c);
    }

    // Full keypad path: DMX, 1, ., 1, At, 5, 0, Enter -> Universe 1 / Address 1 / 50%.
    [Fact]
    public void FullKeypadEntry_ProducesCorrectUniverseAddressAndValue()
    {
        var (context, _, surface) = BuildRig();

        surface.PressToken(CommandTokenKind.Dmx);
        TypeDigits(surface, "1");
        surface.PressDecimalPoint(); // Universe/Address separator, not a decimal point
        TypeDigits(surface, "1");
        surface.PressToken(CommandTokenKind.At);
        TypeDigits(surface, "50");
        surface.PressToken(CommandTokenKind.Enter);

        // Operator Universe 1 -> internal universeId 0.
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal((byte)Math.Round(50 / 100.0 * 255.0), value);
    }

    // 9: Universe.Address dot parsing does not break decimal AT values afterward.
    [Fact]
    public void DotParsing_DoesNotBreakDecimalAtValue_AfterTheAddress()
    {
        var (context, _, surface) = BuildRig();

        surface.PressToken(CommandTokenKind.Dmx);
        TypeDigits(surface, "1");
        surface.PressDecimalPoint(); // Universe/Address separator
        TypeDigits(surface, "1");
        surface.PressToken(CommandTokenKind.At);
        TypeDigits(surface, "50");
        surface.PressDecimalPoint(); // now an ORDINARY decimal point - At value, not an address
        TypeDigits(surface, "5");
        surface.PressToken(CommandTokenKind.Enter);

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal((byte)Math.Round(50.5 / 100.0 * 255.0), value);
    }

    // 10: Universe.Address dot parsing does not break recall "." semantics elsewhere (regression
    // - FIXTURE . and AT . must still work exactly as before this slice).
    [Fact]
    public void DotParsing_DoesNotBreakFixtureRecall()
    {
        var (context, dispatcher, surface) = BuildRig();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);

        surface.PressToken(CommandTokenKind.Fixture);
        TypeDigits(surface, "1");
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Single(context.Selection.Items);
        context.Selection.Clear();

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDecimalPoint(); // FIXTURE . = recall last fixture selection, unaffected by DMX
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Single(context.Selection.Items);
        Assert.Equal(fixture, context.Selection.Items[0]);
    }

    [Fact]
    public void DotParsing_DoesNotBreakAtRecall()
    {
        var (context, _, surface) = BuildRig();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);

        surface.PressToken(CommandTokenKind.Fixture);
        TypeDigits(surface, "1");
        surface.PressToken(CommandTokenKind.At);
        TypeDigits(surface, "63");
        surface.PressToken(CommandTokenKind.Enter);

        surface.PressToken(CommandTokenKind.Fixture);
        TypeDigits(surface, "1");
        surface.PressToken(CommandTokenKind.At);
        surface.PressDecimalPoint(); // AT . = recall last At percent, unaffected by DMX
        surface.PressToken(CommandTokenKind.Enter);

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal((byte)Math.Round(63 / 100.0 * 255.0), value);
    }

    // 7 + 8: command/object context resets to FIXTURE after a DMX command completes - the very
    // next bare numeric command is a plain Fixture selection, never still "in DMX mode".
    [Fact]
    public void ContextResetsToFixture_AfterDmxCommandCompletes()
    {
        var (context, _, surface) = BuildRig();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1) { Number = 9 };
        context.Patch.Add(fixture);

        surface.PressToken(CommandTokenKind.Dmx);
        TypeDigits(surface, "1");
        surface.PressDecimalPoint();
        TypeDigits(surface, "1");
        surface.PressToken(CommandTokenKind.Full);
        Assert.Empty(surface.Current.Tokens); // DMX command self-terminated and reset

        TypeDigits(surface, "9");
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Contains(fixture, context.Selection.Items); // bare "9" meant FIXTURE 9, not DMX
    }

    [Fact]
    public void PressingDot_WithNoDigitsYet_InDmxContext_IsANoOp_NotRecallOrInvalidAddress()
    {
        var (context, _, surface) = BuildRig();

        surface.PressToken(CommandTokenKind.Dmx);
        surface.PressDecimalPoint(); // no Universe digits typed yet - must not misfire as Recall

        // Still waiting for a DmxAddress, nothing pushed/dispatched, no error.
        Assert.Null(surface.DispatchError);
        Assert.Single(surface.Current.Tokens); // just [Dmx] - the dot was ignored, not consumed into a bad token
    }
}

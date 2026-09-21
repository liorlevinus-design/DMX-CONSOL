using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;
using DmxConsole.Web.EditorToolBar;
using DmxConsole.Web.Services;

namespace DmxConsole.Web.Tests;

/// <summary>Backspace is the single editor of command-line input: with pending digits it deletes
/// one digit, otherwise it removes the last command token/entry, and on an empty command line it is
/// a no-op. It must never modify the committed Fixture Selection. PressToken(Backspace) and
/// PressBackspace must be the SAME behavior - there is deliberately no second, divergent path.</summary>
public class CommandSurfaceViewModelBackspaceTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private static (ConsoleContext Context, CommandSurfaceViewModel Surface) BuildRig(int fixtureCount)
    {
        var patch = new Patch();
        var profile = Dimmer1();
        for (int i = 1; i <= fixtureCount; i++)
            patch.Add(new PatchedFixture(profile, profile.Modes[0], 0, i, $"F{i}"));
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var surface = CommandSurfaceViewModelTestSupport.BuildCommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
        return (context, surface);
    }

    /// <summary>Pending digits: exactly ONE digit disappears per press, and the committed
    /// Selection is untouched.</summary>
    [Fact]
    public void Backspace_WithPendingDigits_DeletesOneDigit_AndLeavesSelectionUnchanged()
    {
        var (context, surface) = BuildRig(3);

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Single(context.Selection.Items); // Fixture 1 committed

        // The successful ENTER above already reset the command line, so this new composition starts
        // at AT - the point is that Backspace removes exactly ONE digit of the pending number.
        surface.PressToken(CommandTokenKind.At);
        surface.PressDigit('5');
        surface.PressDigit('7');
        Assert.Equal("AT 57", surface.DisplayPreview.ToUpperInvariant());

        surface.PressBackspace();

        Assert.Equal("AT 5", surface.DisplayPreview.ToUpperInvariant());
        Assert.Single(surface.Current.Tokens); // the committed At token is untouched
        Assert.Single(context.Selection.Items);
        Assert.Equal(1, context.Selection.Items[0].Number);
    }

    /// <summary>No pending digits: the last command TOKEN goes, one entry per press - and the
    /// committed Selection still stays exactly as it was.</summary>
    [Fact]
    public void Backspace_WithNoPendingDigits_RemovesLastCommandToken_AndLeavesSelectionUnchanged()
    {
        var (context, surface) = BuildRig(3);

        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);
        Assert.Single(context.Selection.Items);

        surface.PressToken(CommandTokenKind.At);
        Assert.Single(surface.Current.Tokens);
        Assert.Equal(CommandTokenKind.At, surface.Current.Tokens[0].Kind);

        surface.PressBackspace();

        Assert.Empty(surface.Current.Tokens);
        Assert.Equal(string.Empty, surface.DisplayPreview);
        Assert.Single(context.Selection.Items);
        Assert.Equal(1, context.Selection.Items[0].Number);
    }

    /// <summary>Empty command line: Backspace is a plain no-op, never an error and never a
    /// selection mutation.</summary>
    [Fact]
    public void Backspace_OnEmptyCommandLine_IsANoOp_AndLeavesSelectionUnchanged()
    {
        var (context, surface) = BuildRig(2);
        context.Selection.Add(context.Patch.FindByNumber(1)!);

        surface.PressBackspace();

        Assert.Empty(surface.Current.Tokens);
        Assert.Equal(string.Empty, surface.DisplayPreview);
        Assert.Null(surface.DispatchError);
        Assert.Single(context.Selection.Items);
        Assert.Equal(1, context.Selection.Items[0].Number);
    }

    /// <summary>REGRESSION: the generic PressToken(Backspace) path must delegate to
    /// PressBackspace, not commit the pending digits and then delete the resulting token. With
    /// "57" pending, one digit must disappear - the pending number must NOT be committed.</summary>
    [Fact]
    public void PressTokenBackspace_DelegatesToPressBackspace_SameSingleBehavior()
    {
        var (context, surface) = BuildRig(2);
        context.Selection.Add(context.Patch.FindByNumber(1)!);

        surface.PressToken(CommandTokenKind.At);
        surface.PressDigit('5');
        surface.PressDigit('7');
        Assert.Equal("AT 57", surface.DisplayPreview.ToUpperInvariant());

        surface.PressToken(CommandTokenKind.Backspace);

        Assert.Equal("AT 5", surface.DisplayPreview.ToUpperInvariant());
        Assert.Single(surface.Current.Tokens); // still just At - no Number token was committed
        Assert.Equal(CommandTokenKind.At, surface.Current.Tokens[0].Kind);
        Assert.Single(context.Selection.Items);
    }

    /// <summary>PressToken(Clear) is likewise the SAME behavior as the Clear key, never a separate
    /// composer-only path.</summary>
    [Fact]
    public void PressTokenClear_DelegatesToPressClear_SameSingleBehavior()
    {
        var (context, surface) = BuildRig(2);
        context.Selection.Add(context.Patch.FindByNumber(1)!);
        context.Selection.Add(context.Patch.FindByNumber(2)!);

        surface.PressToken(CommandTokenKind.At); // a partial command line - CLEAR resets this too

        surface.PressToken(CommandTokenKind.Clear);

        Assert.Empty(context.Selection.Items); // the selection cleared
        Assert.Empty(surface.Current.Tokens); // the command line reset too (§B item 2)
    }
}

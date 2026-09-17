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

/// <summary>docs/COMMAND_SURFACE_KEY_SPEC.md §7/§15/§18 - the UI-level gestures that live above
/// CommandComposer's pure grammar: bare RELEASE's two-press escalation to "Clear Entire Editor",
/// SHIFT+RELEASE (Release All Playbacks), and CLEAR's digit-backspace priority.</summary>
public class CommandSurfaceViewModelTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, CommandSurfaceViewModel Surface, PatchedFixture Fixture) BuildRig()
    {
        var patch = new Patch();
        var profile = Dimmer1();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        patch.Add(fixture);
        var engine = new DmxOutputEngine(patch);
        var executors = new ExecutorBank();
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), executors);
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var surface = new CommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
        return (context, dispatcher, surface, fixture);
    }

    [Fact]
    public void BareRelease_OnEmptyLine_ReleasesCurrentSelection_Immediately_NoEnterNeeded()
    {
        var (context, _, surface, fixture) = BuildRig();
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 200);

        surface.PressRelease();

        Assert.False(context.Programmer.HasStoredValue(0, 0, out _));
        Assert.Null(surface.DispatchError);
    }

    [Fact]
    public void ReleaseThenEnter_Immediately_EscalatesToClearEntireEditor_AllPatchedFixtures()
    {
        var (context, _, surface, fixture) = BuildRig();
        var other = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 10);
        context.Patch.Add(other);
        context.Selection.Add(fixture); // only fixture 1 selected
        context.Programmer.SetChannel(0, 0, 200); // fixture 1's Dimmer
        context.Programmer.SetChannel(other.UniverseId, other.AbsoluteIndex(other.FindChannel(ChannelType.Dimmer)!), 150); // NOT selected

        surface.PressRelease(); // first press: releases only the selection (fixture 1)
        Assert.True(context.Programmer.HasStoredValue(other.UniverseId, other.AbsoluteIndex(other.FindChannel(ChannelType.Dimmer)!), out _));

        surface.PressToken(CommandTokenKind.Enter); // immediately-following Enter: escalates

        Assert.False(context.Programmer.HasStoredValue(0, 0, out _));
        Assert.False(context.Programmer.HasStoredValue(other.UniverseId, other.AbsoluteIndex(other.FindChannel(ChannelType.Dimmer)!), out _));
    }

    [Fact]
    public void Enter_WithoutAPrecedingRelease_NeverEscalates_PlainNoOpOnEmptyLine()
    {
        var (context, _, surface, fixture) = BuildRig();
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 200);

        surface.PressToken(CommandTokenKind.Enter); // no Release preceded this

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(200, value); // untouched - Enter alone on an empty line does nothing
    }

    [Fact]
    public void AnyOtherKey_BetweenReleaseAndEnter_CancelsTheEscalation()
    {
        var (context, _, surface, fixture) = BuildRig();
        var other = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 10);
        context.Patch.Add(other);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(other.UniverseId, other.AbsoluteIndex(other.FindChannel(ChannelType.Dimmer)!), 150);

        surface.PressRelease();
        surface.PressDigit('5'); // an unrelated key press in between
        surface.PressClear(); // clean up the stray digit before Enter
        surface.PressToken(CommandTokenKind.Enter);

        // Escalation was cancelled by the digit press - the untouched fixture's value survives.
        Assert.True(context.Programmer.HasStoredValue(other.UniverseId, other.AbsoluteIndex(other.FindChannel(ChannelType.Dimmer)!), out _));
    }

    [Fact]
    public void ShiftRelease_StopsEveryRunningExecutor_NeverTouchesEditorOrSelection()
    {
        var (context, _, surface, fixture) = BuildRig();
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 123); // an Editor value that must survive

        var cueList = new CueList();
        cueList.RecordCue(context.Patch, new Programmer(), context.Selection, context.EffectiveOutput, "Cue 1", 1,
            new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));
        var executor = context.Executors.Add(1);
        executor.Assign(cueList);
        cueList.Go();
        Assert.True(cueList.IsActive);

        surface.PressShift();
        Assert.True(surface.ShiftArmed);
        surface.PressRelease();

        Assert.False(surface.ShiftArmed);
        Assert.False(cueList.IsActive); // stopped
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var editorValue)); // Editor untouched
        Assert.Equal(123, editorValue);
        Assert.Contains(fixture, context.Selection.Items); // Selection untouched
    }

    /// <summary>4a. FAMILY RELEASE must never arm the RELEASE ENTER escalation - only bare
    /// RELEASE (empty command line) does that. If it did, this test's untouched second fixture's
    /// value would be wiped by the immediately-following Enter.</summary>
    [Fact]
    public void FamilyRelease_DoesNotArmReleaseEnterEscalation()
    {
        var (context, _, surface, fixture) = BuildRig();
        context.Selection.Add(fixture);
        var other = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 10);
        context.Patch.Add(other);
        int otherIndex = other.AbsoluteIndex(other.FindChannel(ChannelType.Dimmer)!);
        context.Programmer.SetChannel(other.UniverseId, otherIndex, 150);

        surface.PressToken(CommandTokenKind.Intensity);
        surface.PressToken(CommandTokenKind.Release); // family RELEASE - self-terminates, must not arm escalation
        surface.PressToken(CommandTokenKind.Enter); // if escalation had armed, this would clear every patched fixture

        Assert.True(context.Programmer.HasStoredValue(other.UniverseId, otherIndex, out _)); // untouched - proves no escalation happened
    }

    /// <summary>4b. PARAMETER RELEASE must likewise never arm the escalation.</summary>
    [Fact]
    public void ParameterRelease_DoesNotArmReleaseEnterEscalation()
    {
        var (context, _, surface, fixture) = BuildRig();
        context.Selection.Add(fixture);
        var other = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 10);
        context.Patch.Add(other);
        int otherIndex = other.AbsoluteIndex(other.FindChannel(ChannelType.Dimmer)!);
        context.Programmer.SetChannel(other.UniverseId, otherIndex, 150);

        surface.PressParameter(ChannelType.Dimmer);
        surface.PressToken(CommandTokenKind.Release); // parameter RELEASE - self-terminates, must not arm escalation
        surface.PressToken(CommandTokenKind.Enter);

        Assert.True(context.Programmer.HasStoredValue(other.UniverseId, otherIndex, out _)); // untouched - proves no escalation happened
    }

    [Fact]
    public void ShiftArmed_IsConsumedByTheVeryNextKey_EvenIfUndefined()
    {
        var (_, _, surface, _) = BuildRig();

        surface.PressShift();
        Assert.True(surface.ShiftArmed);

        surface.PressDigit('5'); // Shift+digit has no defined meaning - just clears Shift

        Assert.False(surface.ShiftArmed);
    }

    [Fact]
    public void Clear_WhileDigitPending_BackspacesOneDigit_BeforeTouchingSelectionOrTokens()
    {
        var (context, _, surface, _) = BuildRig();
        surface.PressToken(CommandTokenKind.Fixture);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.At);
        surface.PressDigit('5');
        surface.PressDigit('7');

        surface.PressClear(); // "AT 57" -> "AT 5"
        Assert.Equal("FIXTURE 1 AT 5", surface.DisplayPreview.ToUpperInvariant());

        surface.PressClear(); // "AT 5" -> "AT "
        Assert.Equal("FIXTURE 1 AT", surface.DisplayPreview.ToUpperInvariant());
    }
}

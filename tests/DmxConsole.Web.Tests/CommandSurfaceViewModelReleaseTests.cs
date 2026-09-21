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

/// <summary>
/// RELEASE-panel slice (§C/§D): RELEASE is a two-step contextual gesture, never an immediate
/// mutation. First press arms a family-choice softkey context only; a second RELEASE press or
/// ENTER confirms it. With families chosen, both confirmations behave identically (scoped to the
/// current Selection); with none chosen, they diverge - ENTER releases the current Selection's
/// Programmer values, a second RELEASE clears the entire Programmer globally, independent of
/// Selection. The old "RELEASE fires immediately, ENTER escalates to Clear Entire Editor"
/// two-press gesture is retired.
/// </summary>
public class CommandSurfaceViewModelReleaseTests
{
    private static FixtureProfile MovingHead() => new()
    {
        Id = "test-mh", Manufacturer = "Test", Model = "MovingHead",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "3ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 0 },
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 1, DefaultValue = 128 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2, DefaultValue = 128 },
                },
            },
        },
    };

    /// <summary>RELEASE-family verification slice: adds Color (Red/Green/Blue) and Beam (Focus)
    /// channels on top of MovingHead's existing Dimmer/Pan/Tilt, so a COLOR-family release can be
    /// checked against every OTHER representable family at once (Intensity/Position/Beam), not
    /// just one spot-checked neighbor.</summary>
    private static FixtureProfile ColorMovingHead() => new()
    {
        Id = "test-color-mh", Manufacturer = "Test", Model = "ColorMovingHead",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "7ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 0 },
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 1, DefaultValue = 128 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2, DefaultValue = 128 },
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 3, DefaultValue = 0 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 4, DefaultValue = 0 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 5, DefaultValue = 0 },
                    new FixtureChannel { Name = "Focus", Type = ChannelType.Focus, Offset = 6, DefaultValue = 0 },
                },
            },
        },
    };

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, CommandSurfaceViewModel Surface, PatchedFixture A, PatchedFixture B, DmxOutputEngine Engine) BuildRig()
    {
        var patch = new Patch();
        var profile = MovingHead();
        var a = new PatchedFixture(profile, profile.Modes[0], 0, 1) { Number = 1 };
        var b = new PatchedFixture(profile, profile.Modes[0], 0, 10) { Number = 2 };
        patch.Add(a);
        patch.Add(b);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var surface = CommandSurfaceViewModelTestSupport.BuildCommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
        return (context, dispatcher, surface, a, b, engine);
    }

    private static int DimmerIndex(PatchedFixture f) => f.AbsoluteIndex(f.FindChannel(ChannelType.Dimmer)!);
    private static int PanIndex(PatchedFixture f) => f.AbsoluteIndex(f.FindChannel(ChannelType.Pan)!);

    [Fact]
    public void FirstRelease_ArmsContextOnly_NoDataMutation()
    {
        var (context, _, surface, a, _, _) = BuildRig();
        context.Selection.Add(a);
        context.Programmer.SetChannel(a.UniverseId, DimmerIndex(a), 200);
        context.Programmer.SetChannel(a.UniverseId, PanIndex(a), 60);

        surface.PressRelease();

        Assert.True(surface.ReleaseArmed);
        Assert.Empty(surface.ArmedReleaseFamilies);
        Assert.True(context.Programmer.HasStoredValue(a.UniverseId, DimmerIndex(a), out var dimmer));
        Assert.Equal(200, dimmer);
        Assert.True(context.Programmer.HasStoredValue(a.UniverseId, PanIndex(a), out var pan));
        Assert.Equal(60, pan);
        Assert.False(context.SelectionCycle.StartFreshOnNextSelection); // no programming action happened
        Assert.Contains(a, context.Selection.Items);
    }

    [Fact]
    public void Release_Family_Enter_ReleasesOnlyThatFamily_ForCurrentSelection()
    {
        var (context, _, surface, a, _, _) = BuildRig();
        context.Selection.Add(a);
        context.Programmer.SetChannel(a.UniverseId, DimmerIndex(a), 200);
        context.Programmer.SetChannel(a.UniverseId, PanIndex(a), 60);

        surface.PressRelease();
        surface.ToggleReleaseFamily(AttributeClass.Position);
        surface.PressToken(CommandTokenKind.Enter);

        Assert.False(surface.ReleaseArmed);
        Assert.False(context.Programmer.HasStoredValue(a.UniverseId, PanIndex(a), out _)); // Position released
        Assert.True(context.Programmer.HasStoredValue(a.UniverseId, DimmerIndex(a), out var dimmer)); // Intensity untouched
        Assert.Equal(200, dimmer);
        Assert.True(context.SelectionCycle.StartFreshOnNextSelection); // a programming action closed the cycle
    }

    [Fact]
    public void Release_Family_SecondReleasePress_ConfirmsTheSameWayAsEnter()
    {
        var (context, _, surface, a, _, _) = BuildRig();
        context.Selection.Add(a);
        context.Programmer.SetChannel(a.UniverseId, DimmerIndex(a), 200);
        context.Programmer.SetChannel(a.UniverseId, PanIndex(a), 60);

        surface.PressRelease();
        surface.ToggleReleaseFamily(AttributeClass.Position);
        surface.PressRelease(); // second RELEASE, not ENTER

        Assert.False(surface.ReleaseArmed);
        Assert.False(context.Programmer.HasStoredValue(a.UniverseId, PanIndex(a), out _));
        Assert.True(context.Programmer.HasStoredValue(a.UniverseId, DimmerIndex(a), out var dimmer));
        Assert.Equal(200, dimmer);
        Assert.True(context.SelectionCycle.StartFreshOnNextSelection);
    }

    [Fact]
    public void Release_MultipleFamilies_ReleasesAllChosenFamilies_InOneGesture()
    {
        var (context, _, surface, a, _, _) = BuildRig();
        context.Selection.Add(a);
        context.Programmer.SetChannel(a.UniverseId, DimmerIndex(a), 200);
        context.Programmer.SetChannel(a.UniverseId, PanIndex(a), 60);

        surface.PressRelease();
        surface.ToggleReleaseFamily(AttributeClass.Intensity);
        surface.ToggleReleaseFamily(AttributeClass.Position);
        surface.PressToken(CommandTokenKind.Enter);

        Assert.False(context.Programmer.HasStoredValue(a.UniverseId, DimmerIndex(a), out _));
        Assert.False(context.Programmer.HasStoredValue(a.UniverseId, PanIndex(a), out _));
    }

    [Fact]
    public void Release_NoFamily_Enter_ReleasesAllProgrammerValues_ForCurrentSelectionOnly()
    {
        var (context, _, surface, a, b, _) = BuildRig();
        context.Selection.Add(a); // only A is selected
        context.Programmer.SetChannel(a.UniverseId, DimmerIndex(a), 200);
        context.Programmer.SetChannel(a.UniverseId, PanIndex(a), 60);
        context.Programmer.SetChannel(b.UniverseId, DimmerIndex(b), 150); // B is NOT selected

        surface.PressRelease();
        surface.PressToken(CommandTokenKind.Enter); // no family chosen

        Assert.False(context.Programmer.HasStoredValue(a.UniverseId, DimmerIndex(a), out _));
        Assert.False(context.Programmer.HasStoredValue(a.UniverseId, PanIndex(a), out _));
        Assert.True(context.Programmer.HasStoredValue(b.UniverseId, DimmerIndex(b), out var bValue)); // untouched - not selected
        Assert.Equal(150, bValue);
    }

    [Fact]
    public void Release_NoFamily_SecondReleasePress_ClearsEntireProgrammerGlobally_IndependentOfSelection()
    {
        var (context, _, surface, a, b, _) = BuildRig();
        context.Selection.Add(a); // B is deliberately NOT selected and NOT armed with a family
        context.Programmer.SetChannel(a.UniverseId, DimmerIndex(a), 200);
        context.Programmer.SetChannel(b.UniverseId, DimmerIndex(b), 150);

        surface.PressRelease();
        surface.PressRelease(); // second RELEASE, no family chosen -> global clear

        Assert.False(context.Programmer.HasStoredValue(a.UniverseId, DimmerIndex(a), out _));
        Assert.False(context.Programmer.HasStoredValue(b.UniverseId, DimmerIndex(b), out _)); // B cleared too - global, not selection-scoped
        Assert.True(context.SelectionCycle.StartFreshOnNextSelection);
    }

    [Fact]
    public void Release_NoFamily_SecondReleasePress_WorksEvenWithEmptySelection()
    {
        var (context, _, surface, a, b, _) = BuildRig();
        // Nothing selected at all.
        context.Programmer.SetChannel(a.UniverseId, DimmerIndex(a), 200);
        context.Programmer.SetChannel(b.UniverseId, DimmerIndex(b), 150);

        surface.PressRelease();
        surface.PressRelease();

        Assert.Null(surface.DispatchError);
        Assert.False(context.Programmer.HasStoredValue(a.UniverseId, DimmerIndex(a), out _));
        Assert.False(context.Programmer.HasStoredValue(b.UniverseId, DimmerIndex(b), out _));
    }

    [Fact]
    public void Release_NoFamily_Enter_WithEmptySelection_FailsCleanly_NoGlobalSideEffect()
    {
        var (context, _, surface, a, b, _) = BuildRig();
        context.Programmer.SetChannel(a.UniverseId, DimmerIndex(a), 200);
        context.Programmer.SetChannel(b.UniverseId, DimmerIndex(b), 150);

        surface.PressRelease();
        surface.PressToken(CommandTokenKind.Enter); // no family, no selection - scoped path, not global

        Assert.NotNull(surface.DispatchError);
        Assert.True(context.Programmer.HasStoredValue(a.UniverseId, DimmerIndex(a), out _)); // untouched
        Assert.True(context.Programmer.HasStoredValue(b.UniverseId, DimmerIndex(b), out _)); // untouched
    }

    [Fact]
    public void ShiftRelease_RemainsASeparatePlaybackAction_NeverArmsTheProgrammerReleaseContext()
    {
        var (context, dispatcher, surface, a, _, engine) = BuildRig();
        var cueList = new CueList();
        cueList.RecordCue(context.Patch, context.Programmer, context.Selection, engine, "Cue 1", 1,
            new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));
        var executor = context.Executors.Add(1);
        executor.Assign(cueList);
        cueList.Go();
        Assert.True(cueList.IsActive);

        context.Programmer.SetChannel(a.UniverseId, DimmerIndex(a), 200); // must survive - Shift+Release is not a Programmer op

        surface.PressShift();
        surface.PressRelease();

        Assert.False(cueList.IsActive); // playback stopped
        Assert.False(surface.ReleaseArmed); // never entered the family-arm state machine
        Assert.True(context.Programmer.HasStoredValue(a.UniverseId, DimmerIndex(a), out var value));
        Assert.Equal(200, value);
    }

    /// <summary>Mirrors CaptureAllCommandTests.ReleaseAfterCapture_RevealsUnderlyingPlaybackValueAgain
    /// for the RELEASE-panel path: releasing the Programmer's ownership of a channel a CueList is
    /// also contributing to reveals the CueList's own value/ownership again.</summary>
    [Fact]
    public void Release_RevealsUnderlyingPlaybackValueAgain()
    {
        var (context, _, surface, a, _, engine) = BuildRig();
        int dimmerIdx = DimmerIndex(a);
        context.Selection.Add(a);
        context.Programmer.SetChannel(a.UniverseId, dimmerIdx, 128); // grab the value that will be recorded
        var cueList = new CueList();
        cueList.RecordCue(context.Patch, context.Programmer, context.Selection, engine, "Cue 1", 1,
            new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));
        var executor = context.Executors.Add(1);
        executor.Assign(cueList);
        engine.AddLayer(context.Programmer);
        engine.AddLayer(executor);
        cueList.Go();
        engine.Tick();
        Assert.Equal(OwnerKind.Programmer, engine.GetOwner(a.UniverseId, dimmerIdx)!.Kind); // Programmer wins first

        surface.PressRelease();
        surface.PressToken(CommandTokenKind.Enter); // release ALL for current Selection
        engine.Tick();

        Assert.False(context.Programmer.HasStoredValue(a.UniverseId, dimmerIdx, out _));
        Assert.Equal((byte)128, engine.GetEffectiveValue(a.UniverseId, dimmerIdx)); // Cue's own value reappears
        Assert.Equal(OwnerKind.Executor, engine.GetOwner(a.UniverseId, dimmerIdx)!.Kind); // provenance reverts to the Executor
    }

    /// <summary>RELEASE-family verification slice: "RELEASE -&gt; COLOR -&gt; ENTER" (the panel
    /// path - see CommandComposerFamilyActionTests.ColorRelease_... for the equivalent "COLOR
    /// RELEASE" composer-grammar path) with multiple families touched, a non-selected fixture
    /// also touched, and a CueList contributing an underlying Color value beneath the Programmer -
    /// verifies every required behavior in one place: only COLOR is released, only the current
    /// Selection is affected, Intensity/Position/Beam survive, the non-selected fixture is fully
    /// untouched, the Selection itself stays active, the SelectionCycle closes normally (a
    /// programming action happened), and the CueList's own Color value reappears underneath.</summary>
    [Fact]
    public void ReleaseFamilyPanel_Color_ReleasesOnlyColor_ForSelectionOnly_RevealsPlaybackValue_KeepsSelectionActive()
    {
        var patch = new Patch();
        var profile = ColorMovingHead();
        var selected = new PatchedFixture(profile, profile.Modes[0], 0, 1) { Number = 1 };
        var other = new PatchedFixture(profile, profile.Modes[0], 0, 10) { Number = 2 };
        patch.Add(selected);
        patch.Add(other);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var surface = CommandSurfaceViewModelTestSupport.BuildCommandSurfaceViewModel(context, dispatcher, new EditorContextStack());

        int dimmer = selected.AbsoluteIndex(selected.FindChannel(ChannelType.Dimmer)!);
        int pan = selected.AbsoluteIndex(selected.FindChannel(ChannelType.Pan)!);
        int red = selected.AbsoluteIndex(selected.FindChannel(ChannelType.ColorRed)!);
        int focus = selected.AbsoluteIndex(selected.FindChannel(ChannelType.Focus)!);
        int otherRed = other.AbsoluteIndex(other.FindChannel(ChannelType.ColorRed)!);

        context.Selection.Add(selected); // only `selected` is in the current Selection
        context.Programmer.SetChannel(0, dimmer, 200); // Intensity
        context.Programmer.SetChannel(0, pan, 60);     // Position
        context.Programmer.SetChannel(0, red, 128);    // Color - this is the value a Cue will also record
        context.Programmer.SetChannel(0, focus, 90);   // Beam
        context.Programmer.SetChannel(0, otherRed, 210); // Color on the NON-selected fixture

        // A CueList contributes its own Color value underneath the Programmer's, so releasing
        // Color must reveal IT again, not just clear to a default/zero value.
        var cueList = new CueList();
        cueList.RecordCue(context.Patch, context.Programmer, context.Selection, engine, "Cue 1", 1,
            new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));
        var executor = context.Executors.Add(1);
        executor.Assign(cueList);
        engine.AddLayer(context.Programmer);
        engine.AddLayer(executor);
        cueList.Go();
        engine.Tick();
        Assert.Equal(OwnerKind.Programmer, engine.GetOwner(0, red)!.Kind); // Programmer wins first

        surface.PressRelease();
        surface.ToggleReleaseFamily(AttributeClass.Color);
        surface.PressToken(CommandTokenKind.Enter);
        engine.Tick();

        // Only Color released, only on the selected fixture.
        Assert.False(context.Programmer.HasStoredValue(0, red, out _));
        Assert.True(context.Programmer.HasStoredValue(0, dimmer, out var dimmerAfter)); Assert.Equal(200, dimmerAfter);
        Assert.True(context.Programmer.HasStoredValue(0, pan, out var panAfter)); Assert.Equal(60, panAfter);
        Assert.True(context.Programmer.HasStoredValue(0, focus, out var focusAfter)); Assert.Equal(90, focusAfter);
        Assert.True(context.Programmer.HasStoredValue(other.UniverseId, otherRed, out var otherAfter)); Assert.Equal(210, otherAfter);

        // The underlying Cue value reappears - provenance reverts to the Executor.
        Assert.Equal((byte)128, engine.GetEffectiveValue(0, red));
        Assert.Equal(OwnerKind.Executor, engine.GetOwner(0, red)!.Kind);

        // Selection stays active; the SelectionCycle closes normally (a programming action ran).
        Assert.Contains(selected, context.Selection.Items);
        Assert.True(context.SelectionCycle.StartFreshOnNextSelection);
    }
}

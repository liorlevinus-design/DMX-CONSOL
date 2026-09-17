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

/// <summary>docs/COMMAND_SURFACE_KEY_SPEC.md PARAMETER PICKER slice - closes the gap between the
/// parameter-level grammar/API that already exists (PAN RELEASE, ZOOM RELEASE) and having a
/// visible way to reach it. These tests exercise ParameterPickerViewModel purely against real
/// FixtureChannel data (no invented parameters, no second taxonomy) and confirm it routes
/// exclusively through CommandSurfaceViewModel.PressParameter - the same call any other entry
/// point already uses.</summary>
public class ParameterPickerViewModelTests
{
    // A moving head with a full semantic parameter set across four families: Position (Pan/Tilt,
    // each coarse+fine), Beam (Zoom, Focus), Image (Gobo), Intensity (Dimmer). Shape has no
    // ChannelType member at all today (ChannelTypeExtensions.ToAttributeClass's own doc comment) -
    // used deliberately below to test the honest "no parameters" state without inventing one.
    private static FixtureProfile MovingHeadFull() => new()
    {
        Id = "test-mh-full", Manufacturer = "Test", Model = "MovingHeadFull",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "8ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 },
                    new FixtureChannel { Name = "Pan Fine", Type = ChannelType.PanFine, Offset = 1 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2 },
                    new FixtureChannel { Name = "Tilt Fine", Type = ChannelType.TiltFine, Offset = 3 },
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 4 },
                    new FixtureChannel { Name = "Zoom", Type = ChannelType.Zoom, Offset = 5 },
                    new FixtureChannel { Name = "Focus", Type = ChannelType.Focus, Offset = 6 },
                    new FixtureChannel { Name = "Gobo", Type = ChannelType.Gobo, Offset = 7 },
                },
            },
        },
    };

    // Same family shape, but no Focus channel - used for mixed-selection/partial-support tests.
    private static FixtureProfile MovingHeadNoFocus() => new()
    {
        Id = "test-mh-nofocus", Manufacturer = "Test", Model = "MovingHeadNoFocus",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "6ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 },
                    new FixtureChannel { Name = "Pan Fine", Type = ChannelType.PanFine, Offset = 1 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2 },
                    new FixtureChannel { Name = "Tilt Fine", Type = ChannelType.TiltFine, Offset = 3 },
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 4 },
                    new FixtureChannel { Name = "Zoom", Type = ChannelType.Zoom, Offset = 5 },
                },
            },
        },
    };

    private sealed record Rig(ConsoleContext Context, CommandDispatcher Dispatcher, CommandSurfaceViewModel Surface, ParameterPickerViewModel Picker);

    private static Rig BuildRig()
    {
        var patch = new Patch();
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var surface = new CommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
        var picker = new ParameterPickerViewModel(context, surface);
        return new Rig(context, dispatcher, surface, picker);
    }

    private static PatchedFixture Patch(Rig rig, FixtureProfile profile, int number, int address = 1)
    {
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, address) { Number = number };
        rig.Context.Patch.Add(fixture);
        return fixture;
    }

    // 1: POSITION context exposes Pan/Tilt when supported.
    [Fact]
    public void PositionFamily_ExposesPanAndTilt()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);
        rig.Surface.PressToken(CommandTokenKind.Position);

        var types = rig.Picker.Options.Select(o => o.Type).ToList();

        Assert.Contains(ChannelType.Pan, types);
        Assert.Contains(ChannelType.Tilt, types);
    }

    // 2: BEAM context exposes Zoom/Focus where supported.
    [Fact]
    public void BeamFamily_ExposesZoomAndFocus()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);
        rig.Surface.PressToken(CommandTokenKind.Beam);

        var types = rig.Picker.Options.Select(o => o.Type).ToList();

        Assert.Contains(ChannelType.Zoom, types);
        Assert.Contains(ChannelType.Focus, types);
    }

    // 3: no unsupported parameter appears - Gobo (Image) must never show up under Position.
    [Fact]
    public void NoUnsupportedParameter_AppearsInAnotherFamily()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);
        rig.Surface.PressToken(CommandTokenKind.Position);

        var types = rig.Picker.Options.Select(o => o.Type).ToList();

        Assert.DoesNotContain(ChannelType.Gobo, types);
        Assert.DoesNotContain(ChannelType.Dimmer, types);
        Assert.DoesNotContain(ChannelType.Zoom, types);
    }

    // 4: selecting a parameter routes through PressParameter(ChannelType) - proven by the
    // composer ending up in exactly the state PressParameter itself produces (a lone Parameter
    // token), never a second, independent write path.
    [Fact]
    public void SelectParameter_RoutesThroughPressParameter()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);
        rig.Surface.PressToken(CommandTokenKind.Position);

        rig.Picker.SelectParameter(ChannelType.Pan);

        Assert.Single(rig.Surface.Current.Tokens);
        Assert.Equal(CommandTokenKind.Parameter, rig.Surface.Current.Tokens[0].Kind);
        Assert.Equal(ChannelType.Pan, (ChannelType)rig.Surface.Current.Tokens[0].SemanticPayload!);
        // The prior family token is discarded, not appended to - "POSITION, PAN" collapses to
        // just "PAN", matching the documented "POSITION / PAN / RELEASE" == "PAN RELEASE" workflow.
    }

    // 5: PAN RELEASE releases Pan only, not Tilt.
    [Fact]
    public void PickingPan_ThenRelease_ReleasesPanOnly_NotTilt()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);
        rig.Context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!), 100);
        rig.Context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Tilt)!), 200);

        rig.Surface.PressToken(CommandTokenKind.Position);
        rig.Picker.SelectParameter(ChannelType.Pan);
        rig.Surface.PressToken(CommandTokenKind.Release);

        Assert.False(rig.Context.Programmer.HasStoredValue(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!), out _));
        Assert.True(rig.Context.Programmer.HasStoredValue(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Tilt)!), out var tiltValue));
        Assert.Equal((byte)200, tiltValue);
    }

    // 6: ZOOM RELEASE releases Zoom only.
    [Fact]
    public void PickingZoom_ThenRelease_ReleasesZoomOnly_NotFocus()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);
        rig.Context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Zoom)!), 50);
        rig.Context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Focus)!), 75);

        rig.Surface.PressToken(CommandTokenKind.Beam);
        rig.Picker.SelectParameter(ChannelType.Zoom);
        rig.Surface.PressToken(CommandTokenKind.Release);

        Assert.False(rig.Context.Programmer.HasStoredValue(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Zoom)!), out _));
        Assert.True(rig.Context.Programmer.HasStoredValue(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Focus)!), out var focusValue));
        Assert.Equal((byte)75, focusValue);
    }

    // 7: coarse/fine pair appears as one semantic parameter, never Pan + PanFine separately.
    [Fact]
    public void CoarseFinePair_AppearsAsOneSemanticParameter()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);
        rig.Surface.PressToken(CommandTokenKind.Position);

        var types = rig.Picker.Options.Select(o => o.Type).ToList();

        Assert.Equal(2, types.Count); // Pan, Tilt - never 4
        Assert.Contains(ChannelType.Pan, types);
        Assert.Contains(ChannelType.Tilt, types);
        Assert.DoesNotContain(ChannelType.PanFine, types);
        Assert.DoesNotContain(ChannelType.TiltFine, types);
    }

    // 8: mixed fixture selection marks partial support correctly - Focus exists only on one of
    // the two selected fixtures.
    [Fact]
    public void MixedSelection_MarksPartialSupportCorrectly()
    {
        var rig = BuildRig();
        var withFocus = Patch(rig, MovingHeadFull(), 1, address: 1);
        var withoutFocus = Patch(rig, MovingHeadNoFocus(), 2, address: 20);
        rig.Context.Selection.Add(withFocus);
        rig.Context.Selection.Add(withoutFocus);
        rig.Surface.PressToken(CommandTokenKind.Beam);

        var zoom = rig.Picker.Options.Single(o => o.Type == ChannelType.Zoom);
        var focus = rig.Picker.Options.Single(o => o.Type == ChannelType.Focus);

        Assert.True(zoom.Common);
        Assert.False(zoom.Partial); // both fixtures have Zoom
        Assert.False(focus.Common);
        Assert.True(focus.Partial); // only withFocus has Focus - must not silently imply both
    }

    // 9: family filtering works - switching the armed family changes the option set.
    [Fact]
    public void FamilyFiltering_ChangesAvailableOptions()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);

        rig.Surface.PressToken(CommandTokenKind.Position);
        var positionTypes = rig.Picker.Options.Select(o => o.Type).ToList();

        rig.Surface.PressToken(CommandTokenKind.Beam); // switch directly, no CLEAR needed
        var beamTypes = rig.Picker.Options.Select(o => o.Type).ToList();

        Assert.Equal(new[] { ChannelType.Pan, ChannelType.Tilt }, positionTypes);
        Assert.Equal(new[] { ChannelType.Zoom, ChannelType.Focus }.OrderBy(t => t), beamTypes.OrderBy(t => t));
        Assert.NotEqual(positionTypes, beamTypes);
    }

    // Explicit regression test: POSITION -> BEAM with no CLEAR in between must REPLACE the armed
    // family, never stack two family tokens. BEAM ends up as the sole armed family and Beam's own
    // parameters (Zoom/Focus) become visible - the same behavior whether the family keys are
    // pressed via touch/soft key or (once mapped) any other Command Surface entry point, since
    // both go through this one PressToken path.
    [Fact]
    public void SwitchingFamily_WithoutClear_ReplacesArmedFamily_NeverStacks()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);

        rig.Surface.PressToken(CommandTokenKind.Position);
        Assert.Equal(AttributeClass.Position, rig.Picker.ArmedFamily);

        rig.Surface.PressToken(CommandTokenKind.Beam); // no CLEAR

        Assert.Single(rig.Surface.Current.Tokens); // BEAM replaced POSITION, never [Position, Beam]
        Assert.Equal(CommandTokenKind.Beam, rig.Surface.Current.Tokens[0].Kind);
        Assert.Equal(AttributeClass.Beam, rig.Picker.ArmedFamily);
        Assert.Null(rig.Surface.DispatchError); // never the "unexpected token" error a blind append would produce

        var types = rig.Picker.Options.Select(o => o.Type).ToList();
        Assert.Contains(ChannelType.Zoom, types);
        Assert.Contains(ChannelType.Focus, types);
        Assert.DoesNotContain(ChannelType.Pan, types); // Position's own parameters are gone, not merged in
    }

    // The replace must NOT fire once the family token is part of a real, further-built command -
    // e.g. mid Preset-recall composition ("POSITION PRESET") - pressing another family key there
    // is simply an invalid next token, exactly as before this fix (no blind replace inside a
    // real command).
    [Fact]
    public void SwitchingFamily_DoesNotReplace_WhenFamilyTokenIsPartOfARealCommand()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);

        rig.Surface.PressToken(CommandTokenKind.Position);
        rig.Surface.PressToken(CommandTokenKind.Preset); // now mid a real command: [Position, Preset]

        rig.Surface.PressToken(CommandTokenKind.Beam); // must NOT silently replace/discard the in-progress Preset recall

        // An honest rejection, same as before this fix - CommandLine.razor's own ErrorText reads
        // DispatchError ?? Current.Error, the same fallback checked here. The invalid token is
        // preserved (not silently discarded) so the operator can correct via Backspace, same
        // "preserve what was entered" philosophy CommandComposition already documents elsewhere.
        Assert.NotNull(rig.Surface.DispatchError ?? rig.Surface.Current.Error);
        Assert.Equal(3, rig.Surface.Current.Tokens.Count); // [Position, Preset, Beam] - not silently replaced to just [Beam]
    }

    // 10: empty family state is handled honestly - Shape has no ChannelType member at all today,
    // so any selection always resolves zero Shape parameters; the picker must say so, not invent one.
    [Fact]
    public void EmptyFamily_IsReportedHonestly_NeverFakesADefault()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);
        rig.Surface.PressToken(CommandTokenKind.Shape);

        Assert.Empty(rig.Picker.Options);
        Assert.False(rig.Picker.IsVisible);
        Assert.True(rig.Picker.IsEmptyForArmedFamily);
    }

    // 11: fixture selection change refreshes available parameters - a purely computed property,
    // never a stale cache.
    [Fact]
    public void SelectionChange_RefreshesAvailableParameters()
    {
        var rig = BuildRig();
        var withFocus = Patch(rig, MovingHeadFull(), 1, address: 1);
        var withoutFocus = Patch(rig, MovingHeadNoFocus(), 2, address: 20);

        rig.Context.Selection.Add(withFocus);
        rig.Surface.PressToken(CommandTokenKind.Beam);
        Assert.Contains(ChannelType.Focus, rig.Picker.Options.Select(o => o.Type));

        rig.Context.Selection.Clear();
        rig.Context.Selection.Add(withoutFocus);
        // Family is still armed (Current.Tokens unaffected by Selection mutation) - only the
        // resolved Options should change.
        Assert.DoesNotContain(ChannelType.Focus, rig.Picker.Options.Select(o => o.Type));
        Assert.Contains(ChannelType.Zoom, rig.Picker.Options.Select(o => o.Type));
    }

    // 12: the picker reads the SAME shared ConsoleContext.Selection - not a private copy.
    [Fact]
    public void Picker_UsesSharedSelectionState_NotAPrivateCopy()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Surface.PressToken(CommandTokenKind.Position);

        Assert.Empty(rig.Picker.Options); // nothing selected yet

        rig.Context.Selection.Add(fixture); // mutate the SHARED Selection directly, not via the picker

        Assert.Contains(ChannelType.Pan, rig.Picker.Options.Select(o => o.Type));
    }

    // 13: no UI-only duplicate parameter state is introduced - ArmedFamily always matches exactly
    // what the Command Surface's own composition already shows, never a second tracked value.
    [Fact]
    public void ArmedFamily_AlwaysMatchesCommandSurfaceCompositionState_NoParallelState()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);

        Assert.Null(rig.Picker.ArmedFamily); // idle line

        rig.Surface.PressToken(CommandTokenKind.Position);
        Assert.Equal(AttributeClass.Position, rig.Picker.ArmedFamily);
        Assert.Contains(CommandTokenKind.Release, rig.Surface.Current.ExpectedNext); // same state the grammar itself reports

        rig.Picker.SelectParameter(ChannelType.Pan); // consumes the family token
        Assert.Null(rig.Picker.ArmedFamily); // no longer a lone family token - state derived, not stale
    }

    // 14: existing keyboard/command grammar behavior remains unchanged - family-level RELEASE
    // (without ever touching the picker) still resolves exactly as before this slice.
    [Fact]
    public void ExistingFamilyLevelRelease_StillWorksUnchanged()
    {
        var rig = BuildRig();
        var fixture = Patch(rig, MovingHeadFull(), 1);
        rig.Context.Selection.Add(fixture);
        rig.Context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!), 100);
        rig.Context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Tilt)!), 200);

        rig.Surface.PressToken(CommandTokenKind.Position);
        rig.Surface.PressToken(CommandTokenKind.Release); // family RELEASE, never touching the picker

        Assert.False(rig.Context.Programmer.HasStoredValue(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!), out _));
        Assert.False(rig.Context.Programmer.HasStoredValue(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Tilt)!), out _));
    }
}

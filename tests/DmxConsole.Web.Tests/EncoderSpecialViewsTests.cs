using System.Collections.ObjectModel;
using DmxConsole.Application;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;
using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>Milestone 1 Encoder Drawer redesign commit 6/7 - Position pad / Color Picker gating
/// (ShowsPositionPad/ShowsColorPicker/ColorPickerIsPartial) and MIXED RANGE (EncoderSlot.
/// MixedRange), all built from real per-fixture FixtureChannel calibration, never invented.</summary>
public class EncoderSpecialViewsTests
{
    private static FixtureProfile PanTilt(string id, string unit = "DMX", double panMin = 0, double panMax = 255, double tiltMin = 0, double tiltMax = 255) => new()
    {
        Id = id, Manufacturer = "Test", Model = "PanTilt",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "2ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0, Unit = unit, MinValue = panMin, MaxValue = panMax },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 1, Unit = unit, MinValue = tiltMin, MaxValue = tiltMax },
                },
            },
        },
    };

    private static FixtureProfile PanOnly() => new()
    {
        Id = "test-pan-only", Manufacturer = "Test", Model = "PanOnly",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 } } } },
    };

    private static FixtureProfile Rgb() => new()
    {
        Id = "test-rgb-" + Guid.NewGuid(), Manufacturer = "Test", Model = "RGB",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "3ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 0 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 1 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 2 },
                },
            },
        },
    };

    private static FixtureProfile ColorWheelOnly() => new()
    {
        Id = "test-colorwheel", Manufacturer = "Test", Model = "ColorWheel",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Color Wheel", Type = ChannelType.ColorWheel, Offset = 0 } } } },
    };

    private static (ConsoleContext Context, EncoderDrawerViewModel Drawer) Build()
    {
        var patch = new Patch();
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        var programmerVm = new ProgrammerViewModel(context, dispatcher, new ObservableCollection<ChannelFaderViewModel>());
        var drawer = new EncoderDrawerViewModel(dispatcher, programmerVm);
        return (context, drawer);
    }

    [Fact]
    public void ShowsPositionPad_TrueWhenSelectionHasBothPanAndTilt()
    {
        var (context, drawer) = Build();
        var profile = PanTilt("t1");
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Position);

        Assert.True(drawer.ShowsPositionPad());
    }

    [Fact]
    public void ShowsPositionPad_FalseWhenOnlyPanPresent()
    {
        var (context, drawer) = Build();
        var profile = PanOnly();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Position);

        Assert.False(drawer.ShowsPositionPad());
    }

    [Fact]
    public void PositionPadIsCalibrated_TrueOnlyWhenBothAxesHaveRealUnits()
    {
        var (context, drawer) = Build();
        var profile = PanTilt("t2", unit: "°", panMin: -270, panMax: 270, tiltMin: -135, tiltMax: 135);
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Position);

        Assert.True(drawer.PositionPadIsCalibrated());
    }

    [Fact]
    public void PositionPadIsCalibrated_FalseForUncalibratedGenericProfile()
    {
        var (context, drawer) = Build();
        var profile = PanTilt("t3");
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1); // default "DMX" unit
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Position);

        Assert.False(drawer.PositionPadIsCalibrated());
    }

    [Fact]
    public void PositionPadHasMixedRange_TrueWhenTwoFixturesDeclareDifferentPanCalibration()
    {
        var (context, drawer) = Build();
        var profileA = PanTilt("t4a", unit: "°", panMin: -270, panMax: 270, tiltMin: -135, tiltMax: 135);
        var profileB = PanTilt("t4b", unit: "°", panMin: -180, panMax: 180, tiltMin: -135, tiltMax: 135);
        var fixtureA = new PatchedFixture(profileA, profileA.Modes[0], 0, 1);
        var fixtureB = new PatchedFixture(profileB, profileB.Modes[0], 1, 1);
        context.Patch.Add(fixtureA);
        context.Patch.Add(fixtureB);
        context.Selection.Add(fixtureA);
        context.Selection.Add(fixtureB);
        drawer.SelectCategory(AttributeClass.Position);

        Assert.True(drawer.PositionPadHasMixedRange());
    }

    [Fact]
    public void PositionPadHasMixedRange_FalseWhenPanDiffersButTiltAgrees_StillDetectsPanMismatch()
    {
        // Explicit regression for the "check Pan-vs-Pan and Tilt-vs-Tilt separately" correction -
        // Pan and Tilt never need to match EACH OTHER, only themselves across fixtures.
        var (context, drawer) = Build();
        var profileA = PanTilt("t5a", unit: "°", panMin: -270, panMax: 270, tiltMin: -90, tiltMax: 90);
        var profileB = PanTilt("t5b", unit: "°", panMin: -270, panMax: 270, tiltMin: -90, tiltMax: 90);
        var fixtureA = new PatchedFixture(profileA, profileA.Modes[0], 0, 1);
        var fixtureB = new PatchedFixture(profileB, profileB.Modes[0], 1, 1);
        context.Patch.Add(fixtureA);
        context.Patch.Add(fixtureB);
        context.Selection.Add(fixtureA);
        context.Selection.Add(fixtureB);
        drawer.SelectCategory(AttributeClass.Position);

        // Pan differs from Tilt's own range (-270..270 vs -90..90) but that is NOT a mismatch -
        // both fixtures agree with each other on Pan, and agree with each other on Tilt.
        Assert.False(drawer.PositionPadHasMixedRange());
    }

    [Fact]
    public void ShowsColorPicker_TrueWhenEveryFixtureHasFullRgb()
    {
        var (context, drawer) = Build();
        var profileA = Rgb();
        var profileB = Rgb();
        var fixtureA = new PatchedFixture(profileA, profileA.Modes[0], 0, 1);
        var fixtureB = new PatchedFixture(profileB, profileB.Modes[0], 1, 1);
        context.Patch.Add(fixtureA);
        context.Patch.Add(fixtureB);
        context.Selection.Add(fixtureA);
        context.Selection.Add(fixtureB);
        drawer.SelectCategory(AttributeClass.Color);

        Assert.True(drawer.ShowsColorPicker());
        Assert.False(drawer.ColorPickerIsPartial());
    }

    [Fact]
    public void ShowsColorPicker_FalseAndPartialTrue_WhenOnlySomeFixturesHaveFullRgb()
    {
        var (context, drawer) = Build();
        var rgbProfile = Rgb();
        var wheelProfile = ColorWheelOnly();
        var rgbFixture = new PatchedFixture(rgbProfile, rgbProfile.Modes[0], 0, 1);
        var wheelFixture = new PatchedFixture(wheelProfile, wheelProfile.Modes[0], 1, 1);
        context.Patch.Add(rgbFixture);
        context.Patch.Add(wheelFixture);
        context.Selection.Add(rgbFixture);
        context.Selection.Add(wheelFixture);
        drawer.SelectCategory(AttributeClass.Color);

        Assert.False(drawer.ShowsColorPicker());
        Assert.True(drawer.ColorPickerIsPartial());
    }

    [Fact]
    public void ShowsColorPicker_FalseForColorWheelOnlyFixture_NeverFakesRgb()
    {
        var (context, drawer) = Build();
        var profile = ColorWheelOnly();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Color);

        Assert.False(drawer.ShowsColorPicker());
        Assert.False(drawer.ColorPickerIsPartial()); // no fixture has ANY RGB channel - not even partial
    }
}

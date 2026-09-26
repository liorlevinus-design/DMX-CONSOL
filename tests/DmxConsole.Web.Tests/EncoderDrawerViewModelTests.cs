using System.Collections.ObjectModel;
using DmxConsole.Application;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;
using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>Work-plan Milestone 1 (2026-09-16) - the fixed Encoder Drawer's view model:
/// category availability for mixed selections, paging, category/page stickiness across
/// Open/Close, and the HOME/MIN/MAX contextual actions.</summary>
public class EncoderDrawerViewModelTests
{
    private static FixtureProfile MovingHead() => new()
    {
        Id = "test-moving-head",
        Manufacturer = "Test",
        Model = "MovingHead",
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
                    new FixtureChannel { Name = "Gobo", Type = ChannelType.Gobo, Offset = 4, DefaultValue = 0 },
                    new FixtureChannel { Name = "Shutter", Type = ChannelType.Shutter, Offset = 5, DefaultValue = 255 },
                },
            },
        },
    };

    /// <summary>Pan/PanFine + Tilt/TiltFine coarse/fine pairs, plus one non-paired parameter
    /// (Gobo) - used to prove PSEL-5 logical parameter folding in the Encoder Drawer's own
    /// enumeration (docs/COMMAND_SURFACE_KEY_SPEC.md §23.30).</summary>
    private static FixtureProfile CoarseFineMovingHead() => new()
    {
        Id = "test-coarse-fine-moving-head",
        Manufacturer = "Test",
        Model = "CoarseFineMovingHead",
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
                    new FixtureChannel { Name = "Tilt Fine", Type = ChannelType.TiltFine, Offset = 3 },
                    new FixtureChannel { Name = "Gobo", Type = ChannelType.Gobo, Offset = 4 },
                },
            },
        },
    };

    /// <summary>A moving head with Pan/Tilt but no fine channels at all - the "coarse only" case.</summary>
    private static FixtureProfile CoarseOnlyMovingHead(string id = "test-coarse-only-moving-head") => new()
    {
        Id = id,
        Manufacturer = "Test",
        Model = "CoarseOnlyMovingHead",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "2ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 1 },
                },
            },
        },
    };

    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer",
        Manufacturer = "Test",
        Model = "Dimmer",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "1ch",
                Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 50 } },
            },
        },
    };

    /// <summary>6 distinct color channels on one fixture, to exercise paging (>5 in a category).</summary>
    private static FixtureProfile SixColorFixture() => new()
    {
        Id = "test-six-color",
        Manufacturer = "Test",
        Model = "SixColor",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "6ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 0 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 1 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 2 },
                    new FixtureChannel { Name = "White", Type = ChannelType.ColorWhite, Offset = 3 },
                    new FixtureChannel { Name = "Amber", Type = ChannelType.ColorAmber, Offset = 4 },
                    new FixtureChannel { Name = "Uv", Type = ChannelType.ColorUv, Offset = 5 },
                },
            },
        },
    };

    private static FixtureProfile RgbFixture(string id = "test-rgb") => new()
    {
        Id = id,
        Manufacturer = "Test",
        Model = "RGB",
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

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, EncoderDrawerViewModel Drawer) Build()
    {
        var (context, dispatcher, undoRedo, drawer, _) = BuildWithSurface();
        return (context, dispatcher, undoRedo, drawer);
    }

    /// <summary>PSEL slice 3: the RELEASE + encoder-parameter shortcut needs the real
    /// CommandSurfaceViewModel (it owns ReleaseArmed/ReleaseParameterForCurrentSelection - see that
    /// type's own doc comments), so this variant wires EncoderDrawerViewModel to a real one instead
    /// of a null/stub, exactly as MainViewModel does in production. Uses the same
    /// CommandSurfaceViewModelTestSupport helper CommandSurfaceViewModelReleaseTests already uses,
    /// so this is not a second, parallel construction path.</summary>
    private static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, EncoderDrawerViewModel Drawer, CommandSurfaceViewModel Surface) BuildWithSurface()
    {
        var patch = new Patch();
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        var programmerVm = new ProgrammerViewModel(context, dispatcher, new ObservableCollection<ChannelFaderViewModel>());
        var surface = CommandSurfaceViewModelTestSupport.BuildCommandSurfaceViewModel(context, dispatcher, new DmxConsole.Web.EditorToolBar.EditorContextStack());
        var drawer = new EncoderDrawerViewModel(dispatcher, programmerVm, surface);
        return (context, dispatcher, undoRedo, drawer, surface);
    }

    [Fact]
    public void AvailableCategories_MixedSelection_ShowsCategoryIfAnySelectedFixtureHasIt()
    {
        var (context, _, _, drawer) = Build();
        var movingHead = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1);
        var dimmerOnly = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 10);
        context.Patch.Add(movingHead);
        context.Patch.Add(dimmerOnly);
        context.Selection.Add(movingHead);
        context.Selection.Add(dimmerOnly);

        var available = drawer.AvailableCategories();

        Assert.Contains(AttributeClass.Intensity, available); // both have it
        Assert.Contains(AttributeClass.Position, available);  // only moving head - still shown
        Assert.Contains(AttributeClass.Color, available);
        Assert.Contains(AttributeClass.Image, available);
        Assert.Contains(AttributeClass.Beam, available); // Shutter is a beam effect under the corrected mapping
        Assert.DoesNotContain(AttributeClass.Shape, available); // nothing on either fixture maps to Shape today
    }

    [Fact]
    public void AvailableCategories_DimmerOnlyFixture_ShowsOnlyIntensity()
    {
        var (context, _, _, drawer) = Build();
        var dimmerOnly = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(dimmerOnly);
        context.Selection.Add(dimmerOnly);

        Assert.Equal(new[] { AttributeClass.Intensity }, drawer.AvailableCategories());
    }

    [Fact]
    public void SlotsForCurrentPage_AlwaysReturnsExactlyFive_PaddedWithEmpty()
    {
        var (context, _, _, drawer) = Build();
        var dimmerOnly = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(dimmerOnly);
        context.Selection.Add(dimmerOnly);
        drawer.SelectCategory(AttributeClass.Intensity);

        var slots = drawer.SlotsForCurrentPage();

        Assert.Equal(5, slots.Count);
        Assert.Equal(ChannelType.Dimmer, slots[0].Type);
        Assert.All(slots.Skip(1), s => Assert.Null(s.Type));
    }

    [Fact]
    public void Paging_MoreThanFiveChannelsInCategory_SplitsAcrossPages()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(SixColorFixture(), SixColorFixture().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Color);

        Assert.Equal(2, drawer.PageCount());
        var page0 = drawer.SlotsForCurrentPage().Where(s => s.Type is not null).ToList();
        Assert.Equal(5, page0.Count);

        drawer.NextPage();
        var page1 = drawer.SlotsForCurrentPage().Where(s => s.Type is not null).ToList();
        Assert.Single(page1);

        drawer.NextPage(); // already at last page - no-op
        Assert.Equal(1, drawer.Page);

        drawer.PreviousPage();
        Assert.Equal(0, drawer.Page);
    }

    [Fact]
    public void OpenAndClose_NeverChangeActiveCategoryOrPage()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(SixColorFixture(), SixColorFixture().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Color);
        drawer.NextPage();

        drawer.Close();
        Assert.False(drawer.IsOpen);
        Assert.Equal(AttributeClass.Color, drawer.ActiveCategory);
        Assert.Equal(1, drawer.Page);

        drawer.Open();
        Assert.True(drawer.IsOpen);
        Assert.Equal(AttributeClass.Color, drawer.ActiveCategory);
        Assert.Equal(1, drawer.Page);
    }

    [Fact]
    public void RevalidateActiveCategory_WhenCategoryNoLongerAvailable_AutoSelectsFirstAvailable()
    {
        var (context, _, _, drawer) = Build();
        var movingHead = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1);
        context.Patch.Add(movingHead);
        context.Selection.Add(movingHead);
        drawer.SelectCategory(AttributeClass.Image); // Gobo - only on the moving head
        drawer.NextPage(); // irrelevant here, but confirms Page also resets on the invalidation branch

        context.Selection.Clear();
        var dimmerOnly = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 10);
        context.Patch.Add(dimmerOnly);
        context.Selection.Add(dimmerOnly);
        drawer.RevalidateActiveCategory();

        // Image no longer applies - resets, then auto-selects Intensity (the only category
        // available for the dimmer-only fixture), per the "auto-select first available category
        // when none is active" requirement.
        Assert.Equal(AttributeClass.Intensity, drawer.ActiveCategory);
        Assert.Equal(0, drawer.Page);
    }

    [Fact]
    public void RevalidateActiveCategory_NoFixturesSelected_LeavesCategoryNull()
    {
        var (_, _, _, drawer) = Build();

        drawer.RevalidateActiveCategory();

        Assert.Null(drawer.ActiveCategory); // nothing available to auto-select
    }

    [Fact]
    public void RevalidateActiveCategory_AutoSelectsFirstAvailableCategory_OnFirstSelection()
    {
        var (context, _, _, drawer) = Build();
        var movingHead = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1);
        context.Patch.Add(movingHead);

        Assert.Null(drawer.ActiveCategory); // nothing selected yet

        context.Selection.Add(movingHead);
        drawer.RevalidateActiveCategory();

        Assert.Equal(AttributeClass.Intensity, drawer.ActiveCategory); // first in Vector-bank order
    }

    [Fact]
    public void RevalidateActiveCategory_DoesNotOverride_AlreadyChosenValidCategory()
    {
        var (context, _, _, drawer) = Build();
        var movingHead = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1);
        context.Patch.Add(movingHead);
        context.Selection.Add(movingHead);
        drawer.SelectCategory(AttributeClass.Beam); // operator explicitly picked something other than the first (Shutter is a beam effect)

        drawer.RevalidateActiveCategory();

        Assert.Equal(AttributeClass.Beam, drawer.ActiveCategory); // untouched - still valid
    }

    [Fact]
    public void RevalidateActiveCategory_KeepsCategory_WhenStillAvailable()
    {
        var (context, _, _, drawer) = Build();
        var movingHead = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1);
        var dimmerOnly = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 10);
        context.Patch.Add(movingHead);
        context.Patch.Add(dimmerOnly);
        context.Selection.Add(movingHead);
        drawer.SelectCategory(AttributeClass.Intensity);

        context.Selection.Add(dimmerOnly); // selection changed, but Intensity still applies

        drawer.RevalidateActiveCategory();

        Assert.Equal(AttributeClass.Intensity, drawer.ActiveCategory);
    }

    [Fact]
    public void BuildSlot_Partial_TrueWhenOnlySomeSelectedFixturesSupportChannel()
    {
        var (context, _, _, drawer) = Build();
        var movingHead = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1); // has Gobo (Image)
        var dimmerOnly = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 10);       // no Gobo
        context.Patch.Add(movingHead);
        context.Patch.Add(dimmerOnly);
        context.Selection.Add(movingHead);
        context.Selection.Add(dimmerOnly);
        drawer.SelectCategory(AttributeClass.Image);

        var slot = drawer.SlotsForCurrentPage().Single(s => s.Type == ChannelType.Gobo);

        Assert.True(slot.Partial);
    }

    [Fact]
    public void BuildSlot_NotPartial_WhenAllSelectedFixturesSupportChannel()
    {
        var (context, _, _, drawer) = Build();
        var fixtureA = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        var fixtureB = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 1, 1);
        context.Patch.Add(fixtureA);
        context.Patch.Add(fixtureB);
        context.Selection.Add(fixtureA);
        context.Selection.Add(fixtureB);
        drawer.SelectCategory(AttributeClass.Intensity);

        var slot = drawer.SlotsForCurrentPage().Single(s => s.Type == ChannelType.Dimmer);

        Assert.False(slot.Partial);
    }

    [Fact]
    public void BuildSlot_UsesFixtureChannelsCalibratedUnitAndRange()
    {
        var (context, _, _, drawer) = Build();
        var profile = new FixtureProfile
        {
            Id = "test-calibrated-dimmer", Manufacturer = "Test", Model = "CalibratedDimmer",
            Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, Unit = "%", MinValue = 0, MaxValue = 100 } } } },
        };
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 255);
        ((DmxOutputEngine)context.EffectiveOutput).AddLayer(context.Programmer);
        ((DmxOutputEngine)context.EffectiveOutput).Tick(); // BuildSlot reads live merged output, not Programmer directly
        drawer.SelectCategory(AttributeClass.Intensity);

        var slot = drawer.SlotsForCurrentPage().Single(s => s.Type == ChannelType.Dimmer);

        Assert.Equal("%", slot.Unit);
        Assert.Equal(100.0, slot.DisplayValue);
    }

    [Fact]
    public void GroupSelection_PopulatesEncodersForGroupMembers()
    {
        var (context, _, _, drawer) = Build();
        var movingHead = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1);
        context.Patch.Add(movingHead);
        var group = new FixtureGroup("Movers", new[] { movingHead });

        context.Selection.AddGroup(group);
        drawer.RevalidateActiveCategory();

        Assert.Equal(AttributeClass.Intensity, drawer.ActiveCategory); // auto-selected
        Assert.Contains(AttributeClass.Position, drawer.AvailableCategories());
        Assert.Contains(AttributeClass.Image, drawer.AvailableCategories());
    }

    [Fact]
    public void SetValue_SupportsUndoAndRedo()
    {
        var (context, _, undoRedo, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 10);

        drawer.SetValue(ChannelType.Dimmer, 200);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var afterSet));
        Assert.Equal(200, afterSet);

        var undone = undoRedo.Undo();
        Assert.True(undone.Performed);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var afterUndo));
        Assert.Equal(10, afterUndo);

        undoRedo.Redo();
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var afterRedo));
        Assert.Equal(200, afterRedo);
    }

    [Fact]
    public void MinMax_SupportUndo()
    {
        var (context, _, undoRedo, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 77);

        drawer.Max(ChannelType.Dimmer);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var afterMax));
        Assert.Equal(255, afterMax);

        var undone = undoRedo.Undo();
        Assert.True(undone.Performed);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var afterUndo));
        Assert.Equal(77, afterUndo);
    }

    [Fact]
    public void TrySetDisplayValue_ConvertsThroughFixtureChannelCalibration()
    {
        var (context, _, _, drawer) = Build();
        var profile = new FixtureProfile
        {
            Id = "test-calibrated-dimmer-2", Manufacturer = "Test", Model = "CalibratedDimmer2",
            Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, Unit = "%", MinValue = 0, MaxValue = 100 } } } },
        };
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);

        bool applied = drawer.TrySetDisplayValue(ChannelType.Dimmer, 100);

        Assert.True(applied);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var raw));
        Assert.Equal((byte)255, raw);
    }

    [Fact]
    public void TrySetDisplayValue_ClampsOutOfRangeInput()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1); // default "DMX" unit, 0..255

        context.Patch.Add(fixture);
        context.Selection.Add(fixture);

        drawer.TrySetDisplayValue(ChannelType.Dimmer, 9999);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var tooHigh));
        Assert.Equal((byte)255, tooHigh);

        drawer.TrySetDisplayValue(ChannelType.Dimmer, -50);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var tooLow));
        Assert.Equal((byte)0, tooLow);
    }

    [Fact]
    public void TrySetDisplayValue_ReturnsFalse_WhenNoSelectedFixtureSupportsChannel()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1); // no Pan channel
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);

        bool applied = drawer.TrySetDisplayValue(ChannelType.Pan, 50);

        Assert.False(applied);
    }

    [Fact]
    public void TrySetDisplayValue_SupportsUndo()
    {
        var (context, _, undoRedo, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 33);

        drawer.TrySetDisplayValue(ChannelType.Dimmer, 200);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var afterSet));
        Assert.Equal(200, afterSet);

        var undone = undoRedo.Undo();
        Assert.True(undone.Performed);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var afterUndo));
        Assert.Equal(33, afterUndo);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void TrySetDisplayValue_NonFiniteInput_ReturnsFalse_NeverThrows_NeverWrites(double invalid)
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);

        bool applied = drawer.TrySetDisplayValue(ChannelType.Dimmer, invalid);

        Assert.False(applied);
        Assert.False(context.Programmer.HasStoredValue(0, 0, out _));
    }

    [Fact]
    public void Min_SetsZero_Max_SetsMaxByte()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);

        drawer.Max(ChannelType.Dimmer);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var maxValue));
        Assert.Equal(255, maxValue);

        drawer.Min(ChannelType.Dimmer);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var minValue));
        Assert.Equal(0, minValue);
    }

    [Fact]
    public void Home_UsesEachFixturesOwnDefaultValue_AsOneUndoStep()
    {
        var (context, _, undoRedo, drawer) = Build();
        // Two dimmer fixtures with different profile defaults (50 vs a custom 90).
        var profileA = Dimmer1(); // default 50
        var profileB = new FixtureProfile
        {
            Id = "test-dimmer-90", Manufacturer = "Test", Model = "Dimmer90",
            Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 90 } } } },
        };
        var fixtureA = new PatchedFixture(profileA, profileA.Modes[0], 0, 1);
        var fixtureB = new PatchedFixture(profileB, profileB.Modes[0], 1, 1);
        context.Patch.Add(fixtureA);
        context.Patch.Add(fixtureB);
        context.Selection.Add(fixtureA);
        context.Selection.Add(fixtureB);
        context.Programmer.SetChannel(0, 0, 200);
        context.Programmer.SetChannel(1, 0, 200);

        drawer.Home(ChannelType.Dimmer);

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var valueA));
        Assert.Equal(50, valueA);
        Assert.True(context.Programmer.HasStoredValue(1, 0, out var valueB));
        Assert.Equal(90, valueB);

        var outcome = undoRedo.Undo(); // one Undo() reverts BOTH fixtures - it's one CompositeCommand
        Assert.True(outcome.Performed);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var restoredA));
        Assert.Equal(200, restoredA);
        Assert.True(context.Programmer.HasStoredValue(1, 0, out var restoredB));
        Assert.Equal(200, restoredB);
    }

    [Fact]
    public void ShowsPositionPad_RequiresRealPanAndTiltAndPositionCategory()
    {
        var (context, _, _, drawer) = Build();
        var profile = MovingHead();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);

        drawer.SelectCategory(AttributeClass.Intensity);
        Assert.False(drawer.ShowsPositionPad());

        drawer.SelectCategory(AttributeClass.Position);
        Assert.True(drawer.ShowsPositionPad());
    }

    [Fact]
    public void PositionPadHasMixedRange_ComparesEachAxisAcrossFixtures()
    {
        var (context, _, _, drawer) = Build();
        FixtureProfile PositionProfile(string id, double panMax) => new()
        {
            Id = id, Manufacturer = "Test", Model = id,
            Modes = new[]
            {
                new FixtureMode
                {
                    Name = "2ch",
                    Channels = new[]
                    {
                        new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0, Unit = "°", MinValue = -panMax, MaxValue = panMax },
                        new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 1, Unit = "°", MinValue = -135, MaxValue = 135 },
                    },
                },
            },
        };

        var profileA = PositionProfile("position-a", 270);
        var profileB = PositionProfile("position-b", 360);
        var fixtureA = new PatchedFixture(profileA, profileA.Modes[0], 0, 1);
        var fixtureB = new PatchedFixture(profileB, profileB.Modes[0], 1, 1);
        context.Patch.Add(fixtureA);
        context.Patch.Add(fixtureB);
        context.Selection.Add(fixtureA);
        context.Selection.Add(fixtureB);
        drawer.SelectCategory(AttributeClass.Position);

        Assert.True(drawer.SlotFor(ChannelType.Pan).MixedRange);
        Assert.False(drawer.SlotFor(ChannelType.Tilt).MixedRange);
        Assert.True(drawer.PositionPadHasMixedRange());
    }

    // ---------- PSEL-5 logical parameter folding (CLAUDE.md §16, KEY_SPEC §23.30) ----------

    /// <summary>Pan + Pan Fine must fold to ONE logical PAN slot, not two - the Encoder Drawer's
    /// own enumeration must never let the operator see a raw coarse/fine byte pair as separately
    /// selectable parameters.</summary>
    [Fact]
    public void ChannelTypesForCategory_PanAndPanFine_FoldToOneLogicalPan()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(CoarseFineMovingHead(), CoarseFineMovingHead().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Position);

        var types = drawer.SlotsForCurrentPage().Where(s => s.Type is not null).Select(s => s.Type!.Value).ToList();

        Assert.Contains(ChannelType.Pan, types);
        Assert.DoesNotContain(ChannelType.PanFine, types);
        Assert.Equal(1, types.Count(t => t == ChannelType.Pan));
    }

    /// <summary>Tilt + Tilt Fine must fold to ONE logical TILT slot, same as Pan above.</summary>
    [Fact]
    public void ChannelTypesForCategory_TiltAndTiltFine_FoldToOneLogicalTilt()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(CoarseFineMovingHead(), CoarseFineMovingHead().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Position);

        var types = drawer.SlotsForCurrentPage().Where(s => s.Type is not null).Select(s => s.Type!.Value).ToList();

        Assert.Contains(ChannelType.Tilt, types);
        Assert.DoesNotContain(ChannelType.TiltFine, types);
        Assert.Equal(1, types.Count(t => t == ChannelType.Tilt));
    }

    /// <summary>Folding must produce exactly Pan + Tilt (2 entries) for a fixture with Pan,
    /// PanFine, Tilt, TiltFine - never all four raw channel types.</summary>
    [Fact]
    public void ChannelTypesForCategory_CoarseFinePairs_ProduceExactlyTwoPositionEntries()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(CoarseFineMovingHead(), CoarseFineMovingHead().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Position);

        var types = drawer.SlotsForCurrentPage().Where(s => s.Type is not null).Select(s => s.Type!.Value).ToList();

        Assert.Equal(new[] { ChannelType.Pan, ChannelType.Tilt }, types.OrderBy(t => t));
    }

    /// <summary>A normal 8-bit, non-paired parameter (Gobo) on the same fixture as the coarse/fine
    /// pairs remains visible exactly once, unaffected by the Position-family folding above.</summary>
    [Fact]
    public void ChannelTypesForCategory_NonPairedParameter_RemainsVisibleExactlyOnce()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(CoarseFineMovingHead(), CoarseFineMovingHead().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Image);

        var types = drawer.SlotsForCurrentPage().Where(s => s.Type is not null).Select(s => s.Type!.Value).ToList();

        Assert.Equal(new[] { ChannelType.Gobo }, types);
    }

    /// <summary>A fixture with PAN but no PAN FINE still exposes PAN exactly once - no crash, no
    /// duplicate, no missing entry.</summary>
    [Fact]
    public void ChannelTypesForCategory_PanWithoutPanFine_ExposesPanExactlyOnce()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(CoarseOnlyMovingHead(), CoarseOnlyMovingHead().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Position);

        var types = drawer.SlotsForCurrentPage().Where(s => s.Type is not null).Select(s => s.Type!.Value).ToList();

        Assert.Equal(new[] { ChannelType.Pan, ChannelType.Tilt }, types.OrderBy(t => t));
    }

    /// <summary>Multiple selected fixtures - one with coarse/fine Pan/Tilt, one with coarse-only
    /// Pan/Tilt - must not create duplicate logical PAN/TILT entries.</summary>
    [Fact]
    public void ChannelTypesForCategory_MultipleFixtures_DoNotDuplicateSharedLogicalParameter()
    {
        var (context, _, _, drawer) = Build();
        var coarseFine = new PatchedFixture(CoarseFineMovingHead(), CoarseFineMovingHead().Modes[0], 0, 1);
        var coarseOnly = new PatchedFixture(CoarseOnlyMovingHead("test-coarse-only-2"), CoarseOnlyMovingHead("test-coarse-only-2").Modes[0], 1, 1);
        context.Patch.Add(coarseFine);
        context.Patch.Add(coarseOnly);
        context.Selection.Add(coarseFine);
        context.Selection.Add(coarseOnly);
        drawer.SelectCategory(AttributeClass.Position);

        var types = drawer.SlotsForCurrentPage().Where(s => s.Type is not null).Select(s => s.Type!.Value).ToList();

        Assert.Equal(new[] { ChannelType.Pan, ChannelType.Tilt }, types.OrderBy(t => t));
    }

    /// <summary>A parameter present on only some selected fixtures (Gobo, only on the moving head)
    /// must still be listed (Partial) - never lost by the folding logic.</summary>
    [Fact]
    public void ChannelTypesForCategory_ParameterOnlyOnSomeFixtures_IsNotLost()
    {
        var (context, _, _, drawer) = Build();
        var movingHead = new PatchedFixture(CoarseFineMovingHead(), CoarseFineMovingHead().Modes[0], 0, 1); // has Gobo
        var dimmerOnly = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 1, 10); // no Gobo
        context.Patch.Add(movingHead);
        context.Patch.Add(dimmerOnly);
        context.Selection.Add(movingHead);
        context.Selection.Add(dimmerOnly);
        drawer.SelectCategory(AttributeClass.Image);

        var slot = drawer.SlotsForCurrentPage().Single(s => s.Type == ChannelType.Gobo);

        Assert.True(slot.Partial);
    }

    [Fact]
    public void ShowsColorPicker_OnlyWhenEverySelectedFixtureHasFullRgb()
    {
        var (context, _, _, drawer) = Build();
        var rgbProfile = RgbFixture();
        var rgb = new PatchedFixture(rgbProfile, rgbProfile.Modes[0], 0, 1);
        context.Patch.Add(rgb);
        context.Selection.Add(rgb);
        drawer.SelectCategory(AttributeClass.Color);

        Assert.True(drawer.ShowsColorPicker());
        Assert.False(drawer.ColorPickerIsPartial());

        var dimmerProfile = Dimmer1();
        var dimmer = new PatchedFixture(dimmerProfile, dimmerProfile.Modes[0], 1, 1);
        context.Patch.Add(dimmer);
        context.Selection.Add(dimmer);

        Assert.False(drawer.ShowsColorPicker());
        Assert.True(drawer.ColorPickerIsPartial());
    }

    /// <summary>CLAUDE.md §16 (PSEL-4) - the shared Parameter Selection is a distinct, explicitly-
    /// written state. This slice does not wire the Encoder Drawer to it at all (Parameter Picker
    /// is the chosen operator-facing surface), so category/page changes here must have ZERO effect
    /// on ConsoleContext.ParameterSelection - trivially true by construction, verified anyway per
    /// the slice's explicit requirement.</summary>
    [Fact]
    public void CategoryAndPageChanges_HaveNoEffectOnSharedParameterSelection()
    {
        var (context, _, _, drawer) = Build();
        var movingHead = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1);
        context.Patch.Add(movingHead);
        context.Selection.Add(movingHead);
        context.ParameterSelection.Select(ChannelType.ColorRed);

        drawer.SelectCategory(AttributeClass.Position);
        drawer.SelectCategory(AttributeClass.Color);
        drawer.RevalidateActiveCategory();
        drawer.NextPage();

        Assert.Equal(new[] { ChannelType.ColorRed }, context.ParameterSelection.Items);
    }

    // ==================================================================================
    // PSEL slice 3: the Encoder Drawer IS the Parameter Selection UI - normal parameter-
    // label press toggles ConsoleContext.ParameterSelection; RELEASE-armed press releases
    // that one logical parameter, scoped to the current Fixture Selection, via the
    // canonical ReleaseParameterCommand. See CLAUDE.md §16 (PSEL-1..5) and the operator's
    // PSEL slice 3 task specification for the exact product rules being verified below.
    // ==================================================================================

    // ---------- Normal parameter selection (RELEASE not armed) ----------

    [Fact]
    public void PressParameterLabel_SelectsLogicalParameter_InSharedParameterSelection()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Color);

        drawer.PressParameterLabel(ChannelType.ColorRed);

        Assert.Equal(new[] { ChannelType.ColorRed }, context.ParameterSelection.Items);
        Assert.True(drawer.IsParameterSelected(ChannelType.ColorRed));
    }

    [Fact]
    public void PressParameterLabel_SecondDifferentParameter_AppendsPreservingOrder()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Color);

        drawer.PressParameterLabel(ChannelType.ColorRed);
        drawer.PressParameterLabel(ChannelType.ColorGreen);

        Assert.Equal(new[] { ChannelType.ColorRed, ChannelType.ColorGreen }, context.ParameterSelection.Items);
    }

    [Fact]
    public void PressParameterLabel_PressingSelectedParameterAgain_RemovesOnlyThatOne()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Color);

        drawer.PressParameterLabel(ChannelType.ColorRed);
        drawer.PressParameterLabel(ChannelType.ColorGreen);
        drawer.PressParameterLabel(ChannelType.ColorRed); // toggle RED back off

        Assert.Equal(new[] { ChannelType.ColorGreen }, context.ParameterSelection.Items);
        Assert.False(drawer.IsParameterSelected(ChannelType.ColorRed));
        Assert.True(drawer.IsParameterSelected(ChannelType.ColorGreen));
    }

    [Fact]
    public void PressParameterLabel_DuplicateSelectionIsImpossible()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Color);

        context.ParameterSelection.Select(ChannelType.ColorRed); // pre-selected via another surface
        drawer.PressParameterLabel(ChannelType.ColorGreen);

        // Directly re-selecting an already-present logical parameter (e.g. a second, independent
        // surface choosing the same one) must never create a duplicate entry - Select() itself
        // guards this (Slice 2); re-asserted here at the Encoder Drawer's own call path.
        context.ParameterSelection.Select(ChannelType.ColorRed);

        Assert.Equal(new[] { ChannelType.ColorRed, ChannelType.ColorGreen }, context.ParameterSelection.Items);
    }

    [Fact]
    public void PressParameterLabel_PanAndPanFine_FoldToOneLogicalPanInParameterSelection()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(CoarseFineMovingHead(), CoarseFineMovingHead().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Position);

        drawer.PressParameterLabel(ChannelType.Pan); // BuildSlot only ever offers the folded/coarse type

        Assert.Equal(new[] { ChannelType.Pan }, context.ParameterSelection.Items);
        Assert.True(context.ParameterSelection.Contains(ChannelType.PanFine)); // folds - same logical parameter
        Assert.True(drawer.IsParameterSelected(ChannelType.PanFine));
    }

    [Fact]
    public void PressParameterLabel_TiltAndTiltFine_FoldToOneLogicalTiltInParameterSelection()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(CoarseFineMovingHead(), CoarseFineMovingHead().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Position);

        drawer.PressParameterLabel(ChannelType.Tilt);

        Assert.Equal(new[] { ChannelType.Tilt }, context.ParameterSelection.Items);
        Assert.True(context.ParameterSelection.Contains(ChannelType.TiltFine));
        Assert.True(drawer.IsParameterSelected(ChannelType.TiltFine));
    }

    [Fact]
    public void IsParameterSelected_ReflectsSharedParameterSelection_MutatedDirectly()
    {
        var (context, _, _, drawer) = Build();

        Assert.False(drawer.IsParameterSelected(ChannelType.ColorRed));

        context.ParameterSelection.Select(ChannelType.ColorRed); // mutated by some other surface entirely
        Assert.True(drawer.IsParameterSelected(ChannelType.ColorRed));

        context.ParameterSelection.Remove(ChannelType.ColorRed);
        Assert.False(drawer.IsParameterSelected(ChannelType.ColorRed));
    }

    [Fact]
    public void PressParameterLabel_NeverAffectsEncoderValueEditing_Regression()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 77);
        drawer.SelectCategory(AttributeClass.Intensity);

        drawer.PressParameterLabel(ChannelType.Dimmer); // selects the parameter, must not touch the value

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var stillOriginal));
        Assert.Equal(77, stillOriginal);

        // and the reverse: rotating/editing the value must not implicitly select/deselect the
        // parameter - SetValue/Min/Max/Home are completely independent of ParameterSelection.
        // Dimmer was explicitly selected above by PressParameterLabel; SetValue must leave that
        // selection state exactly as-is (still selected), never toggle it off as a side effect.
        drawer.SetValue(ChannelType.Dimmer, 200);
        Assert.True(drawer.IsParameterSelected(ChannelType.Dimmer));
    }

    [Fact]
    public void PressParameterLabel_ReleaseNotArmed_NeverMutatesProgrammer()
    {
        var (context, _, _, drawer) = BuildWithSurfaceContext();
        var fixture = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 150); // Red
        drawer.SelectCategory(AttributeClass.Color);

        drawer.PressParameterLabel(ChannelType.ColorRed);

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var unchanged));
        Assert.Equal(150, unchanged);
    }

    // ---------- Family/availability ----------

    [Fact]
    public void ColorCategory_ExposesApplicableLogicalColorParameters_SelectableByLabel()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Color);

        var types = drawer.SlotsForCurrentPage().Where(s => s.Type is not null).Select(s => s.Type!.Value).ToList();

        Assert.Equal(new[] { ChannelType.ColorRed, ChannelType.ColorGreen, ChannelType.ColorBlue }, types.OrderBy(t => t));
        foreach (var type in types) drawer.PressParameterLabel(type);
        Assert.Equal(types.Count, context.ParameterSelection.Items.Count);
    }

    [Fact]
    public void PositionCategory_ExposesApplicableLogicalPositionParameters_SelectableByLabel()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Position);

        var types = drawer.SlotsForCurrentPage().Where(s => s.Type is not null).Select(s => s.Type!.Value).ToList();

        Assert.Equal(new[] { ChannelType.Pan, ChannelType.Tilt }, types.OrderBy(t => t));
        drawer.PressParameterLabel(ChannelType.Pan);
        Assert.True(drawer.IsParameterSelected(ChannelType.Pan));
    }

    [Fact]
    public void FixtureSelectionChange_RefreshesAvailableEncoderParameters()
    {
        var (context, _, _, drawer) = Build();
        var rgb = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1);
        var dimmer = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 1, 10);
        context.Patch.Add(rgb);
        context.Patch.Add(dimmer);
        context.Selection.Add(rgb);
        drawer.SelectCategory(AttributeClass.Color);
        Assert.NotEmpty(drawer.SlotsForCurrentPage().Where(s => s.Type is not null));

        context.Selection.Clear();
        context.Selection.Add(dimmer);
        drawer.RevalidateActiveCategory();

        Assert.Empty(drawer.SlotsForCurrentPage().Where(s => s.Type == ChannelType.ColorRed));
    }

    [Fact]
    public void FixtureSelectionChange_DoesNotClearParameterSelection()
    {
        var (context, _, _, drawer) = Build();
        var rgb = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1);
        var dimmer = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 1, 10);
        context.Patch.Add(rgb);
        context.Patch.Add(dimmer);
        context.Selection.Add(rgb);
        drawer.SelectCategory(AttributeClass.Color);
        drawer.PressParameterLabel(ChannelType.ColorRed);

        context.Selection.Clear();
        context.Selection.Add(dimmer); // ColorRed is no longer available on the new selection
        drawer.RevalidateActiveCategory();

        Assert.True(context.ParameterSelection.Contains(ChannelType.ColorRed)); // survives, per PSEL-4
    }

    [Fact]
    public void FixtureSelectionChange_DoesNotReorderParameterSelection()
    {
        var (context, _, _, drawer) = Build();
        var rgb = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1);
        context.Patch.Add(rgb);
        context.Selection.Add(rgb);
        drawer.SelectCategory(AttributeClass.Color);
        drawer.PressParameterLabel(ChannelType.ColorBlue);
        drawer.PressParameterLabel(ChannelType.ColorRed);

        context.Selection.Clear();
        context.Selection.Add(rgb);
        drawer.RevalidateActiveCategory();

        Assert.Equal(new[] { ChannelType.ColorBlue, ChannelType.ColorRed }, context.ParameterSelection.Items);
    }

    [Fact]
    public void SelectedParameterThatBecomesUnavailable_RemainsInSharedParameterSelection()
    {
        var (context, _, _, drawer) = Build();
        var movingHead = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1); // has Pan
        var dimmer = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 1, 10); // no Pan
        context.Patch.Add(movingHead);
        context.Patch.Add(dimmer);
        context.Selection.Add(movingHead);
        drawer.SelectCategory(AttributeClass.Position);
        drawer.PressParameterLabel(ChannelType.Pan);

        context.Selection.Clear();
        context.Selection.Add(dimmer);
        drawer.RevalidateActiveCategory(); // Position no longer applies at all

        Assert.True(context.ParameterSelection.Contains(ChannelType.Pan)); // still held, just not rendered
        Assert.DoesNotContain(AttributeClass.Position, drawer.AvailableCategories());
    }

    // ---------- CLEAR ----------

    [Fact]
    public void Clear_EmptiesParameterSelection()
    {
        var (context, dispatcher, _, drawer) = Build();
        var fixture = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Color);
        drawer.PressParameterLabel(ChannelType.ColorRed);

        dispatcher.DispatchAction(new ClearSelectionAction());

        Assert.True(context.ParameterSelection.IsEmpty);
    }

    [Fact]
    public void Clear_EncoderDrawerSelectedCheck_ReflectsClearImmediately()
    {
        var (context, dispatcher, _, drawer) = Build();
        var fixture = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        drawer.SelectCategory(AttributeClass.Color);
        drawer.PressParameterLabel(ChannelType.ColorRed);
        Assert.True(drawer.IsParameterSelected(ChannelType.ColorRed));

        dispatcher.DispatchAction(new ClearSelectionAction());

        Assert.False(drawer.IsParameterSelected(ChannelType.ColorRed));
    }

    // ---------- RELEASE - selection-scoped (Encoder Drawer shortcut) ----------

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, EncoderDrawerViewModel Drawer) BuildWithSurfaceContext() => Build();

    [Fact]
    public void ReleaseArmed_PressRed_ReleasesOnlySelectedFixtures_ForThatParameter()
    {
        var (context, _, undoRedo, drawer, surface) = BuildWithSurface();
        var f101 = new PatchedFixture(RgbFixture("f101"), RgbFixture("f101").Modes[0], 0, 1) { Number = 101 };
        var f105 = new PatchedFixture(RgbFixture("f105"), RgbFixture("f105").Modes[0], 1, 1) { Number = 105 };
        var f110 = new PatchedFixture(RgbFixture("f110"), RgbFixture("f110").Modes[0], 2, 1) { Number = 110 };
        context.Patch.Add(f101);
        context.Patch.Add(f105);
        context.Patch.Add(f110);

        int Red(PatchedFixture f) => f.AbsoluteIndex(f.FindChannel(ChannelType.ColorRed)!);
        int Blue(PatchedFixture f) => f.AbsoluteIndex(f.FindChannel(ChannelType.ColorBlue)!);

        context.Programmer.SetChannel(f101.UniverseId, Red(f101), 200);
        context.Programmer.SetChannel(f101.UniverseId, Blue(f101), 90);   // must survive - different parameter
        context.Programmer.SetChannel(f105.UniverseId, Red(f105), 210);
        context.Programmer.SetChannel(f110.UniverseId, Red(f110), 220);   // must survive - not in Fixture Selection

        context.Selection.Add(f101);
        context.Selection.Add(f105); // Fixture Selection = {101, 105}; 110 deliberately excluded

        surface.PressRelease(); // arm RELEASE
        Assert.True(surface.ReleaseArmed);

        drawer.PressParameterLabel(ChannelType.ColorRed); // the shortcut: RELEASE armed + parameter label press

        Assert.False(surface.ReleaseArmed); // disarmed after the shortcut fires, same as any other confirmation
        Assert.False(context.Programmer.HasStoredValue(f101.UniverseId, Red(f101), out _)); // 101 RED released
        Assert.False(context.Programmer.HasStoredValue(f105.UniverseId, Red(f105), out _)); // 105 RED released
        Assert.True(context.Programmer.HasStoredValue(f101.UniverseId, Blue(f101), out var blueAfter)); // untouched
        Assert.Equal(90, blueAfter);
        Assert.True(context.Programmer.HasStoredValue(f110.UniverseId, Red(f110), out var f110After)); // untouched - not selected
        Assert.Equal(220, f110After);

        // Routed through the canonical dispatcher/Undo stack (item 28) - Undo restores both released fixtures.
        var undone = undoRedo.Undo();
        Assert.True(undone.Performed);
        Assert.True(context.Programmer.HasStoredValue(f101.UniverseId, Red(f101), out var restored101));
        Assert.Equal(200, restored101);
        Assert.True(context.Programmer.HasStoredValue(f105.UniverseId, Red(f105), out var restored105));
        Assert.Equal(210, restored105);
    }

    [Fact]
    public void ReleaseArmed_PressPan_ReleasesPanOnlyForFixturesInCurrentSelection()
    {
        var (context, _, _, drawer, surface) = BuildWithSurface();
        var moverA = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1) { Number = 1 };
        var moverB = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 1, 1) { Number = 2 };
        context.Patch.Add(moverA);
        context.Patch.Add(moverB);

        int Pan(PatchedFixture f) => f.AbsoluteIndex(f.FindChannel(ChannelType.Pan)!);
        int Dimmer(PatchedFixture f) => f.AbsoluteIndex(f.FindChannel(ChannelType.Dimmer)!);

        context.Programmer.SetChannel(moverA.UniverseId, Pan(moverA), 60);
        context.Programmer.SetChannel(moverA.UniverseId, Dimmer(moverA), 200);
        context.Programmer.SetChannel(moverB.UniverseId, Pan(moverB), 70); // not selected

        context.Selection.Add(moverA);

        surface.PressRelease();
        drawer.PressParameterLabel(ChannelType.Pan);

        Assert.False(context.Programmer.HasStoredValue(moverA.UniverseId, Pan(moverA), out _));
        Assert.True(context.Programmer.HasStoredValue(moverA.UniverseId, Dimmer(moverA), out var dimmerAfter)); // unrelated parameter untouched
        Assert.Equal(200, dimmerAfter);
        Assert.True(context.Programmer.HasStoredValue(moverB.UniverseId, Pan(moverB), out var moverBAfter)); // unrelated fixture untouched
        Assert.Equal(70, moverBAfter);
    }

    [Fact]
    public void ReleaseArmed_PressParameter_RevealsUnderlyingPlaybackValueAgain()
    {
        var (context, _, _, drawer, surface) = BuildWithSurface();
        var fixture = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1) { Number = 1 };
        context.Patch.Add(fixture);
        int red = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.ColorRed)!);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(fixture.UniverseId, red, 128); // recorded into the Cue below

        var cueList = new CueList();
        var engine = (DmxOutputEngine)context.EffectiveOutput;
        cueList.RecordCue(context.Patch, context.Programmer, context.Selection, engine, "Cue 1", 1,
            new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));
        var executor = context.Executors.Add(1);
        executor.Assign(cueList);
        engine.AddLayer(context.Programmer);
        engine.AddLayer(executor);
        cueList.Go();
        engine.Tick();
        Assert.Equal(OwnerKind.Programmer, engine.GetOwner(fixture.UniverseId, red)!.Kind);

        surface.PressRelease();
        drawer.PressParameterLabel(ChannelType.ColorRed);
        engine.Tick();

        Assert.False(context.Programmer.HasStoredValue(fixture.UniverseId, red, out _));
        Assert.Equal((byte)128, engine.GetEffectiveValue(fixture.UniverseId, red)); // the Cue's own value reappears
        Assert.Equal(OwnerKind.Executor, engine.GetOwner(fixture.UniverseId, red)!.Kind);
    }

    [Fact]
    public void ExistingGlobalReleaseRelease_RemainsUnchanged_Regression()
    {
        var (context, _, _, _, surface) = BuildWithSurface();
        var a = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1) { Number = 1 };
        context.Patch.Add(a);
        context.Programmer.SetChannel(a.UniverseId, 0, 200); // not selected at all

        surface.PressRelease();
        surface.PressRelease(); // second bare RELEASE - global clear, independent of Selection/Encoder Drawer

        Assert.False(context.Programmer.HasStoredValue(a.UniverseId, 0, out _));
    }

    [Fact]
    public void ExistingFamilyRelease_RemainsUnchanged_Regression()
    {
        var (context, _, _, _, surface) = BuildWithSurface();
        var fixture = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1) { Number = 1 };
        context.Patch.Add(fixture);
        int pan = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!);
        int dimmer = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Dimmer)!);
        context.Programmer.SetChannel(fixture.UniverseId, pan, 60);
        context.Programmer.SetChannel(fixture.UniverseId, dimmer, 200);
        context.Selection.Add(fixture);

        surface.PressRelease();
        surface.ToggleReleaseFamily(AttributeClass.Position);
        surface.PressToken(DmxConsole.Application.CommandSurface.CommandTokenKind.Enter);

        Assert.False(context.Programmer.HasStoredValue(fixture.UniverseId, pan, out _));
        Assert.True(context.Programmer.HasStoredValue(fixture.UniverseId, dimmer, out var dimmerAfter));
        Assert.Equal(200, dimmerAfter);
    }

    [Fact]
    public void ExistingParameterPickerRelease_RemainsUnchanged_Regression()
    {
        var (context, dispatcher, _, _, surface) = BuildWithSurface();
        var fixture = new PatchedFixture(MovingHead(), MovingHead().Modes[0], 0, 1) { Number = 1 };
        context.Patch.Add(fixture);
        int pan = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!);
        context.Programmer.SetChannel(fixture.UniverseId, pan, 60);
        context.Selection.Add(fixture);
        var picker = new ParameterPickerViewModel(context, surface);

        surface.PressToken(DmxConsole.Application.CommandSurface.CommandTokenKind.Position); // arm the family
        Assert.Equal(AttributeClass.Position, picker.ArmedFamily);

        picker.SelectParameter(ChannelType.Pan); // "POSITION, PAN" - pushes the Parameter token
        surface.PressToken(DmxConsole.Application.CommandSurface.CommandTokenKind.Release);
        surface.PressToken(DmxConsole.Application.CommandSurface.CommandTokenKind.Enter);

        Assert.False(context.Programmer.HasStoredValue(fixture.UniverseId, pan, out _));
        Assert.Equal(new[] { ChannelType.Pan }, context.ParameterSelection.Items); // picker's own recording still works too
    }

    [Fact]
    public void ReleaseNotArmed_PressParameterLabel_NeverDispatchesRelease_StructuralCheck()
    {
        // EncoderDrawerViewModel.PressParameterLabel must only ever construct/dispatch the
        // canonical ReleaseParameterCommand when RELEASE is armed (CommandSurfaceViewModel owns
        // that state) - never a second, Encoder-Drawer-local Programmer mutation. Verified here by
        // confirming an ordinary (non-armed) press leaves the Programmer, and ReleaseArmed itself,
        // completely untouched - the same guarantee item 27 requires, exercised via the object's
        // public API since the drawer holds no Programmer-mutating field.
        var (context, _, _, drawer, surface) = BuildWithSurface();
        var fixture = new PatchedFixture(RgbFixture(), RgbFixture().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 128);
        drawer.SelectCategory(AttributeClass.Color);

        drawer.PressParameterLabel(ChannelType.ColorRed);

        Assert.False(surface.ReleaseArmed);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var unchanged));
        Assert.Equal(128, unchanged);
    }
}

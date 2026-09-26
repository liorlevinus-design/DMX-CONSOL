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
        var patch = new Patch();
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        var programmerVm = new ProgrammerViewModel(context, dispatcher, new ObservableCollection<ChannelFaderViewModel>());
        var drawer = new EncoderDrawerViewModel(dispatcher, programmerVm);
        return (context, dispatcher, undoRedo, drawer);
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
}

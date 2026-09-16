using DmxConsole.Application.Live;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Application.Tests;

/// <summary>OPERATOR_UX_ROADMAP.md §4 - the fixture-level LIVE read model, grouped by the same
/// Vector-bank EncoderCategory the Encoder Drawer already uses (Intensity/Position/Color/Beam/
/// Image/Shape), built entirely from LiveChannelState so Fixtures LIVE can never disagree with
/// Channels LIVE about what's pending/live/used/mixed.</summary>
public class FixtureLiveStateTests
{
    private static readonly IReadOnlySet<(int, int)> NoneUsed = new HashSet<(int, int)>();

    /// <summary>One channel per family that has a documented EncoderCategory, so a single fixture
    /// exercises all six: Dimmer(Intensity), Pan+Tilt(Position), ColorRed/Green/Blue(Color),
    /// Focus(Beam), Gobo(Image), Shutter(Shape). Also one Macro channel with NO category, to
    /// prove uncategorized channels are simply excluded, not force-fit somewhere.</summary>
    private static FixtureProfile AllFamiliesProfile() => new()
    {
        Id = "test-all-families", Manufacturer = "Test", Model = "AllFamilies",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "9ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 },
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 1 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2 },
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 3 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 4 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 5 },
                    new FixtureChannel { Name = "Focus", Type = ChannelType.Focus, Offset = 6 },
                    new FixtureChannel { Name = "Gobo", Type = ChannelType.Gobo, Offset = 7 },
                    new FixtureChannel { Name = "Shutter", Type = ChannelType.Shutter, Offset = 8 },
                    new FixtureChannel { Name = "Macro", Type = ChannelType.Macro, Offset = 9 },
                },
            },
        },
    };

    private static (ConsoleContext Context, PatchedFixture Fixture) BuildRig()
    {
        var patch = new Patch();
        var profile = AllFamiliesProfile();
        var fixture = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        patch.Add(fixture);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new Core.Presets.PresetLibrary(), new ExecutorBank());
        return (context, fixture);
    }

    [Fact]
    public void For_GroupsEveryChannelIntoItsEncoderCategory_ExcludesUncategorized()
    {
        var (context, fixture) = BuildRig();
        ((DmxOutputEngine)context.EffectiveOutput).Tick();

        var state = FixtureLiveState.For(context, fixture, isSelected: false, NoneUsed);

        Assert.NotNull(state.Family(EncoderCategory.Intensity));
        Assert.NotNull(state.Family(EncoderCategory.Position));
        Assert.NotNull(state.Family(EncoderCategory.Color));
        Assert.NotNull(state.Family(EncoderCategory.Beam));
        Assert.NotNull(state.Family(EncoderCategory.Image));
        Assert.NotNull(state.Family(EncoderCategory.Shape));

        Assert.Single(state.Family(EncoderCategory.Intensity)!.Channels);
        Assert.Equal(2, state.Family(EncoderCategory.Position)!.Channels.Count); // Pan + Tilt
        Assert.Equal(3, state.Family(EncoderCategory.Color)!.Channels.Count); // R + G + B

        // The Macro channel has no EncoderCategory - it must not silently land in any family.
        Assert.DoesNotContain(state.Families.Values, f => f.Channels.Any(c => c.Type == ChannelType.Macro));
    }

    [Fact]
    public void Family_NotPresentOnFixture_ReturnsNull_NeverAnEmptyFakeFamily()
    {
        var patch = new Patch();
        var profile = new FixtureProfile
        {
            Id = "test-dimmer-only", Manufacturer = "Test", Model = "DimmerOnly",
            Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
        };
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        patch.Add(fixture);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new Core.Presets.PresetLibrary(), new ExecutorBank());
        engine.Tick();

        var state = FixtureLiveState.For(context, fixture, isSelected: false, NoneUsed);

        Assert.NotNull(state.Family(EncoderCategory.Intensity));
        Assert.Null(state.Family(EncoderCategory.Position));
        Assert.Null(state.Family(EncoderCategory.Color));
    }

    [Fact]
    public void MixedProvenance_TrueWhenTwoChannelsInSameFamilyHaveDifferentOwners()
    {
        var (context, fixture) = BuildRig();
        var engine = (DmxOutputEngine)context.EffectiveOutput;

        // Pan driven by a Cue (via an Executor), Tilt grabbed directly in the Programmer -
        // genuinely different owners for the same Position family.
        context.Programmer.SetChannel(0, 2, 100); // Tilt (offset 2)
        var cueList = new CueList();
        var selection = new FixtureSelection();
        var record = cueList.RecordCue(context.Patch, new Programmer(), selection, engine, "Cue 1", 1,
            new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));
        context.Executors.Add(1).Assign(cueList);
        cueList.Go();
        engine.AddLayer(context.Programmer);
        engine.AddLayer(context.Executors.Executors[0]);
        engine.Tick();

        var state = FixtureLiveState.For(context, fixture, isSelected: false, NoneUsed);
        var position = state.Family(EncoderCategory.Position)!;

        Assert.True(position.IsMixedProvenance);
        Assert.Null(position.CommonOwner);
        Assert.True(position.IsLiveOnStage); // still true - SOMETHING is live, just not one shared source
    }

    [Fact]
    public void CommonOwner_SameSourceForEveryChannel_ReturnsThatOwner_NotMixed()
    {
        var (context, fixture) = BuildRig();
        var engine = (DmxOutputEngine)context.EffectiveOutput;
        context.Programmer.SetChannel(0, 1, 50); // Pan
        context.Programmer.SetChannel(0, 2, 60); // Tilt
        engine.AddLayer(context.Programmer);
        engine.Tick();

        var state = FixtureLiveState.For(context, fixture, isSelected: false, NoneUsed);
        var position = state.Family(EncoderCategory.Position)!;

        Assert.False(position.IsMixedProvenance);
        Assert.NotNull(position.CommonOwner);
        Assert.Equal(OwnerKind.Programmer, position.CommonOwner!.Kind);
    }

    [Fact]
    public void EditorOnlyFilter_MatchesFixture_WhenAnyFamilyHasAPendingValue()
    {
        var (context, fixture) = BuildRig();
        context.Programmer.SetChannel(0, 6, 90); // Focus (Beam family)

        var state = FixtureLiveState.For(context, fixture, isSelected: false, NoneUsed);

        Assert.True(state.Family(EncoderCategory.Beam)!.HasEditorValue);
        Assert.True(state.HasEditorValue);
        Assert.True(state.Matches(LiveFilter.EditorOnly));
        Assert.False(state.Family(EncoderCategory.Color)!.HasEditorValue);
    }

    [Fact]
    public void UsedInShowFilter_MatchesFixture_WhenAnyFamilyChannelIsReferenced()
    {
        var (context, fixture) = BuildRig();
        var used = new HashSet<(int, int)> { (0, 7) }; // Gobo (Image family)

        var state = FixtureLiveState.For(context, fixture, isSelected: false, used);

        Assert.True(state.Family(EncoderCategory.Image)!.IsUsedInShow);
        Assert.True(state.IsUsedInShow);
        Assert.True(state.Matches(LiveFilter.UsedInShow));
        Assert.False(state.Family(EncoderCategory.Shape)!.IsUsedInShow);
    }

    [Fact]
    public void SelectedFilter_ReflectsThePassedInFlag()
    {
        var (context, fixture) = BuildRig();

        var selected = FixtureLiveState.For(context, fixture, isSelected: true, NoneUsed);
        var unselected = FixtureLiveState.For(context, fixture, isSelected: false, NoneUsed);

        Assert.True(selected.Matches(LiveFilter.Selected));
        Assert.False(unselected.Matches(LiveFilter.Selected));
    }
}

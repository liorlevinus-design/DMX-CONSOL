using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Core.Tests;

/// <summary>H1.6 Slice 2 - Vector's five STORE OPTIONS meanings (docs/VECTOR_EDITOR_TOOLBAR_REFERENCE.md
/// §3 "Store Options mode"), exercised against CueList.RecordCue.</summary>
public class CueStoreFilterTests
{
    /// <summary>Configurable per-address live output, for AllParamsForSelected/AllParamsIfActive
    /// which read merged output instead of the Programmer.</summary>
    private sealed class FakeEffectiveOutputReader : IEffectiveOutputReader
    {
        private readonly Dictionary<(int, int), byte> _values = new();
        public void Set(int universeId, int channelIndex, byte value) => _values[(universeId, channelIndex)] = value;
        public byte GetEffectiveValue(int universeId, int channelIndex) => _values.GetValueOrDefault((universeId, channelIndex));
        public OutputOwner? GetOwner(int universeId, int channelIndex) => null;
    }

    private static FixtureProfile DimmerAndColor() => new()
    {
        Id = "test-fixture",
        Manufacturer = "Test",
        Model = "DimmerColor",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "2ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 0 },
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 1, DefaultValue = 0 },
                },
            },
        },
    };

    private static (Patch patch, PatchedFixture a, PatchedFixture b) BuildTwoFixtures()
    {
        var patch = new Patch();
        var profile = DimmerAndColor();
        var a = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        var b = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 3);
        patch.Add(a);
        patch.Add(b);
        return (patch, a, b);
    }

    private static Cue Store(CueList cueList, Patch patch, Programmer programmer, FixtureSelection selection,
        IEffectiveOutputReader effectiveOutput, CueStoreFilter filter) =>
        cueList.RecordCue(patch, programmer, selection, effectiveOutput, "Cue", 1,
            new CueStoreOptions(CueTiming.Default, CueTriggerMode.Manual, TimeSpan.Zero, filter));

    [Fact]
    public void AllStage_StoresEveryPatchedChannel_ProgrammerOrDefault_RegardlessOfSelectionOrTouch()
    {
        var (patch, a, _) = BuildTwoFixtures();
        var programmer = new Programmer();
        programmer.SetChannel(a.UniverseId, a.AbsoluteIndex(a.Mode.Channels[0]), 100); // only fixture A touched

        var cue = Store(new CueList(), patch, programmer, new FixtureSelection(), new FakeEffectiveOutputReader(), CueStoreFilter.AllStage);

        Assert.Equal(4, cue.Levels.Count); // both fixtures, both channels - untouched fall back to DefaultValue
    }

    [Fact]
    public void AllEditor_StoresOnly_ProgrammerTouchedChannels_SparseAcrossFixtures()
    {
        var (patch, a, b) = BuildTwoFixtures();
        var programmer = new Programmer();
        programmer.SetChannel(a.UniverseId, a.AbsoluteIndex(a.Mode.Channels[0]), 100); // only A's Dimmer touched

        var cue = Store(new CueList(), patch, programmer, new FixtureSelection(), new FakeEffectiveOutputReader(), CueStoreFilter.AllEditor);

        var key = (a.UniverseId, a.AbsoluteIndex(a.Mode.Channels[0]));
        Assert.Single(cue.Levels);
        Assert.Equal(100, cue.Levels[key].AbsoluteValue);
        var bKey = (b.UniverseId, b.AbsoluteIndex(b.Mode.Channels[0]));
        Assert.False(cue.Levels.ContainsKey(bKey));
    }

    [Fact]
    public void ActiveOnly_StoresOnly_SelectedFixtures_Touched_Channels()
    {
        var (patch, a, b) = BuildTwoFixtures();
        var programmer = new Programmer();
        programmer.SetChannel(a.UniverseId, a.AbsoluteIndex(a.Mode.Channels[0]), 100);
        programmer.SetChannel(b.UniverseId, b.AbsoluteIndex(b.Mode.Channels[0]), 150); // B touched too, but not selected

        var selection = new FixtureSelection();
        selection.Add(a);

        var cue = Store(new CueList(), patch, programmer, selection, new FakeEffectiveOutputReader(), CueStoreFilter.ActiveOnly);

        Assert.Single(cue.Levels);
        Assert.Equal(100, cue.Levels[(a.UniverseId, a.AbsoluteIndex(a.Mode.Channels[0]))].AbsoluteValue);
    }

    [Fact]
    public void AllParamsForSelected_IncludesAllChannels_ForSelectedFixture_WithDimmerAboveZero_LiveValues()
    {
        var (patch, a, b) = BuildTwoFixtures();
        var programmer = new Programmer(); // deliberately empty - AllParamsForSelected reads live merged output, not Programmer
        var effectiveOutput = new FakeEffectiveOutputReader();
        int dimmerIdx = a.AbsoluteIndex(a.Mode.Channels[0]);
        int redIdx = a.AbsoluteIndex(a.Mode.Channels[1]);
        effectiveOutput.Set(a.UniverseId, dimmerIdx, 255);
        effectiveOutput.Set(a.UniverseId, redIdx, 77);

        var selection = new FixtureSelection();
        selection.Add(a);

        var cue = Store(new CueList(), patch, programmer, selection, effectiveOutput, CueStoreFilter.AllParamsForSelected);

        Assert.Equal(2, cue.Levels.Count); // both of A's channels, none of B's
        Assert.Equal(255, cue.Levels[(a.UniverseId, dimmerIdx)].AbsoluteValue);
        Assert.Equal(77, cue.Levels[(a.UniverseId, redIdx)].AbsoluteValue);
    }

    [Fact]
    public void AllParamsForSelected_ExcludesSelectedFixture_WhoseDimmerIsAtZero()
    {
        var (patch, a, _) = BuildTwoFixtures();
        var programmer = new Programmer();
        var effectiveOutput = new FakeEffectiveOutputReader(); // dimmer stays at 0 (default)

        var selection = new FixtureSelection();
        selection.Add(a);

        var cue = Store(new CueList(), patch, programmer, selection, effectiveOutput, CueStoreFilter.AllParamsForSelected);

        Assert.Empty(cue.Levels);
    }

    [Fact]
    public void AllParamsForSelected_NeverGates_FixtureWithNoIntensityChannel()
    {
        var patch = new Patch();
        var profile = new FixtureProfile
        {
            Id = "no-dimmer",
            Manufacturer = "Test",
            Model = "ColorOnly",
            Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 0 } } } },
        };
        var fixture = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        patch.Add(fixture);

        var programmer = new Programmer();
        var effectiveOutput = new FakeEffectiveOutputReader();
        effectiveOutput.Set(0, 0, 42);

        var selection = new FixtureSelection();
        selection.Add(fixture);

        var cue = Store(new CueList(), patch, programmer, selection, effectiveOutput, CueStoreFilter.AllParamsForSelected);

        Assert.Single(cue.Levels); // included despite having no Intensity channel to gate on
        Assert.Equal(42, cue.Levels[(0, 0)].AbsoluteValue);
    }

    [Fact]
    public void AllParamsIfActive_Includes_ProgrammerTouchedButUnselected_Fixture_WhenDimmerAboveZero()
    {
        var (patch, a, b) = BuildTwoFixtures();
        var programmer = new Programmer();
        int bDimmerIdx = b.AbsoluteIndex(b.Mode.Channels[0]);
        programmer.SetChannel(b.UniverseId, bDimmerIdx, 10); // B touched via Programmer, not selected

        var effectiveOutput = new FakeEffectiveOutputReader();
        effectiveOutput.Set(b.UniverseId, bDimmerIdx, 200); // and actually on stage

        var selection = new FixtureSelection(); // A selected, but never touched at all
        selection.Add(a);

        var cue = Store(new CueList(), patch, programmer, selection, effectiveOutput, CueStoreFilter.AllParamsIfActive);

        // A: selected but dimmer at 0 -> excluded. B: not selected but Programmer-touched + dimmer>0 -> included.
        Assert.Equal(2, cue.Levels.Count); // B's two channels
        Assert.Equal(200, cue.Levels[(b.UniverseId, bDimmerIdx)].AbsoluteValue);
    }

    [Fact]
    public void AllParamsIfActive_Excludes_CompletelyUntouchedUnselectedFixture()
    {
        var (patch, a, b) = BuildTwoFixtures();
        var programmer = new Programmer();
        var effectiveOutput = new FakeEffectiveOutputReader();
        var selection = new FixtureSelection();
        selection.Add(a); // A selected but dimmer stays 0

        var cue = Store(new CueList(), patch, programmer, selection, effectiveOutput, CueStoreFilter.AllParamsIfActive);

        Assert.Empty(cue.Levels); // neither fixture qualifies
    }
}

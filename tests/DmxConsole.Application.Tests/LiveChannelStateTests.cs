using DmxConsole.Application.Live;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Application.Tests;

/// <summary>OPERATOR_UX_ROADMAP.md §1/§2 - the unified LIVE read model (Effective Live Value /
/// Editor-Pending Value / Provenance) and the six LiveFilter values it must answer identically
/// for every consumer (Channels, Fixtures, the Editor inspector).</summary>
public class LiveChannelStateTests
{
    private static readonly IReadOnlySet<(int, int)> NoneUsed = new HashSet<(int, int)>();

    [Fact]
    public void UntouchedChannel_HasNoEffectiveOwnerNoEditorValue_NotUsedNotSelected()
    {
        var patch = TestFixtures.BuildPatch(1);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new Core.Presets.PresetLibrary(), new ExecutorBank());
        engine.Tick();

        var state = LiveChannelState.For(context, 0, 0, isSelected: false, NoneUsed);

        Assert.Equal(0, state.EffectiveValue);
        Assert.Null(state.Owner);
        Assert.False(state.HasEditorValue);
        Assert.False(state.EditorValueIsLive);
        Assert.False(state.IsSelected);
        Assert.False(state.IsUsedInShow);
        Assert.True(state.Matches(LiveFilter.All));
        Assert.False(state.Matches(LiveFilter.EditorOnly));
        Assert.False(state.Matches(LiveFilter.LiveOnStage));
        Assert.False(state.Matches(LiveFilter.UsedInShow));
        Assert.False(state.Matches(LiveFilter.Selected));
    }

    [Fact]
    public void ProgrammerValue_IsEditorPendingAndLive_WhenNothingElseContends()
    {
        var patch = TestFixtures.BuildPatch(1);
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 200);
        var engine = new DmxOutputEngine(patch);
        engine.AddLayer(programmer);
        engine.Tick();

        var context = new ConsoleContext(patch, programmer, new FixtureSelection(), new GroupManager(),
            engine, new Core.Presets.PresetLibrary(), new ExecutorBank());

        var state = LiveChannelState.For(context, 0, 0, isSelected: false, NoneUsed);

        Assert.Equal(200, state.EffectiveValue);
        Assert.Equal(OwnerKind.Programmer, state.Owner!.Kind);
        Assert.Equal((byte)200, state.EditorValue);
        Assert.True(state.EditorValueIsLive);
        Assert.True(state.Matches(LiveFilter.EditorOnly));
        Assert.True(state.Matches(LiveFilter.LiveOnStage));
    }

    [Fact]
    public void ProgrammerValue_KnockedOutOrLosingPriority_IsPendingButNotLive()
    {
        // A Programmer value can exist (Editor/Pending) without currently winning the merge -
        // the roadmap explicitly distinguishes these two facts (§6: "whether the pending Editor
        // value currently wins the output or merely exists as an unstored edit").
        var patch = TestFixtures.BuildPatch(1);
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 200);
        programmer.Knockout(0, 0); // stored but suppressed - HasStoredValue still true, TryGetChannelValue false
        var engine = new DmxOutputEngine(patch);
        engine.AddLayer(programmer);
        engine.Tick();

        var context = new ConsoleContext(patch, programmer, new FixtureSelection(), new GroupManager(),
            engine, new Core.Presets.PresetLibrary(), new ExecutorBank());

        var state = LiveChannelState.For(context, 0, 0, isSelected: false, NoneUsed);

        Assert.True(state.HasEditorValue);
        Assert.False(state.EditorValueIsLive); // knocked out - Programmer isn't the winning owner
        Assert.Null(state.Owner); // nothing else is driving it either
        Assert.True(state.Matches(LiveFilter.EditorOnly));
        Assert.False(state.Matches(LiveFilter.LiveOnStage));
    }

    [Fact]
    public void UsedInShow_ReflectsPassedInAddressSet_IndependentOfCurrentValue()
    {
        var patch = TestFixtures.BuildPatch(1);
        var fixture = patch.Fixtures[0];
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new Core.Presets.PresetLibrary(), new ExecutorBank());
        engine.Tick();

        var used = new HashSet<(int, int)> { (fixture.UniverseId, fixture.AbsoluteIndex(fixture.Mode.Channels[0])) };
        var state = LiveChannelState.For(context, fixture.UniverseId, fixture.AbsoluteIndex(fixture.Mode.Channels[0]), isSelected: false, used);

        Assert.True(state.IsUsedInShow);
        Assert.True(state.Matches(LiveFilter.UsedInShow));
        Assert.Equal(0, state.EffectiveValue); // used-in-show is independent of currently being at 0%
    }

    [Fact]
    public void Selected_ReflectsThePassedInFlag_NotDerivedElsewhere()
    {
        var patch = TestFixtures.BuildPatch(1);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new Core.Presets.PresetLibrary(), new ExecutorBank());
        engine.Tick();

        var state = LiveChannelState.For(context, 0, 0, isSelected: true, NoneUsed);

        Assert.True(state.IsSelected);
        Assert.True(state.Matches(LiveFilter.Selected));
    }

    [Fact]
    public void Patched_AlwaysMatches_HonestNoOpForAddressBasedRows()
    {
        var patch = TestFixtures.BuildPatch(1);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new Core.Presets.PresetLibrary(), new ExecutorBank());
        engine.Tick();

        var state = LiveChannelState.For(context, 0, 0, isSelected: false, NoneUsed);

        Assert.True(state.Matches(LiveFilter.Patched));
    }
}

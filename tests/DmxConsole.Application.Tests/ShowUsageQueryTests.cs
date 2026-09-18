using DmxConsole.Application.Live;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Application.Tests;

/// <summary>OPERATOR_UX_ROADMAP.md §2 "Used in Show" - a channel counts as used when it's
/// referenced by a Cue List assigned to an Executor, regardless of its current live value.</summary>
public class ShowUsageQueryTests
{
    private static readonly FixtureSelection EmptySelection = new();

    private static Cue Record(CueList cueList, Patch patch, Programmer programmer, double number) =>
        cueList.RecordCue(patch, programmer, EmptySelection, new DmxOutputEngine(patch), "Cue", number,
            new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));

    [Fact]
    public void ReferencedAddresses_IncludesChannelsFromCueListsAssignedToExecutors()
    {
        var patch = TestFixtures.BuildPatch(1);
        var fixture = patch.Fixtures[0];
        var programmer = new Programmer();
        var cueList = new CueList();
        Record(cueList, patch, programmer, 1);

        var executors = new ExecutorBank();
        executors.Add(1).Assign(cueList);

        var context = new ConsoleContext(patch, programmer, new FixtureSelection(), new GroupManager(),
            new DmxOutputEngine(patch), new Core.Presets.PresetLibrary(), executors);

        var used = ShowUsageQuery.ReferencedAddresses(context);

        Assert.Contains((fixture.UniverseId, fixture.AbsoluteIndex(fixture.Mode.Channels[0])), used);
    }

    [Fact]
    public void ReferencedAddresses_ExcludesChannelsFromUnassignedCueLists()
    {
        // A CueList that exists but was never Assign()ed to any Executor is not reachable by any
        // real playback path - it must not be reported as "used in show".
        var patch = TestFixtures.BuildPatch(1);
        var programmer = new Programmer();
        var orphanCueList = new CueList();
        Record(orphanCueList, patch, programmer, 1);

        var executors = new ExecutorBank(); // nothing assigned
        var context = new ConsoleContext(patch, programmer, new FixtureSelection(), new GroupManager(),
            new DmxOutputEngine(patch), new Core.Presets.PresetLibrary(), executors);

        Assert.Empty(ShowUsageQuery.ReferencedAddresses(context));
    }

    [Fact]
    public void ReferencedAddresses_NoExecutorsAtAll_ReturnsEmptySet()
    {
        var patch = TestFixtures.BuildPatch(1);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            new DmxOutputEngine(patch), new Core.Presets.PresetLibrary(), new ExecutorBank());

        Assert.Empty(ShowUsageQuery.ReferencedAddresses(context));
    }
}

using DmxConsole.Application;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;
using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>
/// Separate IN/OUT progress display slice: CueListViewModel.RefreshStatus() now reads
/// CueList.GetStatus().FadeIn/FadeOut (in addition to the pre-existing aggregate
/// TransitionProgress from GetTransitionStatus()) and exposes them as FadeInProgress/
/// FadeOutProgress/FadeInRemainingText/FadeOutRemainingText for CueListPanel.razor's two
/// progress bars. RefreshStatus() runs synchronously off CueList.Changed (Go() raises it), the
/// same event the pre-existing TransitionProgress wiring already relies on - no new refresh
/// mechanism, just two more values read on the same trigger.
/// </summary>
public class CueListViewModelFadeProgressTests
{
    private sealed class StubEffectiveOutputReader : IEffectiveOutputReader
    {
        public byte GetEffectiveValue(int universeId, int channelIndex) => 0;
        public OutputOwner? GetOwner(int universeId, int channelIndex) => null;
    }

    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private static (CueListViewModel Vm, CueList CueList, Patch Patch, Programmer Programmer) BuildRig()
    {
        var patch = new Patch();
        var profile = Dimmer1();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1) { Number = 1 };
        patch.Add(fixture);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var cueList = new CueList();
        var executor = new Executor(-1);
        var vm = new CueListViewModel(patch, context.Programmer, context.Selection, engine, cueList, dispatcher, executor);
        return (vm, cueList, patch, context.Programmer);
    }

    [Fact]
    public void RefreshStatus_NoActiveCue_BothFadeProgressesReportComplete()
    {
        var (vm, _, _, _) = BuildRig();

        Assert.Equal(1.0, vm.FadeInProgress);
        Assert.Equal(1.0, vm.FadeOutProgress);
    }

    [Fact]
    public void RefreshStatus_AsymmetricTiming_ExposesIndependentInOutProgress()
    {
        var (vm, cueList, patch, programmer) = BuildRig();
        var stub = new StubEffectiveOutputReader();
        cueList.RecordCue(patch, programmer, new FixtureSelection(), stub, "Cue 1", 1,
            new CueStoreOptions(
                new CueTiming(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(500), TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));

        cueList.Go(); // raises CueList.Changed -> CueListViewModel.OnCueListChanged -> RefreshStatus()

        // Immediately after Go(), neither side is complete yet (both durations are well above zero).
        Assert.True(vm.FadeInProgress < 1.0);
        Assert.True(vm.FadeOutProgress < 1.0);

        Thread.Sleep(200); // past TimeIn (150ms), well before TimeOut (500ms)
        // The production poll timer (100ms interval, see CueListViewModel's constructor doc
        // comment) is what normally drives this next read; invoke the same private RefreshStatus()
        // it calls directly here so the test is deterministic instead of racing a background Timer.
        var refresh = typeof(CueListViewModel).GetMethod("RefreshStatus",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        refresh.Invoke(vm, null);

        Assert.Equal(1.0, vm.FadeInProgress); // In finished
        Assert.True(vm.FadeOutProgress < 1.0); // Out still progressing independently
    }
}

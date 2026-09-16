using DmxConsole.Application.Live;
using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>Regression coverage for a real bug: Channels LIVE and Fixtures LIVE used to keep
/// their chosen LiveFilter in a private Razor component field. Blazor recreates a pane's
/// inactive-tab component (and every private field on it) whenever the operator switches to
/// another tab and back, so the filter was silently resetting to All on every revisit -
/// unacceptable console UX. The fix moves the filter onto MainViewModel (the one long-lived
/// singleton every view already shares, per docs/OPERATOR_UX_ROADMAP.md's "no parallel state"
/// rule) - these tests pin the two properties' existence, default, and persistence across
/// however many times a value is read, which is the only thing the ViewModel layer can prove
/// about this (the "component gets recreated" half of the bug is inherently a Razor-lifecycle
/// fact, verified by manual browser testing instead - see PR/commit notes).</summary>
public class MainViewModelTests
{
    [Fact]
    public void ChannelsLiveFilter_DefaultsToAll_OnFirstConstruction()
    {
        var vm = new MainViewModel();
        Assert.Equal(LiveFilter.All, vm.ChannelsLiveFilter);
    }

    [Fact]
    public void FixturesLiveFilter_DefaultsToAll_OnFirstConstruction()
    {
        var vm = new MainViewModel();
        Assert.Equal(LiveFilter.All, vm.FixturesLiveFilter);
    }

    [Fact]
    public void ChannelsLiveFilter_RetainsAnAssignedValue_AcrossAnyNumberOfReads()
    {
        // The essence of the fix: this is a plain, durable property on the singleton VM, not
        // something a component's own OnInitialized can quietly reinitialize.
        var vm = new MainViewModel();
        vm.ChannelsLiveFilter = LiveFilter.Selected;

        Assert.Equal(LiveFilter.Selected, vm.ChannelsLiveFilter);
        Assert.Equal(LiveFilter.Selected, vm.ChannelsLiveFilter); // reading again changes nothing
    }

    [Fact]
    public void FixturesLiveFilter_RetainsAnAssignedValue_AcrossAnyNumberOfReads()
    {
        var vm = new MainViewModel();
        vm.FixturesLiveFilter = LiveFilter.UsedInShow;

        Assert.Equal(LiveFilter.UsedInShow, vm.FixturesLiveFilter);
        Assert.Equal(LiveFilter.UsedInShow, vm.FixturesLiveFilter);
    }

    [Fact]
    public void ChannelsAndFixturesLiveFilter_AreIndependent()
    {
        var vm = new MainViewModel();
        vm.ChannelsLiveFilter = LiveFilter.EditorOnly;
        vm.FixturesLiveFilter = LiveFilter.LiveOnStage;

        Assert.Equal(LiveFilter.EditorOnly, vm.ChannelsLiveFilter);
        Assert.Equal(LiveFilter.LiveOnStage, vm.FixturesLiveFilter);
    }

    [Fact]
    public void ProgrammerVm_LiveFilter_DefaultsToEditorOnly_WithoutAnyComponentInitializingIt()
    {
        // Regression for the sibling variant of the same bug: ProgrammerPanel.razor used to force
        // Prog.LiveFilter = EditorOnly inside OnInitialized, every single mount - which discarded
        // whatever the operator had actually picked, every time they revisited the panel. The
        // default now lives solely on ProgrammerViewModel's own field initializer, so it must
        // already read EditorOnly straight out of MainViewModel's constructor, with no Razor
        // component involved at all.
        var vm = new MainViewModel();
        Assert.Equal(LiveFilter.EditorOnly, vm.ProgrammerVm.LiveFilter);
    }

    [Fact]
    public void ProgrammerVm_LiveFilter_RetainsAnAssignedValue()
    {
        var vm = new MainViewModel();
        vm.ProgrammerVm.LiveFilter = LiveFilter.Selected;

        Assert.Equal(LiveFilter.Selected, vm.ProgrammerVm.LiveFilter);
    }
}

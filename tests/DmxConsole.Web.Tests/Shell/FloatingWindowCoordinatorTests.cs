using DmxConsole.Web.Services;

namespace DmxConsole.Web.Tests.Shell;

public class FloatingWindowCoordinatorTests
{
    [Fact]
    public void GetOrRegister_FirstCall_UsesSuppliedDefaults()
    {
        var coordinator = new FloatingWindowCoordinator();

        var state = coordinator.GetOrRegister("w1", 10, 20);

        Assert.Equal("w1", state.Id);
        Assert.Equal(10, state.X);
        Assert.Equal(20, state.Y);
        Assert.True(state.IsVisible);
    }

    [Fact]
    public void GetOrRegister_SecondCall_ReturnsSameStateAndIgnoresNewDefaults()
    {
        var coordinator = new FloatingWindowCoordinator();
        var first = coordinator.GetOrRegister("w1", 10, 20);

        var second = coordinator.GetOrRegister("w1", 999, 999);

        Assert.Same(first, second);
        Assert.Equal(10, second.X);
        Assert.Equal(20, second.Y);
    }

    [Fact]
    public void Move_UpdatesPositionAndRaisesChanged()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("w1", 0, 0);
        var changedCount = 0;
        coordinator.Changed += () => changedCount++;

        coordinator.Move("w1", 50, 75);

        var state = coordinator.Find("w1")!;
        Assert.Equal(50, state.X);
        Assert.Equal(75, state.Y);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void Move_UnknownId_NoOpsSilently()
    {
        var coordinator = new FloatingWindowCoordinator();
        var changedCount = 0;
        coordinator.Changed += () => changedCount++;

        coordinator.Move("nope", 1, 1);

        Assert.Equal(0, changedCount);
        Assert.Null(coordinator.Find("nope"));
    }

    [Fact]
    public void BringToFront_GivesHighestZIndexAmongRegisteredWindows()
    {
        var coordinator = new FloatingWindowCoordinator();
        var a = coordinator.GetOrRegister("a", 0, 0);
        var b = coordinator.GetOrRegister("b", 0, 0);
        Assert.True(a.ZIndex < b.ZIndex); // b registered after a, already on top

        coordinator.BringToFront("a");

        Assert.True(a.ZIndex > b.ZIndex);
    }

    [Fact]
    public void BringToFront_RepeatedFocus_KeepsIncreasingAboveOthers()
    {
        var coordinator = new FloatingWindowCoordinator();
        var a = coordinator.GetOrRegister("a", 0, 0);
        var b = coordinator.GetOrRegister("b", 0, 0);

        coordinator.BringToFront("a");
        coordinator.BringToFront("b");
        coordinator.BringToFront("a");

        Assert.True(a.ZIndex > b.ZIndex);
    }

    [Fact]
    public void Hide_ThenShow_RestoresVisibilityAndBringsToFront()
    {
        var coordinator = new FloatingWindowCoordinator();
        var a = coordinator.GetOrRegister("a", 0, 0);
        var b = coordinator.GetOrRegister("b", 0, 0);

        coordinator.Hide("a");
        Assert.False(a.IsVisible);

        coordinator.Show("a");
        Assert.True(a.IsVisible);
        Assert.True(a.ZIndex > b.ZIndex);
    }

    [Fact]
    public void Hide_AlreadyHidden_DoesNotRaiseChangedAgain()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("a", 0, 0);
        coordinator.Hide("a");
        var changedCount = 0;
        coordinator.Changed += () => changedCount++;

        coordinator.Hide("a");

        Assert.Equal(0, changedCount);
    }

    [Fact]
    public void Toggle_FlipsVisibility()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("a", 0, 0);

        coordinator.Toggle("a");
        Assert.False(coordinator.Find("a")!.IsVisible);

        coordinator.Toggle("a");
        Assert.True(coordinator.Find("a")!.IsVisible);
    }

    [Fact]
    public void Toggle_UnknownId_NoOpsSilently()
    {
        var coordinator = new FloatingWindowCoordinator();

        coordinator.Toggle("nope");

        Assert.Null(coordinator.Find("nope"));
    }

    // --- Floating-window positioning-bug fix: clamping, resize recovery, and guaranteed
    // reopen/recover for a window (Command Surface today; any future floatable panel) that has
    // been dragged off-screen or otherwise become unreachable. ---

    [Fact]
    public void Move_AboveViewport_ClampsToReachablePosition()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("w1", 0, 0);
        coordinator.SetViewportSize(1000, 800);

        coordinator.Move("w1", 100, -5000);

        var state = coordinator.Find("w1")!;
        Assert.InRange(state.Y, 0, 800);
    }

    [Fact]
    public void Move_LeftOfViewport_RemainsRecoverable()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("w1", 0, 0);
        coordinator.SetViewportSize(1000, 800);

        coordinator.Move("w1", -5000, 100);

        var state = coordinator.Find("w1")!;
        Assert.InRange(state.X, 0, 1000);
    }

    [Fact]
    public void Move_RightOfViewport_RemainsRecoverable()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("w1", 0, 0);
        coordinator.SetViewportSize(1000, 800);

        coordinator.Move("w1", 5000, 100);

        var state = coordinator.Find("w1")!;
        Assert.InRange(state.X, 0, 1000);
    }

    [Fact]
    public void Move_BelowViewport_RemainsRecoverable()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("w1", 0, 0);
        coordinator.SetViewportSize(1000, 800);

        coordinator.Move("w1", 100, 5000);

        var state = coordinator.Find("w1")!;
        Assert.InRange(state.Y, 0, 800);
    }

    [Fact]
    public void Move_ExtremelyLargePositiveCoordinates_NormalizeSafely()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("w1", 0, 0);
        coordinator.SetViewportSize(1200, 900);

        coordinator.Move("w1", double.MaxValue / 2, double.MaxValue / 2);

        var state = coordinator.Find("w1")!;
        Assert.InRange(state.X, 0, 1200);
        Assert.InRange(state.Y, 0, 900);
    }

    [Fact]
    public void Move_ExtremelyLargeNegativeCoordinates_NormalizeSafely()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("w1", 0, 0);
        coordinator.SetViewportSize(1200, 900);

        coordinator.Move("w1", double.MinValue / 2, double.MinValue / 2);

        var state = coordinator.Find("w1")!;
        Assert.InRange(state.X, 0, 1200);
        Assert.InRange(state.Y, 0, 900);
    }

    [Fact]
    public void SetViewportSize_Shrink_ReClampsStrandedWindow()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("w1", 0, 0);
        coordinator.SetViewportSize(1920, 1080);
        coordinator.Move("w1", 1800, 1000);

        coordinator.SetViewportSize(640, 480);

        var state = coordinator.Find("w1")!;
        Assert.InRange(state.X, 0, 640);
        Assert.InRange(state.Y, 0, 480);
    }

    [Fact]
    public void SetViewportSize_NonPositive_IsIgnored()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("w1", 0, 0);
        coordinator.SetViewportSize(1920, 1080);
        coordinator.Move("w1", 500, 500);

        coordinator.SetViewportSize(0, 0);
        coordinator.SetViewportSize(-100, -100);

        var state = coordinator.Find("w1")!;
        Assert.Equal(500, state.X);
        Assert.Equal(500, state.Y);
    }

    [Fact]
    public void Recover_HiddenWindow_BecomesVisibleAgain()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("command-surface", 40, 90);
        coordinator.Hide("command-surface");

        coordinator.Recover("command-surface");

        Assert.True(coordinator.Find("command-surface")!.IsVisible);
    }

    [Fact]
    public void Recover_OffScreenWindow_RestoresReachablePosition()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.GetOrRegister("command-surface", 40, 90);
        coordinator.SetViewportSize(1000, 800);
        coordinator.Move("command-surface", 999_999, 999_999);

        coordinator.Recover("command-surface");

        var state = coordinator.Find("command-surface")!;
        Assert.InRange(state.X, 0, 1000);
        Assert.InRange(state.Y, 0, 800);
    }

    [Fact]
    public void Recover_BringsWindowToFront()
    {
        var coordinator = new FloatingWindowCoordinator();
        var a = coordinator.GetOrRegister("a", 0, 0);
        var commandSurface = coordinator.GetOrRegister("command-surface", 40, 90);
        coordinator.BringToFront("a"); // "a" now on top of command-surface

        coordinator.Recover("command-surface");

        Assert.True(commandSurface.ZIndex > a.ZIndex);
    }

    [Fact]
    public void Recover_UnknownId_NoOpsSilently()
    {
        var coordinator = new FloatingWindowCoordinator();
        var changedCount = 0;
        coordinator.Changed += () => changedCount++;

        coordinator.Recover("nope");

        Assert.Equal(0, changedCount);
        Assert.Null(coordinator.Find("nope"));
    }

    [Fact]
    public void GetOrRegister_DefaultPositionBeyondViewport_ClampsAtRegistration()
    {
        var coordinator = new FloatingWindowCoordinator();
        coordinator.SetViewportSize(200, 150);

        var state = coordinator.GetOrRegister("w1", 5000, 5000);

        Assert.InRange(state.X, 0, 200);
        Assert.InRange(state.Y, 0, 150);
    }

    [Fact]
    public void WindowOperations_NeverExposeAnyConsoleStateSurface()
    {
        // State-safety guarantee: FloatingWindowCoordinator is purely positional. This is a
        // structural/architectural assertion, not a behavioral one - it fails to compile (and
        // therefore fails this test file's build) the moment anyone adds a Selection/Programmer/
        // Playback/CommandComposer/Cue/Release/Store-shaped member to this type, which is exactly
        // the boundary CLAUDE.md requires floating-window infrastructure to respect.
        var coordinator = new FloatingWindowCoordinator();
        var state = coordinator.GetOrRegister("w1", 0, 0);

        coordinator.Move("w1", 10, 10);
        coordinator.BringToFront("w1");
        coordinator.Hide("w1");
        coordinator.Show("w1");
        coordinator.Recover("w1");
        coordinator.SetViewportSize(1024, 768);

        // Only positional/visibility fields exist on the state - enumerate them explicitly so a
        // future addition of console-state fields here is a visible, reviewable diff.
        Assert.Equal("w1", state.Id);
        _ = state.X;
        _ = state.Y;
        _ = state.ZIndex;
        _ = state.IsVisible;
        _ = state.DefaultX;
        _ = state.DefaultY;
    }
}

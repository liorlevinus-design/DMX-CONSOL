using DmxConsole.Application.Commands.Patch;
using DmxConsole.Core.Fixtures;
using Xunit;

namespace DmxConsole.Application.Tests;

/// <summary>
/// Quick Patch stabilization slice - the ONE shared Patch operation both the PATCH screen and
/// the Command Surface's Quick Patch grammar dispatch. Atomicity (no half-patched state on a
/// numbering conflict) and "already patched" validation happening BEFORE any fixture is created
/// are proven here at the Application layer, independent of either UI entry point.
///
/// Patch/Undo-architecture slice: PatchFixturesCommand is an IConsoleAction (not an
/// IConsoleCommand), dispatched exclusively through CommandDispatcher.DispatchAction.
/// DispatchAction never calls into UndoRedoService or pushes to the Undo stack (see
/// CommandDispatcher.DispatchAction), so Quick Patch structurally cannot enter Programming
/// Undo - there is no Undo/PrepareUndo test here because the type has no such capability
/// by design.
/// </summary>
public class PatchFixturesCommandTests
{
    private static FixtureProfile Dimmer1() => TestFixtures.Dimmer1();

    [Fact]
    public void Execute_CreatesAllRequestedFixtures_AtTheRequestedNumbersAndAddresses()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 0);
        var profile = Dimmer1();
        var requests = new[]
        {
            new PatchFixturesCommand.Request(1, 0, 11, "F1"),
            new PatchFixturesCommand.Request(2, 0, 12, "F2"),
        };

        var result = dispatcher.DispatchAction(new PatchFixturesCommand(profile, profile.Modes[0], requests));

        Assert.True(result.Success);
        Assert.Equal(2, context.Patch.Fixtures.Count);
        Assert.NotNull(context.Patch.FindByNumber(1));
        Assert.NotNull(context.Patch.FindByNumber(2));
        Assert.Equal(11, context.Patch.FindByNumber(1)!.StartAddress);
        Assert.Equal(12, context.Patch.FindByNumber(2)!.StartAddress);
    }

    [Fact]
    public void Execute_FailsAtomically_WhenAnyRequestedNumberIsAlreadyPatched_NothingIsCreated()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 0);
        var profile = Dimmer1();
        // Pre-existing Fixture #3 - part of a batch requesting #1-#4 conflicts on #3.
        context.Patch.Add(new PatchedFixture(profile, profile.Modes[0], 0, 50) { Number = 3 });

        var requests = Enumerable.Range(1, 4)
            .Select(n => new PatchFixturesCommand.Request(n, 0, 10 + n, $"F{n}"))
            .ToArray();

        var result = dispatcher.DispatchAction(new PatchFixturesCommand(profile, profile.Modes[0], requests));

        Assert.False(result.Success);
        Assert.Contains("#3", result.Error);
        // Atomic: #1, #2, #4 must NOT have been created just because #3 conflicted.
        Assert.Null(context.Patch.FindByNumber(1));
        Assert.Null(context.Patch.FindByNumber(2));
        Assert.Null(context.Patch.FindByNumber(4));
        Assert.Single(context.Patch.Fixtures); // only the pre-existing #3
    }

    [Fact]
    public void Execute_WithEmptyRequestList_FailsCleanly_NoException()
    {
        var (_, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 0);
        var profile = Dimmer1();

        var result = dispatcher.DispatchAction(new PatchFixturesCommand(profile, profile.Modes[0], Array.Empty<PatchFixturesCommand.Request>()));

        Assert.False(result.Success);
    }

    [Fact]
    public void Execute_ReplayedOnTheSameInstance_ReValidatesFromScratch_NeverCorruptsState()
    {
        // Macro replay (Patch/Undo-architecture slice) dispatches the SAME PatchFixturesCommand
        // instance a second time (Actions hold no per-execution mutable state, per
        // MacroPlaybackService's own doc comment) - re-running Execute must behave exactly like a
        // fresh dispatch: it re-validates the requested numbers against the CURRENT Patch state,
        // never assumes anything from the first run.
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 0);
        var profile = Dimmer1();
        var command = new PatchFixturesCommand(profile, profile.Modes[0],
            new[] { new PatchFixturesCommand.Request(1, 0, 11, "F1") });

        var first = dispatcher.DispatchAction(command);
        Assert.True(first.Success);
        Assert.Single(context.Patch.Fixtures);

        // Replaying against the same instance a second time: #1 is now already patched, so this
        // must fail cleanly (not throw, not duplicate, not corrupt) - exactly the same "already
        // patched" validation a fresh dispatch would hit.
        var second = dispatcher.DispatchAction(command);
        Assert.False(second.Success);
        Assert.Contains("#1", second.Error);
        Assert.Single(context.Patch.Fixtures);
    }
}

using DmxConsole.Application.CommandSurface;

namespace DmxConsole.Application.Tests.CommandSurface;

/// <summary>docs/COMMAND_SURFACE_KEY_SPEC.md §2/§3/§14 - the numeric/object-context grammar:
/// bare numeric input's default FIXTURE context, GROUP/CUE establishing explicit context for the
/// current command only, context never persisting across a completed command, and the "ends in a
/// numeric/reference token -> needs ENTER" rule with no timeout involved.</summary>
public class CommandComposerObjectContextTests
{
    [Fact]
    public void BareNumeric_DefaultsToFixture_WithoutRequiringTheFixtureKeyword()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 3);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Number(2));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        // Quick-Patch/implicit-Fixture convergence slice (§4): a bare Number as the first token
        // now injects a REAL Fixture token, so the Task line shows "Fixture 2" - identical to
        // what explicit "FIXTURE 2" would show - rather than hiding the inferred object type.
        Assert.Equal("Fixture 2", final.PreviewText);
        dispatcher.Dispatch(final.ReadyOperation!);
        Assert.Single(context.Selection.Items);
        Assert.Equal(2, context.Selection.Items[0].Number);
    }

    [Fact]
    public void GroupContext_AppliesOnlyToTheCurrentCommand_NotPersisted()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 3));
        var group = context.Groups.CreateFromSelection("G1", context.Selection, number: 1);
        context.Selection.Clear();

        var composer = new CommandComposer(context);

        // First command: explicit GROUP context.
        composer.Push(CommandToken.Simple(CommandTokenKind.Group));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(50));
        var first = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));
        Assert.True(first.IsComplete);
        dispatcher.Dispatch(first.ReadyOperation!);
        Assert.Contains(context.Selection.Items, f => f == group.Fixtures[0]);
        composer.Reset(); // same "start a fresh command" reset CommandSurfaceViewModel performs after every successful dispatch

        // Second command on the SAME composer instance: bare numeric, no GROUP keyword this
        // time - must default back to FIXTURE, proving GROUP context did not leak/persist.
        composer.Push(CommandToken.Number(2));
        var second = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(second.IsComplete);
        Assert.Equal("Fixture 2", second.PreviewText); // §4: implicit Fixture is now a real, visible token
        dispatcher.Dispatch(second.ReadyOperation!);
        Assert.Single(context.Selection.Items);
        Assert.Equal(2, context.Selection.Items[0].Number);
    }

    [Fact]
    public void CueHeadToken_EstablishesContext_ButHonestlyReportsNoResolutionPathYet()
    {
        // §3: CUE is a recognized head token symmetric with FIXTURE/GROUP. Since the Cue-timing
        // slice, "CUE <n> TIME <value> ENTER" is a real, implemented grammar branch - so "CUE 5"
        // alone (before Enter) is legitimately still-typing (THRU or TIME could still follow),
        // never an immediate error. But this console still has no Application-layer command for
        // anything else targeting a Cue by number, so finalizing with nothing but the bare number
        // (no THRU, no TIME) remains a real, honestly-reported gap - never a fabricated resolution,
        // and never falls through to Fixture-selection parsing or its "Expected a fixture
        // number..." error.
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 1);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        var afterNumber = composer.Push(CommandToken.Number(5));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(afterNumber.IsComplete);
        Assert.Null(afterNumber.Error); // still-typing - THRU/TIME could still follow
        Assert.Contains(CommandTokenKind.Timing, afterNumber.ExpectedNext);
        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.DoesNotContain("Expected a fixture number", final.Error);
        Assert.Null(final.ReadyOperation);
    }

    [Fact]
    public void EndingInNumericToken_NeverCompletesWithoutExplicitEnter_NoTimeoutInvolved()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        var beforeEnter = composer.Push(CommandToken.Number(70));

        // Current (not a fresh Push) reflects the exact same not-yet-complete state - proves
        // nothing resolves it implicitly (no timer, no background completion).
        Assert.False(beforeEnter.IsComplete);
        Assert.False(composer.Current.IsComplete);
        Assert.Null(beforeEnter.ReadyOperation);

        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));
        Assert.True(final.IsComplete);
    }
}

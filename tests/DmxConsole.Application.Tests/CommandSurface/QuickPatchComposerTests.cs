using DmxConsole.Application.CommandSurface;
using DmxConsole.Application.Commands.Patch;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Tests.CommandSurface;

/// <summary>
/// Quick Patch stabilization slice - both grammar forms resolve through the SAME
/// PatchFixturesCommand as the PATCH screen, using ConsoleContext.DefaultPatchProfile/Mode as
/// the "currently active" profile (mirrored there by MainViewModel from the PATCH screen's own
/// selection - see ConsoleContext's own doc comment).
/// </summary>
public class QuickPatchComposerTests
{
    private static (ConsoleContext Context, CommandDispatcher Dispatcher) BuildRigWithActiveProfile()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 0);
        var profile = TestFixtures.Dimmer1();
        context.DefaultPatchProfile = profile;
        context.DefaultPatchMode = profile.Modes[0]; // FootprintSize = 1
        return (context, dispatcher);
    }

    [Fact]
    public void FormA_FixtureThruAtDmx_PatchesSequentialFixtures_AtSequentialAddresses()
    {
        var (context, dispatcher) = BuildRigWithActiveProfile();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(8));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        var final = composer.Push(CommandToken.Number(11));
        Assert.False(final.IsComplete); // ends in a numeric token - needs Enter, same as ordinary At

        final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        Assert.IsType<PatchFixturesCommand>(final.ReadyAction);
        var result = dispatcher.DispatchAction(final.ReadyAction!);
        Assert.True(result.Success);

        Assert.Equal(8, context.Patch.Fixtures.Count);
        for (int n = 1; n <= 8; n++)
        {
            var fixture = context.Patch.FindByNumber(n);
            Assert.NotNull(fixture);
            Assert.Equal(10 + n, fixture!.StartAddress); // 11, 12, ..., 18 - advances by footprint (1)
        }
    }

    [Fact]
    public void FormB_DmxThruFixture_PatchesSameResultAsFormA_ReverseSyntax()
    {
        var (context, dispatcher) = BuildRigWithActiveProfile();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.Number(11));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(18));
        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        Assert.IsType<PatchFixturesCommand>(final.ReadyAction);
        var result = dispatcher.DispatchAction(final.ReadyAction!);
        Assert.True(result.Success);

        Assert.Equal(8, context.Patch.Fixtures.Count);
        for (int n = 1; n <= 8; n++)
        {
            var fixture = context.Patch.FindByNumber(n);
            Assert.NotNull(fixture);
            Assert.Equal(10 + n, fixture!.StartAddress);
        }
    }

    [Fact]
    public void FormA_WithoutAnActiveProfile_FailsHonestly_NeverCrashes()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 0);
        // DefaultPatchProfile/Mode deliberately left null.
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.Number(11));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Empty(context.Patch.Fixtures);
    }

    [Fact]
    public void FormA_ConflictingFixtureNumber_FailsAtomically_NoPartialPatch()
    {
        var (context, dispatcher) = BuildRigWithActiveProfile();
        var profile = context.DefaultPatchProfile!;
        context.Patch.Add(new PatchedFixture(profile, profile.Modes[0], 0, 99) { Number = 4 }); // pre-existing conflict

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(8));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.Number(11));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        var result = dispatcher.DispatchAction(final.ReadyAction!);

        Assert.False(result.Success);
        Assert.Single(context.Patch.Fixtures); // only the pre-existing #4 - nothing half-patched
    }

    [Fact]
    public void FormA_OnGroupObjectType_IsRejected_QuickPatchNeverAppliesToGroups()
    {
        var (context, _) = BuildRigWithActiveProfile();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Group));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.Number(11));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
    }
}

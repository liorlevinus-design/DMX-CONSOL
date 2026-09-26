using DmxConsole.Application.CommandSurface;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Application.Tests.CommandSurface;

/// <summary>
/// Parameter-scoped AT (CLAUDE.md §16, PSEL-1/PSEL-2/PSEL-3): "&lt;Parameter&gt; AT &lt;value&gt;
/// [THRU &lt;value&gt;]* ENTER" and the ParameterSelection-driven bare "AT ... ENTER" fallback.
/// Applies to Current Fixture Selection x Current Parameter Selection via the shared
/// ParameterTargetResolver + SetParameterValuesCommand, reusing (never reimplementing) the exact
/// InterpolateAlongPath engine CommandComposerValueDistributionTests already proves for
/// fixture-first AT. Empty ParameterSelection + no Parameter token on the line is the hard
/// backward-compatibility boundary - see the regression tests at the bottom of this file and
/// CommandComposerValueDistributionTests itself, which is untouched by this slice.
/// </summary>
public class ParameterAtTests
{
    // ---- Fixture profiles -------------------------------------------------------------------

    private static FixtureProfile MovingHeadColor() => new()
    {
        Id = "test-mover-color",
        Manufacturer = "Test",
        Model = "MoverColor",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "6ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 },
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 1 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 2 },
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 3 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 4 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 5 },
                },
            },
        },
    };

    private static FixtureProfile ColorOnlyPar() => new()
    {
        Id = "test-par-color",
        Manufacturer = "Test",
        Model = "ParColor",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "4ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 },
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 1 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 2 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 3 },
                },
            },
        },
    };

    // ---- Rig / console plumbing --------------------------------------------------------------

    /// <summary>Builds a Patch with the given profiles, patched back-to-back starting at address 1
    /// in each fixture's own universe-0 footprint, numbered 1..N in the order given (Patch.Add's
    /// own auto-numbering) - mirrors TestFixtures.BuildConsole's shape but allows mixed profiles
    /// per fixture, which this slice's mixed-compatibility tests need and the shared TestFixtures
    /// helper (single Dimmer-only profile) does not support.</summary>
    private static (Patch Patch, ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo) BuildConsole(params FixtureProfile[] profiles)
    {
        var patch = new Patch();
        int address = 1;
        foreach (var profile in profiles)
        {
            patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: address));
            address += profile.Modes[0].Channels.Count + 4; // headroom so footprints never overlap
        }

        var programmer = new Programmer();
        var engine = new DmxOutputEngine(patch);
        engine.AddLayer(programmer);
        var context = new ConsoleContext(patch, programmer, new FixtureSelection(), new GroupManager(), engine, new Core.Presets.PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        return (patch, context, dispatcher, undoRedo);
    }

    private static void Select(ConsoleContext context, params int[] fixtureNumbers)
    {
        foreach (var n in fixtureNumbers)
            context.Selection.Add(context.Patch.FindByNumber(n)!);
    }

    private static byte? ProgrammerValue(ConsoleContext context, int fixtureNumber, ChannelType type)
    {
        var fixture = context.Patch.FindByNumber(fixtureNumber)!;
        var channel = fixture.FindChannel(type);
        if (channel is null) return null;
        return context.Programmer.HasStoredValue(fixture.UniverseId, fixture.AbsoluteIndex(channel), out var value) ? value : null;
    }

    private static CommandComposition RunToEnter(CommandComposer composer, params CommandToken[] tokens)
    {
        CommandComposition last = composer.Current;
        foreach (var token in tokens) last = composer.Push(token);
        return last;
    }

    private static CommandToken[] ParameterAt(ChannelType parameter, params double[] controlPoints)
    {
        var tokens = new List<CommandToken> { CommandToken.Parameter(parameter), CommandToken.Simple(CommandTokenKind.At), CommandToken.Number(controlPoints[0]) };
        for (int i = 1; i < controlPoints.Length; i++)
        {
            tokens.Add(CommandToken.Simple(CommandTokenKind.Thru));
            tokens.Add(CommandToken.Number(controlPoints[i]));
        }
        return tokens.ToArray();
    }

    // =========================================================================================
    // Fixed value (8-13)
    // =========================================================================================

    [Fact]
    public void RedAt40_AffectsOnlyRed_OnSelectedFixtures()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterAt(ChannelType.ColorRed, 40).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        Assert.Equal((byte)40, ProgrammerValue(context, 1, ChannelType.ColorRed));
        Assert.Null(ProgrammerValue(context, 1, ChannelType.ColorGreen));
        Assert.Null(ProgrammerValue(context, 1, ChannelType.Dimmer));
    }

    [Fact]
    public void PanAt25_AffectsOnlyPan()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterAt(ChannelType.Pan, 25).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());
        var result = dispatcher.Dispatch(final.ReadyOperation!);

        Assert.True(result.Success);
        Assert.Equal((byte)25, ProgrammerValue(context, 1, ChannelType.Pan));
        Assert.Null(ProgrammerValue(context, 1, ChannelType.Tilt));
    }

    [Fact]
    public void RedPlusGreenAt40_AffectsBoth()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var tokens = new List<CommandToken>
        {
            CommandToken.Parameter(ChannelType.ColorRed), CommandToken.Simple(CommandTokenKind.Plus), CommandToken.Parameter(ChannelType.ColorGreen),
            CommandToken.Simple(CommandTokenKind.At), CommandToken.Number(40), CommandToken.Simple(CommandTokenKind.Enter),
        };
        var final = RunToEnter(composer, tokens.ToArray());
        var result = dispatcher.Dispatch(final.ReadyOperation!);

        Assert.True(result.Success);
        Assert.Equal((byte)40, ProgrammerValue(context, 1, ChannelType.ColorRed));
        Assert.Equal((byte)40, ProgrammerValue(context, 1, ChannelType.ColorGreen));
        Assert.Null(ProgrammerValue(context, 1, ChannelType.ColorBlue));
    }

    [Fact]
    public void ParameterAt_LeavesUnrelatedProgrammerParametersUnchanged()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var fixture = context.Patch.FindByNumber(1)!;
        context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!), 200);

        var composer = new CommandComposer(context);
        var final = RunToEnter(composer, ParameterAt(ChannelType.ColorRed, 40).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal((byte)200, ProgrammerValue(context, 1, ChannelType.Pan)); // untouched
        Assert.Equal((byte)40, ProgrammerValue(context, 1, ChannelType.ColorRed));
    }

    [Fact]
    public void ParameterAt_LeavesUnselectedFixturesUnchanged()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor());
        Select(context, 1); // fixture 2 never selected

        var composer = new CommandComposer(context);
        var final = RunToEnter(composer, ParameterAt(ChannelType.ColorRed, 40).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal((byte)40, ProgrammerValue(context, 1, ChannelType.ColorRed));
        Assert.Null(ProgrammerValue(context, 2, ChannelType.ColorRed));
    }

    [Fact]
    public void ParameterAt_IncompatibleFixture_UnchangedAndSkippedNotFailed()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), ColorOnlyPar()); // fixture 2 has no Pan
        Select(context, 1, 2);

        var composer = new CommandComposer(context);
        var final = RunToEnter(composer, ParameterAt(ChannelType.Pan, 40).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());
        var result = dispatcher.Dispatch(final.ReadyOperation!);

        Assert.True(result.Success);
        Assert.Equal((byte)40, ProgrammerValue(context, 1, ChannelType.Pan));
        Assert.Null(ProgrammerValue(context, 2, ChannelType.Pan)); // fixture has no Pan channel at all -> null either way
        Assert.NotNull(result.Warning); // operator feedback: 1 affected, 1 skipped
    }

    // =========================================================================================
    // Distribution (14-18)
    // =========================================================================================

    [Fact]
    public void RedAt20Thru60_DistributesByFixtureSelectionOrder()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2, 3);

        var composer = new CommandComposer(context);
        var final = RunToEnter(composer, ParameterAt(ChannelType.ColorRed, 20, 60).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal((byte)20, ProgrammerValue(context, 1, ChannelType.ColorRed));
        Assert.Equal((byte)40, ProgrammerValue(context, 2, ChannelType.ColorRed));
        Assert.Equal((byte)60, ProgrammerValue(context, 3, ChannelType.ColorRed));
    }

    /// <summary>THE critical test (this slice's own spec): a distributed value is computed ONCE
    /// per fixture position, then fanned out to every compatible parameter on that fixture -
    /// fixture 1 gets RED=20/GREEN=20, fixture 2 gets RED=40/GREEN=40, fixture 3 gets
    /// RED=60/GREEN=60. NOT a flattened 6-slot distribution (which would produce something like
    /// RED=20,GREEN=28,RED=36,... - see the negative test below for the explicit disproof).</summary>
    [Fact]
    public void RedPlusGreenAt20Thru60_AppliesOneFixtureValueToBothParameters()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2, 3);

        var composer = new CommandComposer(context);
        var tokens = new List<CommandToken>
        {
            CommandToken.Parameter(ChannelType.ColorRed), CommandToken.Simple(CommandTokenKind.Plus), CommandToken.Parameter(ChannelType.ColorGreen),
            CommandToken.Simple(CommandTokenKind.At), CommandToken.Number(20), CommandToken.Simple(CommandTokenKind.Thru), CommandToken.Number(60),
            CommandToken.Simple(CommandTokenKind.Enter),
        };
        var final = RunToEnter(composer, tokens.ToArray());
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        Assert.Equal((byte)20, ProgrammerValue(context, 1, ChannelType.ColorRed));
        Assert.Equal((byte)20, ProgrammerValue(context, 1, ChannelType.ColorGreen));
        Assert.Equal((byte)40, ProgrammerValue(context, 2, ChannelType.ColorRed));
        Assert.Equal((byte)40, ProgrammerValue(context, 2, ChannelType.ColorGreen));
        Assert.Equal((byte)60, ProgrammerValue(context, 3, ChannelType.ColorRed));
        Assert.Equal((byte)60, ProgrammerValue(context, 3, ChannelType.ColorGreen));
    }

    [Fact]
    public void PanAt20Thru60_SkipsIncompatibleFixture_WithoutCorruptingFixtureOrderValuesForTheRest()
    {
        // Fixture 2 has no Pan - the distribution path (1,2,3) must still assign 20/-/60 to
        // fixtures 1 and 3 (their ORIGINAL positions in a 3-fixture path), never re-compact to a
        // 2-fixture 20/60 path across just the two Pan-capable fixtures.
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), ColorOnlyPar(), MovingHeadColor());
        Select(context, 1, 2, 3);

        var composer = new CommandComposer(context);
        var final = RunToEnter(composer, ParameterAt(ChannelType.Pan, 20, 60).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());
        var result = dispatcher.Dispatch(final.ReadyOperation!);

        Assert.True(result.Success);
        Assert.Equal((byte)20, ProgrammerValue(context, 1, ChannelType.Pan));
        Assert.Null(ProgrammerValue(context, 2, ChannelType.Pan)); // no Pan channel at all
        Assert.Equal((byte)60, ProgrammerValue(context, 3, ChannelType.Pan));
    }

    [Fact]
    public void ExistingMultiPointThruDistribution_WorksUnchangedThroughParameterAt()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor(), MovingHeadColor(), MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2, 3, 4, 5);

        var composer = new CommandComposer(context);
        var final = RunToEnter(composer, ParameterAt(ChannelType.ColorRed, 20, 60, 30).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());
        dispatcher.Dispatch(final.ReadyOperation!);

        // Same asymmetric 20->60->30 path CommandComposerValueDistributionTests already proves.
        Assert.Equal((byte)20, ProgrammerValue(context, 1, ChannelType.ColorRed));
        Assert.Equal((byte)40, ProgrammerValue(context, 2, ChannelType.ColorRed));
        Assert.Equal((byte)60, ProgrammerValue(context, 3, ChannelType.ColorRed));
        Assert.Equal((byte)45, ProgrammerValue(context, 4, ChannelType.ColorRed));
        Assert.Equal((byte)30, ProgrammerValue(context, 5, ChannelType.ColorRed));
    }

    [Fact]
    public void RedPlusGreenAt20Thru60_IsNotAFlattenedSixSlotDistribution()
    {
        // Negative proof: a flattened (fixture,parameter) distribution across 6 slots would give
        // fixture 1 RED=20 but GREEN would be the SECOND slot's value (28), not 20 again. Assert
        // the actual (correct) per-fixture-once semantics directly contradicts that alternative.
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2, 3);

        var composer = new CommandComposer(context);
        var tokens = new List<CommandToken>
        {
            CommandToken.Parameter(ChannelType.ColorRed), CommandToken.Simple(CommandTokenKind.Plus), CommandToken.Parameter(ChannelType.ColorGreen),
            CommandToken.Simple(CommandTokenKind.At), CommandToken.Number(20), CommandToken.Simple(CommandTokenKind.Thru), CommandToken.Number(60),
            CommandToken.Simple(CommandTokenKind.Enter),
        };
        dispatcher.Dispatch(RunToEnter(composer, tokens.ToArray()).ReadyOperation!);

        byte flattenedSecondSlotValue = 28; // what a wrong 6-slot linear distribution would produce for fixture 1's GREEN
        Assert.NotEqual(flattenedSecondSlotValue, ProgrammerValue(context, 1, ChannelType.ColorGreen));
        Assert.Equal(ProgrammerValue(context, 1, ChannelType.ColorRed), ProgrammerValue(context, 1, ChannelType.ColorGreen));
    }

    // =========================================================================================
    // Grammar / UI state (19-22)
    // =========================================================================================

    [Fact]
    public void ExplicitRedAt40Enter_WorksEndToEnd()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Parameter(ChannelType.ColorRed));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(40));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        Assert.Null(final.Error);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Equal((byte)40, ProgrammerValue(context, 1, ChannelType.ColorRed));
    }

    [Fact]
    public void EncoderSelectedParameter_NoParameterTokenOnLine_At40Enter_ResolvesFromParameterSelection()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        context.ParameterSelection.Select(ChannelType.ColorRed); // as if armed via the Encoder Drawer

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(40));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Equal((byte)40, ProgrammerValue(context, 1, ChannelType.ColorRed));
        Assert.Null(ProgrammerValue(context, 1, ChannelType.Dimmer)); // legacy Intensity AT never ran
    }

    [Fact]
    public void EmptyParameterSelection_PreservesExactLegacyAtBehavior_Regression()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        Assert.True(context.ParameterSelection.IsEmpty);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(50));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        byte expectedIntensity = (byte)Math.Round(50.0 / 100.0 * 255.0);
        Assert.Equal(expectedIntensity, ProgrammerValue(context, 1, ChannelType.Dimmer));
        Assert.Null(ProgrammerValue(context, 1, ChannelType.ColorRed));
    }

    [Fact]
    public void UnsupportedLeadingThruForm_StillFailsHonestly_NoNewSemantics()
    {
        var (_, context, _, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Parameter(ChannelType.ColorRed));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru)); // no leading number - malformed
        var final = composer.Push(CommandToken.Number(60));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Null(final.ReadyOperation);
        Assert.Null(ProgrammerValue(context, 1, ChannelType.ColorRed));
    }

    // =========================================================================================
    // Undo / atomicity (23-25)
    // =========================================================================================

    [Fact]
    public void FixedParameterAt_IsOneUndoTransaction()
    {
        var (_, context, dispatcher, undoRedo) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterAt(ChannelType.ColorRed, 40).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());
        dispatcher.Dispatch(final.ReadyOperation!);
        Assert.Equal((byte)40, ProgrammerValue(context, 1, ChannelType.ColorRed));

        undoRedo.Undo(); // exactly one Undo() call
        Assert.Null(ProgrammerValue(context, 1, ChannelType.ColorRed));
        Assert.False(undoRedo.CanUndo); // nothing left to undo - it really was one transaction
    }

    [Fact]
    public void DistributedParameterAt_IsOneUndoTransaction()
    {
        var (_, context, dispatcher, undoRedo) = BuildConsole(MovingHeadColor(), MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2, 3);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterAt(ChannelType.ColorRed, 20, 60).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());
        dispatcher.Dispatch(final.ReadyOperation!);

        undoRedo.Undo();
        Assert.Null(ProgrammerValue(context, 1, ChannelType.ColorRed));
        Assert.Null(ProgrammerValue(context, 2, ChannelType.ColorRed));
        Assert.Null(ProgrammerValue(context, 3, ChannelType.ColorRed));
        Assert.False(undoRedo.CanUndo);
    }

    [Fact]
    public void MultiParameterAt_IsOneUndoTransaction()
    {
        var (_, context, dispatcher, undoRedo) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var tokens = new List<CommandToken>
        {
            CommandToken.Parameter(ChannelType.ColorRed), CommandToken.Simple(CommandTokenKind.Plus), CommandToken.Parameter(ChannelType.ColorGreen),
            CommandToken.Simple(CommandTokenKind.At), CommandToken.Number(40), CommandToken.Simple(CommandTokenKind.Enter),
        };
        dispatcher.Dispatch(RunToEnter(composer, tokens.ToArray()).ReadyOperation!);

        undoRedo.Undo();
        Assert.Null(ProgrammerValue(context, 1, ChannelType.ColorRed));
        Assert.Null(ProgrammerValue(context, 1, ChannelType.ColorGreen));
        Assert.False(undoRedo.CanUndo);
    }

    // =========================================================================================
    // Regression (26-30) - RELEASE/HOME/ParameterSelection UI/legacy AT/distribution untouched.
    // (26/27 are CommandComposerValueDistributionTests and the existing AT tests themselves,
    // unmodified by this slice - see that file. Listed here as an explicit index only.)
    // =========================================================================================

    [Fact]
    public void Release_StillReleasesTheWholeParameter_Regression()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var fixture = context.Patch.FindByNumber(1)!;
        context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!), 100);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Parameter(ChannelType.Pan));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Release));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Null(ProgrammerValue(context, 1, ChannelType.Pan));
    }

    [Fact]
    public void Home_StillWorksAgainstWholeFixture_Regression()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var fixture = context.Patch.FindByNumber(1)!;
        context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!), 100);

        var composer = new CommandComposer(context);
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Home));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Equal((byte)0, ProgrammerValue(context, 1, ChannelType.Pan)); // FixtureChannel.DefaultValue (0)
    }

    [Fact]
    public void ParameterSelectionState_UnaffectedByParameterAt_Regression()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        context.ParameterSelection.Select(ChannelType.Pan);

        var composer = new CommandComposer(context);
        // Explicit-parameter AT (RED), independent of whatever ParameterSelection already holds.
        var final = RunToEnter(composer, ParameterAt(ChannelType.ColorRed, 40).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(new[] { ChannelType.Pan }, context.ParameterSelection.Items); // untouched
    }
}

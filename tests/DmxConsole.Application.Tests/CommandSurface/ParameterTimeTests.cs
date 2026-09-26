using DmxConsole.Application.CommandSurface;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Application.Tests.CommandSurface;

/// <summary>
/// Parameter-scoped TIME (CLAUDE.md §16, ROADMAP §9a): "&lt;Parameter&gt; TIME [IN|OUT]
/// &lt;value&gt; [THRU &lt;value&gt;]* ENTER" and the ParameterSelection-driven bare "TIME ...
/// ENTER" fallback. Applies to Current Fixture Selection x Current Parameter Selection via the
/// SAME ParameterTargetResolver + InterpolateAlongPath ParameterAtTests already proves, but writes
/// to Programmer's parallel per-channel TIMING store (SetParameterTimingCommand) instead of VALUES
/// - an independent axis, never touching Programmer values, Cues, or Playback directly (temporary
/// programming state until STORE CUE - see CueListParameterTimingTests for the Store/playback
/// half of this slice).
/// </summary>
public class ParameterTimeTests
{
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

    private static FixtureProfile MoverWithPanFine() => new()
    {
        Id = "test-mover-panfine",
        Manufacturer = "Test",
        Model = "MoverFine",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "2ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 },
                    new FixtureChannel { Name = "PanFine", Type = ChannelType.PanFine, Offset = 1 },
                },
            },
        },
    };

    private static (Patch Patch, ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo) BuildConsole(params FixtureProfile[] profiles)
    {
        var patch = new Patch();
        int address = 1;
        foreach (var profile in profiles)
        {
            patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: address));
            address += profile.Modes[0].Channels.Count + 4;
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

    private static (TimeSpan? In, TimeSpan? Out) ProgrammerTiming(ConsoleContext context, int fixtureNumber, ChannelType type)
    {
        var fixture = context.Patch.FindByNumber(fixtureNumber)!;
        var channel = fixture.FindChannel(type);
        if (channel is null) return (null, null);
        context.Programmer.TryGetTiming(fixture.UniverseId, fixture.AbsoluteIndex(channel), out var timeIn, out var timeOut);
        return (timeIn, timeOut);
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

    private static CommandToken[] ParameterTime(ChannelType parameter, CommandTokenKind? side, params double[] controlPoints)
    {
        var tokens = new List<CommandToken> { CommandToken.Parameter(parameter), CommandToken.Simple(CommandTokenKind.Timing) };
        if (side is { } s) tokens.Add(CommandToken.Simple(s));
        tokens.Add(CommandToken.Number(controlPoints[0]));
        for (int i = 1; i < controlPoints.Length; i++)
        {
            tokens.Add(CommandToken.Simple(CommandTokenKind.Thru));
            tokens.Add(CommandToken.Number(controlPoints[i]));
        }
        tokens.Add(CommandToken.Simple(CommandTokenKind.Enter));
        return tokens.ToArray();
    }

    // =========================================================================================
    // Targeting (7-11)
    // =========================================================================================

    [Fact]
    public void PanFineNormalizesToLogicalPan_ForTimingToo()
    {
        var (_, context, dispatcher, _) = BuildConsole(MoverWithPanFine());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.PanFine, null, 5));
        var result = dispatcher.Dispatch(final.ReadyOperation!);

        Assert.True(result.Success);
        var (timeIn, timeOut) = ProgrammerTiming(context, 1, ChannelType.Pan);
        Assert.Equal(TimeSpan.FromSeconds(5), timeIn);
        Assert.Equal(TimeSpan.FromSeconds(5), timeOut);
    }

    [Fact]
    public void RedTime5_UsesParameterTargetResolver_AffectsOnlyRed()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, null, 5));
        var result = dispatcher.Dispatch(final.ReadyOperation!);

        Assert.True(result.Success);
        Assert.Equal((TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)), ProgrammerTiming(context, 1, ChannelType.ColorRed));
        Assert.Equal((null, (TimeSpan?)null), ProgrammerTiming(context, 1, ChannelType.ColorGreen));
    }

    [Fact]
    public void ParameterTime_IncompatibleFixture_SkippedNotFailed_WithWarning()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), ColorOnlyPar());
        Select(context, 1, 2);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.Pan, null, 5));
        var result = dispatcher.Dispatch(final.ReadyOperation!);

        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(5), ProgrammerTiming(context, 1, ChannelType.Pan).In);
        Assert.Null(ProgrammerTiming(context, 2, ChannelType.Pan).In);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void ParameterTime_LeavesUnselectedFixturesUnchanged()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, null, 5));
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(TimeSpan.FromSeconds(5), ProgrammerTiming(context, 1, ChannelType.ColorRed).In);
        Assert.Null(ProgrammerTiming(context, 2, ChannelType.ColorRed).In);
    }

    [Fact]
    public void ParameterTime_LeavesUnrelatedParametersTimingUnchanged()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var fixture = context.Patch.FindByNumber(1)!;
        context.Programmer.SetTimeIn(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!), TimeSpan.FromSeconds(9));

        var composer = new CommandComposer(context);
        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, null, 5));
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(TimeSpan.FromSeconds(9), ProgrammerTiming(context, 1, ChannelType.Pan).In); // untouched
        Assert.Equal(TimeSpan.FromSeconds(5), ProgrammerTiming(context, 1, ChannelType.ColorRed).In);
    }

    // =========================================================================================
    // Distribution (12-18)
    // =========================================================================================

    [Fact]
    public void TimeOut1Thru5_DistributesByFixtureSelectionOrder()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor(), MovingHeadColor(), MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2, 3, 4, 5);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, CommandTokenKind.Out, 1, 5));
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(TimeSpan.FromSeconds(1), ProgrammerTiming(context, 1, ChannelType.ColorRed).Out);
        Assert.Equal(TimeSpan.FromSeconds(2), ProgrammerTiming(context, 2, ChannelType.ColorRed).Out);
        Assert.Equal(TimeSpan.FromSeconds(3), ProgrammerTiming(context, 3, ChannelType.ColorRed).Out);
        Assert.Equal(TimeSpan.FromSeconds(4), ProgrammerTiming(context, 4, ChannelType.ColorRed).Out);
        Assert.Equal(TimeSpan.FromSeconds(5), ProgrammerTiming(context, 5, ChannelType.ColorRed).Out);
        Assert.Null(ProgrammerTiming(context, 1, ChannelType.ColorRed).In); // TIME OUT never touches TimeIn
    }

    [Fact]
    public void TimeIn1Thru3_Distributes()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2, 3);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, CommandTokenKind.In, 1, 3));
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(TimeSpan.FromSeconds(1), ProgrammerTiming(context, 1, ChannelType.ColorRed).In);
        Assert.Equal(TimeSpan.FromSeconds(2), ProgrammerTiming(context, 2, ChannelType.ColorRed).In);
        Assert.Equal(TimeSpan.FromSeconds(3), ProgrammerTiming(context, 3, ChannelType.ColorRed).In);
        Assert.Null(ProgrammerTiming(context, 1, ChannelType.ColorRed).Out);
    }

    [Fact]
    public void BareTime_Distributes_SettingBothSidesToTheSameInterpolatedValue()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, null, 2, 6));
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal((TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)), ProgrammerTiming(context, 1, ChannelType.ColorRed));
        Assert.Equal((TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(6)), ProgrammerTiming(context, 2, ChannelType.ColorRed));
    }

    /// <summary>THE critical rule (same as Parameter AT's own critical test): a distributed timing
    /// value is computed ONCE per fixture position, then fanned out to every compatible parameter
    /// on that fixture - never a flattened (fixture, parameter) cross-product.</summary>
    [Fact]
    public void RedPlusGreenTime2Thru6_AppliesOneFixtureTimingToBothParameters()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2, 3);
        var composer = new CommandComposer(context);

        var tokens = new List<CommandToken>
        {
            CommandToken.Parameter(ChannelType.ColorRed), CommandToken.Simple(CommandTokenKind.Plus), CommandToken.Parameter(ChannelType.ColorGreen),
            CommandToken.Simple(CommandTokenKind.Timing), CommandToken.Number(2), CommandToken.Simple(CommandTokenKind.Thru), CommandToken.Number(6),
            CommandToken.Simple(CommandTokenKind.Enter),
        };
        var result = dispatcher.Dispatch(RunToEnter(composer, tokens.ToArray()).ReadyOperation!);
        Assert.True(result.Success);

        Assert.Equal(TimeSpan.FromSeconds(2), ProgrammerTiming(context, 1, ChannelType.ColorRed).In);
        Assert.Equal(TimeSpan.FromSeconds(2), ProgrammerTiming(context, 1, ChannelType.ColorGreen).In);
        Assert.Equal(TimeSpan.FromSeconds(4), ProgrammerTiming(context, 2, ChannelType.ColorRed).In);
        Assert.Equal(TimeSpan.FromSeconds(4), ProgrammerTiming(context, 2, ChannelType.ColorGreen).In);
        Assert.Equal(TimeSpan.FromSeconds(6), ProgrammerTiming(context, 3, ChannelType.ColorRed).In);
        Assert.Equal(TimeSpan.FromSeconds(6), ProgrammerTiming(context, 3, ChannelType.ColorGreen).In);
    }

    [Fact]
    public void RedPlusGreenTime2Thru6_IsNotAFlattenedSixSlotDistribution()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2, 3);
        var composer = new CommandComposer(context);

        var tokens = new List<CommandToken>
        {
            CommandToken.Parameter(ChannelType.ColorRed), CommandToken.Simple(CommandTokenKind.Plus), CommandToken.Parameter(ChannelType.ColorGreen),
            CommandToken.Simple(CommandTokenKind.Timing), CommandToken.Number(2), CommandToken.Simple(CommandTokenKind.Thru), CommandToken.Number(6),
            CommandToken.Simple(CommandTokenKind.Enter),
        };
        dispatcher.Dispatch(RunToEnter(composer, tokens.ToArray()).ReadyOperation!);

        var fixture1Red = ProgrammerTiming(context, 1, ChannelType.ColorRed).In;
        var fixture1Green = ProgrammerTiming(context, 1, ChannelType.ColorGreen).In;
        Assert.Equal(fixture1Red, fixture1Green); // both equal to the FIRST control point (2s), never a flattened second slot
    }

    [Fact]
    public void PanTime1Thru5_SkipsIncompatibleFixture_WithoutCorruptingFixtureOrder()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), ColorOnlyPar(), MovingHeadColor());
        Select(context, 1, 2, 3);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.Pan, null, 1, 5));
        var result = dispatcher.Dispatch(final.ReadyOperation!);

        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(1), ProgrammerTiming(context, 1, ChannelType.Pan).In);
        Assert.Null(ProgrammerTiming(context, 2, ChannelType.Pan).In);
        Assert.Equal(TimeSpan.FromSeconds(5), ProgrammerTiming(context, 3, ChannelType.Pan).In);
    }

    [Fact]
    public void MultiPointThru_ReusesExistingDistributionSemantics()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor(), MovingHeadColor(), MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2, 3, 4, 5);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, CommandTokenKind.Out, 20, 60, 30));
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(TimeSpan.FromSeconds(20), ProgrammerTiming(context, 1, ChannelType.ColorRed).Out);
        Assert.Equal(TimeSpan.FromSeconds(40), ProgrammerTiming(context, 2, ChannelType.ColorRed).Out);
        Assert.Equal(TimeSpan.FromSeconds(60), ProgrammerTiming(context, 3, ChannelType.ColorRed).Out);
        Assert.Equal(TimeSpan.FromSeconds(45), ProgrammerTiming(context, 4, ChannelType.ColorRed).Out);
        Assert.Equal(TimeSpan.FromSeconds(30), ProgrammerTiming(context, 5, ChannelType.ColorRed).Out);
    }

    // =========================================================================================
    // Grammar (31-37)
    // =========================================================================================

    [Fact]
    public void RedTime5Enter_WorksEndToEnd()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, null, 5));
        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Equal((TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)), ProgrammerTiming(context, 1, ChannelType.ColorRed));
    }

    [Fact]
    public void RedTimeIn5Enter_SetsOnlyTimeIn()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, CommandTokenKind.In, 5));
        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(5), ProgrammerTiming(context, 1, ChannelType.ColorRed).In);
        Assert.Null(ProgrammerTiming(context, 1, ChannelType.ColorRed).Out);
    }

    [Fact]
    public void RedTimeOut5Enter_SetsOnlyTimeOut()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, CommandTokenKind.Out, 5));
        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Null(ProgrammerTiming(context, 1, ChannelType.ColorRed).In);
        Assert.Equal(TimeSpan.FromSeconds(5), ProgrammerTiming(context, 1, ChannelType.ColorRed).Out);
    }

    [Fact]
    public void RedTimeOut1Thru5Enter_Distributes()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor(), MovingHeadColor(), MovingHeadColor(), MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2, 3, 4, 5);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, CommandTokenKind.Out, 1, 5));
        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(TimeSpan.FromSeconds(3), ProgrammerTiming(context, 3, ChannelType.ColorRed).Out);
    }

    [Fact]
    public void EncoderSelectedRed_BareTimeOut5Enter_ResolvesFromParameterSelection()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        context.ParameterSelection.Select(ChannelType.ColorRed); // as if armed via the Encoder Drawer

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Simple(CommandTokenKind.Out));
        composer.Push(CommandToken.Number(5));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(5), ProgrammerTiming(context, 1, ChannelType.ColorRed).Out);
    }

    [Fact]
    public void BareTime_EmptyParameterSelection_IsAnHonestError_NoLegacyFallback()
    {
        var (_, context, _, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        Assert.True(context.ParameterSelection.IsEmpty);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Number(5));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Null(final.ReadyOperation);
    }

    [Fact]
    public void UnsupportedLeadingThruForm_StillFailsHonestly_NoNewSemantics()
    {
        var (_, context, _, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Parameter(ChannelType.ColorRed));
        composer.Push(CommandToken.Simple(CommandTokenKind.Timing));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru)); // no leading number - malformed
        var final = composer.Push(CommandToken.Number(5));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Null(final.ReadyOperation);
    }

    // =========================================================================================
    // Undo / atomicity (38-40)
    // =========================================================================================

    [Fact]
    public void FixedParameterTime_IsOneUndoTransaction()
    {
        var (_, context, dispatcher, undoRedo) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, null, 5));
        dispatcher.Dispatch(final.ReadyOperation!);
        Assert.Equal(TimeSpan.FromSeconds(5), ProgrammerTiming(context, 1, ChannelType.ColorRed).In);

        undoRedo.Undo();
        Assert.Null(ProgrammerTiming(context, 1, ChannelType.ColorRed).In);
        Assert.False(undoRedo.CanUndo);
    }

    [Fact]
    public void DistributedParameterTime_IsOneUndoTransaction()
    {
        var (_, context, dispatcher, undoRedo) = BuildConsole(MovingHeadColor(), MovingHeadColor(), MovingHeadColor());
        Select(context, 1, 2, 3);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer, ParameterTime(ChannelType.ColorRed, CommandTokenKind.Out, 1, 3));
        dispatcher.Dispatch(final.ReadyOperation!);

        undoRedo.Undo();
        Assert.Null(ProgrammerTiming(context, 1, ChannelType.ColorRed).Out);
        Assert.Null(ProgrammerTiming(context, 2, ChannelType.ColorRed).Out);
        Assert.Null(ProgrammerTiming(context, 3, ChannelType.ColorRed).Out);
        Assert.False(undoRedo.CanUndo);
    }

    [Fact]
    public void MultiParameterTime_IsOneUndoTransaction()
    {
        var (_, context, dispatcher, undoRedo) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        var tokens = new List<CommandToken>
        {
            CommandToken.Parameter(ChannelType.ColorRed), CommandToken.Simple(CommandTokenKind.Plus), CommandToken.Parameter(ChannelType.ColorGreen),
            CommandToken.Simple(CommandTokenKind.Timing), CommandToken.Number(5), CommandToken.Simple(CommandTokenKind.Enter),
        };
        dispatcher.Dispatch(RunToEnter(composer, tokens.ToArray()).ReadyOperation!);

        undoRedo.Undo();
        Assert.Null(ProgrammerTiming(context, 1, ChannelType.ColorRed).In);
        Assert.Null(ProgrammerTiming(context, 1, ChannelType.ColorGreen).In);
        Assert.False(undoRedo.CanUndo);
    }

    // =========================================================================================
    // Regression (41-42)
    // =========================================================================================

    [Fact]
    public void ParameterAt_StillWritesValuesOnly_NeverTiming_Regression()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Parameter(ChannelType.ColorRed));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(40));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Equal((byte)40, ProgrammerValue(context, 1, ChannelType.ColorRed));
        Assert.Equal((null, (TimeSpan?)null), ProgrammerTiming(context, 1, ChannelType.ColorRed)); // AT never touches timing
    }

    [Fact]
    public void Release_StillReleasesTheWholeParameterValue_TimingIndependentlyUntouched_Regression()
    {
        var (_, context, dispatcher, _) = BuildConsole(MovingHeadColor());
        Select(context, 1);
        var fixture = context.Patch.FindByNumber(1)!;
        int idx = fixture.AbsoluteIndex(fixture.FindChannel(ChannelType.Pan)!);
        context.Programmer.SetChannel(fixture.UniverseId, idx, 100);
        context.Programmer.SetTimeIn(fixture.UniverseId, idx, TimeSpan.FromSeconds(3));

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Parameter(ChannelType.Pan));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Release));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Null(ProgrammerValue(context, 1, ChannelType.Pan));
        // RELEASE's existing behavior (unchanged by this slice) uses ClearChannel, which - by
        // design (see ProgrammerTimingTests.ClearChannel_DoesNotTouchTiming) - never touches
        // timing, so the independently-set TimeIn override survives a value RELEASE.
        Assert.Equal(TimeSpan.FromSeconds(3), ProgrammerTiming(context, 1, ChannelType.Pan).In);
    }
}

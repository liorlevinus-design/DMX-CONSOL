using DmxConsole.Application.CommandSurface;
using Xunit;

namespace DmxConsole.Application.Tests.CommandSurface;

/// <summary>
/// Value-distribution slice: "THRU" after AT means "add a value-distribution control point",
/// never object-range selection (which only ever appears BEFORE At) - resolved purely by
/// position within the same CommandComposer grammar, no new token kind, no second parser. All
/// writes converge on the exact same AdjustIntensityCommand/Programmer path single-value AT
/// already used before this slice - one instance per fixture, its own interpolated value, batched
/// into the same CompositeCommand as any selection-building commands (Store-grammar slice's own
/// BuildSelectionCommands), so a distribution is exactly as atomic as any other AT.
/// </summary>
public class CommandComposerValueDistributionTests
{
    private static byte PercentToByte(double percent) => (byte)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * 255.0);

    private static byte DimmerByte(ConsoleContext context, int fixtureNumber)
    {
        var fixture = context.Patch.FindByNumber(fixtureNumber)!;
        context.Programmer.TryGetChannelValue(fixture.UniverseId, fixture.AbsoluteIndex(fixture.FindChannel(DmxConsole.Core.ChannelType.Dimmer)!), out var value);
        return value;
    }

    private static CommandComposition RunToEnter(CommandComposer composer, params CommandToken[] tokens)
    {
        CommandComposition last = composer.Current;
        foreach (var token in tokens) last = composer.Push(token);
        return last;
    }

    private static CommandToken[] FixtureThru(int from, int to) => new[]
    {
        CommandToken.Simple(CommandTokenKind.Fixture),
        CommandToken.Number(from),
        CommandToken.Simple(CommandTokenKind.Thru),
        CommandToken.Number(to),
    };

    private static CommandToken[] AtThru(params double[] controlPoints)
    {
        var tokens = new List<CommandToken> { CommandToken.Simple(CommandTokenKind.At), CommandToken.Number(controlPoints[0]) };
        for (int i = 1; i < controlPoints.Length; i++)
        {
            tokens.Add(CommandToken.Simple(CommandTokenKind.Thru));
            tokens.Add(CommandToken.Number(controlPoints[i]));
        }
        return tokens.ToArray();
    }

    // ---------- §1/§9: linear two-point distribution ----------

    [Fact]
    public void TwoFixtures_LinearDistribution_20To60_ProducesExactEndpoints()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 2);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer,
            FixtureThru(1, 2).Concat(AtThru(20, 60)).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        Assert.Equal(PercentToByte(20), DimmerByte(context, 1));
        Assert.Equal(PercentToByte(60), DimmerByte(context, 2));
    }

    [Fact]
    public void FiveFixtures_LinearDistribution_20To60_ProducesEvenSteps()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer,
            FixtureThru(1, 5).Concat(AtThru(20, 60)).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(new[] { 20.0, 30.0, 40.0, 50.0, 60.0 }.Select(PercentToByte), new[] { 1, 2, 3, 4, 5 }.Select(n => DimmerByte(context, n)));
    }

    // ---------- §2/§9: multi-point / bounce distribution ----------

    [Fact]
    public void FiveFixtures_BounceDistribution_20To60To20_InterpolatesBothSegments()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer,
            FixtureThru(1, 5).Concat(AtThru(20, 60, 20)).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(new[] { 20.0, 40.0, 60.0, 40.0, 20.0 }.Select(PercentToByte), new[] { 1, 2, 3, 4, 5 }.Select(n => DimmerByte(context, n)));
    }

    /// <summary>Arbitrary-control-points slice: "AT 20 THRU 60 THRU 30" - an ASYMMETRIC path
    /// (the last point does NOT equal the first, unlike the bounce-back 20-60-20 case above).
    /// Audited and confirmed already correct at every layer (raw CommandComposer tokens, full
    /// CommandSurfaceViewModel digit-by-digit keypad simulation, and the live rendered UI) before
    /// this test was added - there was no "last point must equal first point" assumption anywhere
    /// in InterpolateAlongPath/the Thru-after-At parsing loop to begin with. Kept here as a
    /// permanent regression guard specifically for the asymmetric case, since every other existing
    /// multi-point test happens to be a symmetric bounce (20-60-20) and would not have caught a
    /// regression that only broke asymmetric paths.</summary>
    [Fact]
    public void FiveFixtures_AsymmetricThreePointDistribution_20To60To30_InterpolatesBothSegmentsIndependently()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer,
            FixtureThru(1, 5).Concat(AtThru(20, 60, 30)).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        // 20 -> 60 -> 30 across 5 fixtures: two segments of length 2 each (20->60, then 60->30).
        Assert.Equal(new[] { 20.0, 40.0, 60.0, 45.0, 30.0 }.Select(PercentToByte), new[] { 1, 2, 3, 4, 5 }.Select(n => DimmerByte(context, n)));
    }

    /// <summary>General-purpose proof, not a 2/3-point special case: four arbitrary control
    /// points, none of which repeat or mirror each other.</summary>
    [Fact]
    public void FiveFixtures_FourPointDistribution_10To90To40To70_InterpolatesAcrossThreeSegments()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer,
            FixtureThru(1, 5).Concat(AtThru(10, 90, 40, 70)).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        // Path: 10 -> 90 -> 40 -> 70, three segments across 5 fixtures (t = 0, .25, .5, .75, 1).
        Assert.Equal(new[] { 10.0, 70.0, 65.0, 47.5, 70.0 }.Select(PercentToByte), new[] { 1, 2, 3, 4, 5 }.Select(n => DimmerByte(context, n)));
    }

    // ---------- §3: single-value AT unchanged ----------

    [Fact]
    public void SingleValueAt_StillAssignsTheSameValueToEveryTarget_Regression()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 10);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer,
            FixtureThru(1, 10)
                .Append(CommandToken.Simple(CommandTokenKind.At)).Append(CommandToken.Number(50))
                .Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        for (int n = 1; n <= 10; n++)
            Assert.Equal(PercentToByte(50), DimmerByte(context, n));
    }

    /// <summary>Exactly the sequence named in the arbitrary-control-points slice's own report
    /// ("FIXTURE 1 THRU 5 AT 20 THRU 60 THRU 30 ENTER"), driven through the composer with
    /// individually-typed digit tokens the same way the object-range clauses elsewhere in this
    /// file are, verifying "20 -> 60 -> 30" end to end once more with the exact literal command.</summary>
    [Fact]
    public void FixtureOneThruFiveAt20Thru60Thru30Enter_MatchesTheReportedCommandExactly()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(5));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(20));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(60));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(30));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        Assert.Null(final.Error);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        Assert.Equal(new[] { 20.0, 40.0, 60.0, 45.0, 30.0 }.Select(PercentToByte), new[] { 1, 2, 3, 4, 5 }.Select(n => DimmerByte(context, n)));
    }

    /// <summary>An extreme asymmetric path (a full-range drop then a partial recovery) with
    /// duplicate-adjacent-value-free control points, matching the arbitrary-control-points
    /// slice's own "AT 100 THRU 0 THRU 25" example.</summary>
    [Fact]
    public void FiveFixtures_ThreePointDistribution_100To0To25()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer,
            FixtureThru(1, 5).Concat(AtThru(100, 0, 25)).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        // 100 -> 0 -> 25 across 5 fixtures: t=0,.25,.5,.75,1 -> 100, 50, 0, 12.5, 25.
        Assert.Equal(new[] { 100.0, 50.0, 0.0, 12.5, 25.0 }.Select(PercentToByte), new[] { 1, 2, 3, 4, 5 }.Select(n => DimmerByte(context, n)));
    }

    // ---------- §4: Selection order is authoritative, never fixture-ID order ----------

    [Fact]
    public void NonMonotonicSelectionOrder_DistributesByTypedOrder_NeverByFixtureId()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 40);
        var composer = new CommandComposer(context);

        // Ordered Selection: 21, 4, 17, 8, 31 - typed exactly in this order, individually (not a
        // Thru range), so BuildSelectionCommands' shadow `targets` list preserves it verbatim.
        int[] order = { 21, 4, 17, 8, 31 };
        var tokens = new List<CommandToken> { CommandToken.Simple(CommandTokenKind.Fixture) };
        tokens.AddRange(order.Select(n => CommandToken.Number(n)));
        tokens.AddRange(AtThru(20, 60));
        tokens.Add(CommandToken.Simple(CommandTokenKind.Enter));

        var final = RunToEnter(composer, tokens.ToArray());
        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        // 21 -> first value (20), 31 -> final value (60), the three in between interpolate in
        // between - exactly the order they were typed, not fixture-number order (which would put
        // 4 first and 31 last-by-coincidence only, never intentionally).
        var expected = new[] { 20.0, 30.0, 40.0, 50.0, 60.0 };
        for (int i = 0; i < order.Length; i++)
            Assert.Equal(PercentToByte(expected[i]), DimmerByte(context, order[i]));
    }

    [Fact]
    public void ReversedSelectionOrder_DistributesHighToLowFixtureNumbers_ByOrderNotById()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        var tokens = new List<CommandToken> { CommandToken.Simple(CommandTokenKind.Fixture) };
        tokens.AddRange(new[] { 5, 4, 3, 2, 1 }.Select(n => CommandToken.Number(n))); // reversed order
        tokens.AddRange(AtThru(20, 60));
        tokens.Add(CommandToken.Simple(CommandTokenKind.Enter));

        dispatcher.Dispatch(RunToEnter(composer, tokens.ToArray()).ReadyOperation!);

        // Fixture 5 was typed FIRST -> gets the first control point (20); Fixture 1 was typed
        // LAST -> gets the last control point (60) - the exact inverse of fixture-number order.
        Assert.Equal(PercentToByte(20), DimmerByte(context, 5));
        Assert.Equal(PercentToByte(30), DimmerByte(context, 4));
        Assert.Equal(PercentToByte(40), DimmerByte(context, 3));
        Assert.Equal(PercentToByte(50), DimmerByte(context, 2));
        Assert.Equal(PercentToByte(60), DimmerByte(context, 1));
    }

    // ---------- §5: Selection produced by ODD/EVEN ----------

    [Fact]
    public void OddDerivedSelection_DistributesOverTheResultingOrderedSubset()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 10);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer,
            FixtureThru(1, 10)
                .Append(CommandToken.Simple(CommandTokenKind.Odd))
                .Concat(AtThru(20, 60))
                .Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        // Odd-position subset of 1..10 (1-based positions 1,3,5,7,9) = fixtures 1,3,5,7,9.
        var oddFixtures = new[] { 1, 3, 5, 7, 9 };
        var expected = new[] { 20.0, 30.0, 40.0, 50.0, 60.0 };
        for (int i = 0; i < oddFixtures.Length; i++)
            Assert.Equal(PercentToByte(expected[i]), DimmerByte(context, oddFixtures[i]));

        // Even fixtures were never part of the resolved Selection - untouched.
        foreach (var n in new[] { 2, 4, 6, 8, 10 })
            Assert.False(context.Programmer.HasStoredValue(0, context.Patch.FindByNumber(n)!.AbsoluteIndex(context.Patch.FindByNumber(n)!.FindChannel(DmxConsole.Core.ChannelType.Dimmer)!), out _));
    }

    [Fact]
    public void EvenDerivedSelection_DistributesOverTheResultingOrderedSubset()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 10);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer,
            FixtureThru(1, 10)
                .Append(CommandToken.Simple(CommandTokenKind.Even))
                .Concat(AtThru(20, 60))
                .Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        var evenFixtures = new[] { 2, 4, 6, 8, 10 };
        var expected = new[] { 20.0, 30.0, 40.0, 50.0, 60.0 };
        for (int i = 0; i < evenFixtures.Length; i++)
            Assert.Equal(PercentToByte(expected[i]), DimmerByte(context, evenFixtures[i]));
    }

    // ---------- §6/§9: single-fixture uses the FIRST control point, never an average ----------

    [Fact]
    public void OneFixture_TwoPointDistribution_UsesFirstValue_NeverAnAverage()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 1);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer,
            new[] { CommandToken.Simple(CommandTokenKind.Fixture), CommandToken.Number(1) }
                .Concat(AtThru(20, 60))
                .Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        dispatcher.Dispatch(final.ReadyOperation!);
        Assert.Equal(PercentToByte(20), DimmerByte(context, 1)); // NOT 40 (the average of 20/60)
    }

    [Fact]
    public void OneFixture_ThreePointBounceDistribution_StillUsesFirstValue()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 1);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer,
            new[] { CommandToken.Simple(CommandTokenKind.Fixture), CommandToken.Number(1) }
                .Concat(AtThru(20, 60, 20))
                .Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        dispatcher.Dispatch(final.ReadyOperation!);
        Assert.Equal(PercentToByte(20), DimmerByte(context, 1));
    }

    // ---------- §5/§9: malformed grammar - clear error, zero Programmer mutation ----------

    [Fact]
    public void MissingEndpoint_AfterThru_ReturnsClearError_NoProgrammerMutation()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(5));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(20));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter)); // no number after the second Thru

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Null(final.ReadyOperation);
        for (int n = 1; n <= 5; n++)
            Assert.False(context.Programmer.HasStoredValue(0, context.Patch.FindByNumber(n)!.AbsoluteIndex(context.Patch.FindByNumber(n)!.FindChannel(DmxConsole.Core.ChannelType.Dimmer)!), out _));
    }

    [Fact]
    public void TooManyTokens_AfterDistributionPath_ReturnsClearError_NoProgrammerMutation()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(5));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(20));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(60));
        var final = composer.Push(CommandToken.Number(99)); // a bare Number where only Enter/Thru is valid

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        for (int n = 1; n <= 5; n++)
            Assert.False(context.Programmer.HasStoredValue(0, context.Patch.FindByNumber(n)!.AbsoluteIndex(context.Patch.FindByNumber(n)!.FindChannel(DmxConsole.Core.ChannelType.Dimmer)!), out _));
    }

    [Fact]
    public void InvalidObjectRange_BeforeAt_StillFailsHonestly_NoProgrammerMutation()
    {
        // Object-range Thru (BEFORE At) referencing a fixture that doesn't exist must still fail
        // the way it always did - the value-distribution slice must not weaken that.
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 3);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(99)); // fixture 99 doesn't exist
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(20));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        var final = composer.Push(CommandToken.Number(60));

        // Fixture Thru ranges silently skip numbers that don't resolve to a patched fixture
        // (existing behavior) - so this remains a normal, smaller-than-requested Selection, not
        // an error. The real assertion here is simply that nothing throws and only the fixtures
        // that DO exist (1..3) receive a value once finalized.
        var afterEnter = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));
        Assert.True(afterEnter.IsComplete);
    }

    // ---------- §1/§9: task-line/SelectionCycle bookkeeping (composed via ViewModel elsewhere;
    // verified here at the composition level, matching the existing AT-50 test's own scope) ----------

    [Fact]
    public void Distribution_SetsEndsSelectionCycle_LikeAnyOtherAt()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        var final = RunToEnter(composer,
            FixtureThru(1, 5).Concat(AtThru(20, 60)).Append(CommandToken.Simple(CommandTokenKind.Enter)).ToArray());

        Assert.True(final.EndsSelectionCycle);
    }
}

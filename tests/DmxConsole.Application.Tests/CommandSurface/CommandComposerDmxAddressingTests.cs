using DmxConsole.Application.CommandSurface;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Tests.CommandSurface;

/// <summary>DMX DIRECT ADDRESSING - the composer-level grammar: "DMX &lt;Universe.Address&gt;
/// (THRU &lt;Universe.Address&gt;)? (AT number | FULL | RELEASE)?". These tests push a
/// CommandToken.DmxAddress directly (the same token CommandSurfaceViewModel's dot-parsing
/// produces - see CommandSurfaceViewModelDmxTests for that parsing itself), since the composer's
/// grammar doesn't care how the token was assembled, only what it contains.</summary>
public class CommandComposerDmxAddressingTests
{
    private static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo) BuildRig()
    {
        var patch = new Patch();
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        return (context, dispatcher, undoRedo);
    }

    // 1: DMX 1.1 AT 50 ENTER writes the correct address/value.
    [Fact]
    public void DmxAtValue_WritesCorrectAddressAndValue()
    {
        var (context, dispatcher, _) = BuildRig();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(1, 1));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        var beforeEnter = composer.Push(CommandToken.Number(50));
        Assert.False(beforeEnter.IsComplete); // ends in a numeric token - needs ENTER (§14)

        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));
        Assert.True(final.IsComplete);

        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        // Operator Universe 1 -> internal universeId 0; Address 1 -> channelIndex 0.
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal((byte)Math.Round(50 / 100.0 * 255.0), value);
    }

    // 2: DMX 1.1 AT FULL writes 100% (byte 255), self-terminating - no ENTER needed.
    [Fact]
    public void DmxAtFull_Writes100Percent_SelfTerminates()
    {
        var (context, dispatcher, _) = BuildRig();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(1, 1));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Full));

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal((byte)255, value);
    }

    // 3: a DMX range writes every address in the range.
    [Fact]
    public void DmxRange_WritesEveryAddressInRange()
    {
        var (context, dispatcher, _) = BuildRig();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(1, 1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.DmxAddress(1, 12));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(50));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        byte expected = (byte)Math.Round(50 / 100.0 * 255.0);
        for (int addr = 1; addr <= 12; addr++)
        {
            Assert.True(context.Programmer.HasStoredValue(0, addr - 1, out var value));
            Assert.Equal(expected, value);
        }
        Assert.False(context.Programmer.HasStoredValue(0, 12, out _)); // address 13 (index 12) untouched
    }

    // Reverse ranges normalize, same as FIXTURE/GROUP THRU.
    [Fact]
    public void DmxRange_Descending_StillWritesEveryAddress()
    {
        var (context, dispatcher, _) = BuildRig();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(1, 12));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.DmxAddress(1, 1));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Full));

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        for (int addr = 1; addr <= 12; addr++)
            Assert.True(context.Programmer.HasStoredValue(0, addr - 1, out _));
    }

    // 4: invalid address 0 is rejected.
    [Fact]
    public void InvalidAddress_Zero_IsRejected()
    {
        var (context, _, _) = BuildRig();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        var result = composer.Push(CommandToken.DmxAddress(1, 0));

        Assert.False(result.IsComplete);
        Assert.NotNull(result.Error);
        Assert.Null(result.ReadyOperation);
    }

    // 5: invalid address 513 is rejected.
    [Fact]
    public void InvalidAddress_513_IsRejected()
    {
        var (context, _, _) = BuildRig();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        var result = composer.Push(CommandToken.DmxAddress(1, 513));

        Assert.False(result.IsComplete);
        Assert.NotNull(result.Error);
    }

    // 6: an invalid (negative) Universe is rejected honestly.
    [Fact]
    public void InvalidUniverse_Negative_IsRejectedHonestly()
    {
        var (context, _, _) = BuildRig();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        var result = composer.Push(CommandToken.DmxAddress(-1, 1));

        Assert.False(result.IsComplete);
        Assert.NotNull(result.Error);
        Assert.Contains("Universe", result.Error);
    }

    // Operator-facing Universe numbering is 1-based (DMX DIRECT ADDRESSING follow-up §1) -
    // Universe 0 is honestly rejected, not silently translated to some other Universe.
    [Fact]
    public void InvalidUniverse_Zero_IsRejectedHonestly_OperatorNumberingIsOneBased()
    {
        var (context, _, _) = BuildRig();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        var result = composer.Push(CommandToken.DmxAddress(0, 1));

        Assert.False(result.IsComplete);
        Assert.NotNull(result.Error);
        Assert.Contains("Universe", result.Error);
    }

    // 11: Undo restores the previous Editor/address state exactly (both "had a value" and "had none").
    [Fact]
    public void Undo_RestoresPreviousAddressState()
    {
        var (context, dispatcher, undoRedo) = BuildRig();
        context.Programmer.SetChannel(0, 0, 77); // address 1 already had a value

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(1, 1));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(90));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));
        dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var afterSet));
        Assert.NotEqual((byte)77, afterSet);

        var outcome = undoRedo.Undo();

        Assert.True(outcome.Performed);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var restored));
        Assert.Equal((byte)77, restored);
    }

    [Fact]
    public void Undo_RestoresNoValue_WhenAddressHadNoneBefore()
    {
        var (context, dispatcher, undoRedo) = BuildRig();
        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(1, 5));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Full));
        dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(context.Programmer.HasStoredValue(0, 4, out _));

        var outcome = undoRedo.Undo();

        Assert.True(outcome.Performed);
        Assert.False(context.Programmer.HasStoredValue(0, 4, out _));
    }

    // 12: an unpatched address works (Programmer is fixture-agnostic by design).
    [Fact]
    public void UnpatchedAddress_WorksCorrectly_NoFixturePatchedAnywhere()
    {
        var (context, dispatcher, _) = BuildRig();
        Assert.Empty(context.Patch.Fixtures); // nothing patched at all in this rig

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(3, 250));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(60));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.True(context.Programmer.HasStoredValue(2, 249, out var value));
        Assert.Equal((byte)Math.Round(60 / 100.0 * 255.0), value);
    }

    // 12b: an address within a Universe the engine already tracks (because SOME fixture is
    // patched there) is fully supported end-to-end, including real effective output. See the
    // NeverPatchedUniverse_* tests below for the ZERO-patched-fixtures-anywhere case, which the
    // DMX DIRECT ADDRESSING follow-up's IUniverseAllocator now also fully supports (previously a
    // documented architectural gap).
    [Fact]
    public void UnpatchedAddress_InAnAlreadyTrackedUniverse_ReachesRealEffectiveOutput()
    {
        var (context, dispatcher, _) = BuildRig();
        var engine = (DmxOutputEngine)context.EffectiveOutput;
        engine.AddLayer(context.Programmer);
        var profile = new FixtureProfile
        {
            Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
            Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = DmxConsole.Core.ChannelType.Dimmer, Offset = 0 } } } },
        };
        // Patch a fixture at internal universe 0 (operator Universe 1) - this is what makes the
        // Universe exist to the engine even before the IUniverseAllocator follow-up.
        context.Patch.Add(new PatchedFixture(profile, profile.Modes[0], 0, 1));

        // Address 400 in the SAME Universe has no fixture of its own - genuinely unpatched.
        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(1, 400));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Full));
        dispatcher.Dispatch(final.ReadyOperation!);
        engine.Tick();

        Assert.Equal((byte)255, engine.GetEffectiveValue(0, 399));
        Assert.Equal(OwnerKind.Programmer, engine.GetOwner(0, 399)!.Kind);
    }

    // DMX DIRECT ADDRESSING follow-up §2-3: a configured output Universe must exist and reach
    // REAL effective/output state even with ZERO patched fixtures anywhere - the previously
    // documented gap. DmxAddressCommandBase.Execute now calls context.UniverseAllocator?.
    // EnsureUniverse(...) itself before touching the Programmer, so no test-side EnsureUniverse
    // call or patched fixture is needed to make this Universe visible to the engine.
    [Fact]
    public void NeverPatchedUniverse_DmxAtFull_ReachesRealEffectiveOutput()
    {
        var (context, dispatcher, _) = BuildRig();
        var engine = (DmxOutputEngine)context.EffectiveOutput;
        engine.AddLayer(context.Programmer);
        Assert.Empty(context.Patch.Fixtures); // nothing patched anywhere in this rig

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(5, 1)); // operator Universe 5 -> internal universeId 4, never patched
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Full));
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        engine.Tick();

        Assert.Equal((byte)255, engine.GetEffectiveValue(4, 0));
        Assert.Equal(OwnerKind.Programmer, engine.GetOwner(4, 0)!.Kind); // LIVE/DMX diagnostics can inspect it too
        Assert.Empty(context.Patch.Fixtures); // still no dummy fixture / fake patch entry was ever created
    }

    [Fact]
    public void NeverPatchedUniverse_DmxRelease_ReleasesTheValueAndStopsContributing()
    {
        var (context, dispatcher, _) = BuildRig();
        var engine = (DmxOutputEngine)context.EffectiveOutput;
        engine.AddLayer(context.Programmer);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(5, 1));
        dispatcher.Dispatch(composer.Push(CommandToken.Simple(CommandTokenKind.Full)).ReadyOperation!);
        engine.Tick();
        Assert.Equal((byte)255, engine.GetEffectiveValue(4, 0));

        var releaseComposer = new CommandComposer(context);
        releaseComposer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        releaseComposer.Push(CommandToken.DmxAddress(5, 1));
        var releaseFinal = releaseComposer.Push(CommandToken.Simple(CommandTokenKind.Release));
        var releaseResult = dispatcher.Dispatch(releaseFinal.ReadyOperation!);
        Assert.True(releaseResult.Success);
        engine.Tick();

        Assert.False(context.Programmer.HasStoredValue(4, 0, out _));
        Assert.Equal((byte)0, engine.GetEffectiveValue(4, 0));
        Assert.Null(engine.GetOwner(4, 0));
        Assert.Empty(context.Patch.Fixtures);
    }

    [Fact]
    public void NeverPatchedUniverse_Undo_RestoresPreviousState()
    {
        var (context, dispatcher, undoRedo) = BuildRig();
        var engine = (DmxOutputEngine)context.EffectiveOutput;
        engine.AddLayer(context.Programmer);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(5, 1));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Full));
        dispatcher.Dispatch(final.ReadyOperation!);
        engine.Tick();
        Assert.Equal((byte)255, engine.GetEffectiveValue(4, 0));

        var outcome = undoRedo.Undo();
        engine.Tick();

        Assert.True(outcome.Performed);
        Assert.False(context.Programmer.HasStoredValue(4, 0, out _));
        Assert.Equal((byte)0, engine.GetEffectiveValue(4, 0));
        Assert.Empty(context.Patch.Fixtures); // Undo never touches Patch either
    }

    // 13: a DMX range does not silently cross a Universe boundary - honest structured rejection.
    [Fact]
    public void DmxRange_CrossingUniverseBoundary_IsHonestlyRejected_NotSilentlyCrossed()
    {
        var (context, _, _) = BuildRig();
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(1, 500));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        var result = composer.Push(CommandToken.DmxAddress(2, 10));

        Assert.False(result.IsComplete);
        Assert.NotNull(result.Error);
        Assert.Contains("Universe", result.Error);
        Assert.Null(result.ReadyOperation);
    }

    // 14: direct DMX addressing does not mutate Fixture selection.
    [Fact]
    public void DirectDmx_DoesNotMutateFixtureSelection()
    {
        var (context, dispatcher, _) = BuildRig();
        var profile = new FixtureProfile
        {
            Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
            Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = DmxConsole.Core.ChannelType.Dimmer, Offset = 0 } } } },
        };
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(5, 200));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Full));
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Single(context.Selection.Items);
        Assert.Equal(fixture, context.Selection.Items[0]);
    }

    // 15: provenance/Editor ownership is consistent with the existing model - the written
    // address becomes Programmer-owned and visible via the same IEffectiveOutputReader every
    // other LIVE consumer already uses.
    [Fact]
    public void ProvenanceIsConsistentWithExistingModel_ProgrammerOwnsTheWrittenAddress()
    {
        var (context, dispatcher, _) = BuildRig();
        var engine = (DmxOutputEngine)context.EffectiveOutput;
        engine.AddLayer(context.Programmer);
        // DMX DIRECT ADDRESSING follow-up: the DmxAddressCommandBase now allocates the target
        // Universe itself via IUniverseAllocator (see DmxAddressCommandBase.Execute) - no manual
        // EnsureUniverse call needed here anymore, even though nothing is patched anywhere.

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(1, 1)); // operator Universe 1 -> internal universeId 0
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Full));
        dispatcher.Dispatch(final.ReadyOperation!);
        engine.Tick();

        Assert.Equal((byte)255, engine.GetEffectiveValue(0, 0));
        Assert.NotNull(engine.GetOwner(0, 0));
        Assert.Equal(OwnerKind.Programmer, engine.GetOwner(0, 0)!.Kind);
    }

    // DMX RELEASE - address-level release fits the existing Programmer model cleanly.
    [Fact]
    public void DmxRelease_ReleasesTheStoredValue_SelfTerminates()
    {
        var (context, dispatcher, _) = BuildRig();
        context.Programmer.SetChannel(0, 0, 100);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(1, 1));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Release));

        Assert.True(final.IsComplete); // self-terminates, like FAMILY RELEASE
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.False(context.Programmer.HasStoredValue(0, 0, out _));
    }

    [Fact]
    public void DmxRelease_Range_ReleasesEveryAddressInRange()
    {
        var (context, dispatcher, _) = BuildRig();
        for (int addr = 1; addr <= 10; addr++) context.Programmer.SetChannel(0, addr - 1, 100);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(1, 1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.DmxAddress(1, 10));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Release));

        dispatcher.Dispatch(final.ReadyOperation!);

        for (int addr = 1; addr <= 10; addr++)
            Assert.False(context.Programmer.HasStoredValue(0, addr - 1, out _));
    }

    // Object/domain context resets after the command completes - the NEXT bare numeric command
    // defaults back to FIXTURE, never staying "sticky" in DMX mode (§7).
    [Fact]
    public void AfterDmxCommandCompletes_NextBareNumericCommand_DefaultsBackToFixture()
    {
        var (context, dispatcher, _) = BuildRig();
        var profile = new FixtureProfile
        {
            Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
            Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = DmxConsole.Core.ChannelType.Dimmer, Offset = 0 } } } },
        };
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 20) { Number = 7 };
        context.Patch.Add(fixture);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Dmx));
        composer.Push(CommandToken.DmxAddress(1, 1));
        var dmxFinal = composer.Push(CommandToken.Simple(CommandTokenKind.Full));
        dispatcher.Dispatch(dmxFinal.ReadyOperation!);
        composer.Reset(); // same reset CommandSurfaceViewModel performs after every successful dispatch

        composer.Push(CommandToken.Number(7));
        var fixtureFinal = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(fixtureFinal.IsComplete);
        Assert.Equal("7", fixtureFinal.PreviewText); // resolved as bare FIXTURE 7, not DMX
        var result = dispatcher.Dispatch(fixtureFinal.ReadyOperation!);
        Assert.True(result.Success);
        Assert.Contains(fixture, context.Selection.Items);
    }
}

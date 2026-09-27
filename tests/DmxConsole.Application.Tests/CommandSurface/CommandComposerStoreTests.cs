using DmxConsole.Application.CommandSurface;
using DmxConsole.Application.Commands;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using Xunit;

namespace DmxConsole.Application.Tests.CommandSurface;

/// <summary>
/// Store-grammar slice: STORE is real CommandComposer grammar - "&lt;selection/context&gt; STORE
/// &lt;target&gt; &lt;number&gt; ENTER" - never a ViewModel-to-ViewModel shortcut, never a silent
/// default to GROUP, and never an auto-assigned number. Group/Cue/Preset Store all converge on
/// shared Application operations (StoreGroupCommand/StoreCueCommand/StorePresetCommand), assembled
/// by CommandComposer exactly like every other grammar branch - the composer builds, the caller
/// dispatches.
/// </summary>
public class CommandComposerStoreTests
{
    /// <summary>A fixture profile with channels across every family this slice's tests need:
    /// Dimmer (Intensity), Pan (Position), Red/Green/Blue (Color), Shutter (Shape).</summary>
    private static FixtureProfile MultiFamilyProfile() => new()
    {
        Id = "multi-family", Manufacturer = "Test", Model = "Multi",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "5ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 },
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 1 },
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 2 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 3 },
                    new FixtureChannel { Name = "Shutter", Type = ChannelType.Shutter, Offset = 4 },
                },
            },
        },
    };

    private static (ConsoleContext Context, CommandDispatcher Dispatcher) BuildMultiFamilyRig(int fixtureCount = 1)
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 0);
        var profile = MultiFamilyProfile();
        for (int i = 1; i <= fixtureCount; i++)
            context.Patch.Add(new PatchedFixture(profile, profile.Modes[0], 0, (i - 1) * 5 + 1) { Number = i });
        return (context, dispatcher);
    }

    private static void TouchFamily(ConsoleContext context, PatchedFixture fixture, ChannelType channelType, byte value)
    {
        var channel = fixture.FindChannel(channelType)!;
        context.Programmer.SetChannel(fixture.UniverseId, fixture.AbsoluteIndex(channel), value);
    }

    // ---------- §A/§B: STORE alone never defaults, appears in the task line, waits for a target ----------

    [Fact]
    public void StoreAlone_IsIncomplete_NeverDefaultsToAnyTarget()
    {
        var (context, _) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        context.Selection.Add(fixture);

        var composer = new CommandComposer(context);
        var composition = composer.Push(CommandToken.Simple(CommandTokenKind.Store));

        Assert.False(composition.IsComplete);
        Assert.Null(composition.ReadyOperation);
        Assert.Null(composition.ReadyAction);
        Assert.Null(composition.PendingStoreChoice);
        Assert.Equal("Store", composition.PreviewText);
    }

    [Fact]
    public void Store_WithNoTarget_AtEnter_FailsWithStoreTargetMissing_NoMutation()
    {
        var (context, dispatcher) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        context.Selection.Add(fixture);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.Equal("STORE TARGET IS MISSING", final.Error);
        Assert.Empty(context.Groups.Groups);
    }

    // ---------- §A/§D: STORE GROUP - Selection-based, order preserved, ODD/EVEN filter Selection order ----------

    [Fact]
    public void FixtureThruStoreGroup_CreatesGroupFromTheOrderedSelection()
    {
        var (context, dispatcher) = BuildMultiFamilyRig(fixtureCount: 10);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(10));
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Group));
        composer.Push(CommandToken.Number(1));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        Assert.NotNull(final.ReadyOperation);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        var group = Assert.Single(context.Groups.Groups);
        Assert.Equal(1, group.Number);
        Assert.Equal(Enumerable.Range(1, 10), group.Fixtures.Select(f => f.Number));
    }

    [Fact]
    public void FixtureThruOddStoreGroup_StoresOnlyOddPositionMembers_SelectionOrderPreserved()
    {
        // §D: ODD/EVEN operate on SELECTION ORDER, not fixture numbers/IDs - proven here by
        // selecting a reversed-ish set first isn't needed, but the key assertion is that the
        // group ends up with exactly the odd 1-based POSITIONS of the resolved selection (1,3,5,7,9),
        // not e.g. "every even-numbered fixture" or any ID-based filter.
        var (context, dispatcher) = BuildMultiFamilyRig(fixtureCount: 10);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(10));
        composer.Push(CommandToken.Simple(CommandTokenKind.Odd));
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Group));
        composer.Push(CommandToken.Number(5));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        var group = context.Groups.FindByNumber(5);
        Assert.NotNull(group);
        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, group!.Fixtures.Select(f => f.Number));

        // The dispatched transaction also actually left the console Selection in that same
        // odd-filtered state - proving ODD really ran as part of the SAME one transaction, not
        // just informed a shadow calculation.
        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, context.Selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void StoreGroup_WithoutANumber_AtEnter_FailsWithGroupNumberMissing_NoMutation()
    {
        var (context, dispatcher) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        context.Selection.Add(fixture);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Group));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.Equal("GROUP NUMBER IS MISSING", final.Error);
        Assert.Empty(context.Groups.Groups);
    }

    [Fact]
    public void StoreGroup_BeforeEnter_IsPending_NotAnError()
    {
        var (context, _) = BuildMultiFamilyRig();
        context.Selection.Add(context.Patch.Fixtures.Single());

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        var pending = composer.Push(CommandToken.Simple(CommandTokenKind.Group));

        Assert.False(pending.IsComplete);
        Assert.Null(pending.Error);
        Assert.Contains(CommandTokenKind.Number, pending.ExpectedNext);
    }

    // ---------- §A: STORE CUE ----------

    [Fact]
    public void StoreCue_CreatesACue_ThroughARealApplicationCommand_NotADirectCoreCall()
    {
        var (context, dispatcher) = BuildMultiFamilyRig();
        context.PrimaryCueList = new CueList();
        context.Selection.Add(context.Patch.Fixtures.Single());

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        composer.Push(CommandToken.Number(5));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);

        // N1 (CLAUDE.md §5): STORE CUE's ReadyOperation is now always a CompositeCommand of
        // [StoreCueCommand, ClearProgrammerCommand] - never the bare StoreCueCommand - so the
        // Programmer clear rides along atomically with the Store as one Undo entry.
        var composite = Assert.IsType<CompositeCommand>(final.ReadyOperation);
        Assert.Contains(composite.Commands, c => c is Application.Commands.Cues.StoreCueCommand);
        Assert.Contains(composite.Commands, c => c is Application.Commands.Programmer.ClearProgrammerCommand);

        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.NotNull(context.PrimaryCueList!.FindByNumber(5));
    }

    [Fact]
    public void StoreCue_WithoutANumber_AtEnter_FailsWithCueNumberMissing()
    {
        var (context, _) = BuildMultiFamilyRig();
        context.PrimaryCueList = new CueList();
        context.Selection.Add(context.Patch.Fixtures.Single());

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Cue));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.Equal("CUE NUMBER IS MISSING", final.Error);
        Assert.Empty(context.PrimaryCueList!.Cues);
    }

    // ---------- §A/§C: STORE PRESET - explicit family, no dialog ----------

    [Fact]
    public void StorePresetWithExplicitFamily_StoresOnlyThatFamily_NoDialog()
    {
        var (context, dispatcher) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        context.Selection.Add(fixture);
        TouchFamily(context, fixture, ChannelType.Pan, 100);
        TouchFamily(context, fixture, ChannelType.ColorRed, 200); // a second, untouched-by-this-store family

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Preset));
        composer.Push(CommandToken.Simple(CommandTokenKind.Position));
        composer.Push(CommandToken.Number(1));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        Assert.Null(final.PendingStoreChoice);
        Assert.Null(final.PendingStoreConflict);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        Assert.NotNull(context.Presets.FindByNumber(AttributeClass.Position, 1));
        Assert.Null(context.Presets.FindByNumber(AttributeClass.Color, 1)); // Color was NOT stored
    }

    [Fact]
    public void StoreFamilyShorthand_NoPresetKeyword_ResolvesIdenticallyToStorePresetFamily()
    {
        // "STORE COLOR 1 ENTER" (no literal PRESET token) is sugar for "STORE PRESET COLOR 1".
        var (context, dispatcher) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        context.Selection.Add(fixture);
        TouchFamily(context, fixture, ChannelType.ColorRed, 200);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Color));
        composer.Push(CommandToken.Number(1));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.NotNull(context.Presets.FindByNumber(AttributeClass.Color, 1));
    }

    [Theory]
    [InlineData(CommandTokenKind.Color)]
    [InlineData(CommandTokenKind.Position)]
    public void StoreFamily_WithoutANumber_AtEnter_FailsWithPresetNumberMissing_NeverOpensADialog(CommandTokenKind familyToken)
    {
        var (context, _) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        context.Selection.Add(fixture);
        TouchFamily(context, fixture, ChannelType.ColorRed, 200);
        TouchFamily(context, fixture, ChannelType.Pan, 100);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(familyToken));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.Equal("PRESET NUMBER IS MISSING", final.Error);
        Assert.Null(final.PendingStoreChoice);
        Assert.Empty(context.Presets.Presets);
    }

    // ---------- §C: STORE PRESET <n> - ambiguity from touched families ----------

    [Fact]
    public void StorePresetWithNumber_ExactlyOneTouchedFamily_StoresDirectly_NoDialog()
    {
        var (context, dispatcher) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        context.Selection.Add(fixture);
        TouchFamily(context, fixture, ChannelType.Pan, 77);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Preset));
        composer.Push(CommandToken.Number(3));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        Assert.Null(final.PendingStoreChoice);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);
        Assert.NotNull(context.Presets.FindByNumber(AttributeClass.Position, 3));
    }

    [Fact]
    public void StorePresetWithNumber_MultipleTouchedFamilies_OpensFamilyChoiceDialog_NeverGuesses()
    {
        var (context, _) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        context.Selection.Add(fixture);
        TouchFamily(context, fixture, ChannelType.Pan, 77);
        TouchFamily(context, fixture, ChannelType.ColorRed, 200);
        TouchFamily(context, fixture, ChannelType.Shutter, 5);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Preset));
        composer.Push(CommandToken.Number(3));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.Null(final.Error);
        Assert.NotNull(final.PendingStoreChoice);
        Assert.Equal(3, final.PendingStoreChoice!.Number);
        Assert.Equal(new[] { AttributeClass.Position, AttributeClass.Color, AttributeClass.Beam }, final.PendingStoreChoice.TouchedFamilies);
        Assert.Empty(context.Presets.Presets); // nothing mutated while the dialog is pending
    }

    [Fact]
    public void BareStorePreset_AtEnter_AlwaysOpensFullDialog_EvenWithOnlyOneTouchedFamily()
    {
        // §C's explicit special case: bare "STORE PRESET ENTER" always opens the dialog (Number
        // is unknown either way), even when the touched-family set happens to have exactly one
        // entry - unlike "STORE PRESET <n> ENTER", which resolves a single touched family directly.
        var (context, _) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        context.Selection.Add(fixture);
        TouchFamily(context, fixture, ChannelType.Pan, 77);

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Preset));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.PendingStoreChoice);
        Assert.Null(final.PendingStoreChoice!.Number);
        Assert.Equal(new[] { AttributeClass.Position }, final.PendingStoreChoice.TouchedFamilies);
    }

    // ---------- §C: OVERWRITE/UPDATE/CANCEL conflict resolution ----------

    [Fact]
    public void StorePreset_NumberAlreadyExistsForThatFamily_OpensConflictPanel_NoMutation()
    {
        var (context, dispatcher) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        context.Selection.Add(fixture);
        TouchFamily(context, fixture, ChannelType.Pan, 77);

        // Pre-existing Position Preset #1.
        context.Presets.Add(new Core.Presets.Preset { Class = AttributeClass.Position, Number = 1, Name = "Existing" });

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Position));
        var final = composer.Push(CommandToken.Number(1));
        var afterEnter = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(afterEnter.IsComplete);
        Assert.Null(afterEnter.Error);
        Assert.NotNull(afterEnter.PendingStoreConflict);
        Assert.Equal(1, afterEnter.PendingStoreConflict!.Number);
        Assert.Equal(new[] { AttributeClass.Position }, afterEnter.PendingStoreConflict.ConflictingFamilies);

        // Nothing was mutated - Preset #1's original values untouched.
        Assert.Equal("Existing", context.Presets.FindByNumber(AttributeClass.Position, 1)!.Name);
    }

    [Fact]
    public void StorePresetTransactionBuilder_Update_MergesNewValues_PreservesUntouchedOnes()
    {
        var (context, _) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        var existing = new Core.Presets.Preset { Class = AttributeClass.Position, Number = 1, Name = "Existing" };
        existing.Values[ChannelType.Tilt] = 42; // an untouched-by-this-store value that must survive UPDATE
        context.Presets.Add(existing);

        var operation = Application.Commands.Presets.StorePresetTransactionBuilder.BuildTransaction(
            context.Presets, new[] { AttributeClass.Position }, 1, new List<PatchedFixture> { fixture },
            precedingCommands: new List<IConsoleCommand>(), overwrite: false);

        TouchFamily(context, fixture, ChannelType.Pan, 200);
        new CommandDispatcher(context, new UndoRedoService(context)).Dispatch(operation);

        Assert.Equal(42, existing.Values[ChannelType.Tilt]); // preserved (UPDATE merges)
    }

    [Fact]
    public void StorePresetTransactionBuilder_Overwrite_DropsValuesNotPartOfThisStore()
    {
        var (context, _) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        var existing = new Core.Presets.Preset { Class = AttributeClass.Position, Number = 1, Name = "Existing" };
        existing.Values[ChannelType.Tilt] = 42; // must be DROPPED by OVERWRITE
        context.Presets.Add(existing);
        TouchFamily(context, fixture, ChannelType.Pan, 200);

        var operation = Application.Commands.Presets.StorePresetTransactionBuilder.BuildTransaction(
            context.Presets, new[] { AttributeClass.Position }, 1, new List<PatchedFixture> { fixture },
            precedingCommands: new List<IConsoleCommand>(), overwrite: true);
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var result = dispatcher.Dispatch(operation);

        Assert.True(result.Success);
        Assert.False(existing.Values.ContainsKey(ChannelType.Tilt));
        Assert.True(existing.Values.ContainsKey(ChannelType.Pan));
    }

    [Fact]
    public void StorePresetTransactionBuilder_MultiFamily_IsOneAtomicTransaction_OneUndoStep()
    {
        var (context, _) = BuildMultiFamilyRig();
        var fixture = context.Patch.Fixtures.Single();
        TouchFamily(context, fixture, ChannelType.Pan, 77);
        TouchFamily(context, fixture, ChannelType.ColorRed, 200);

        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        var operation = Application.Commands.Presets.StorePresetTransactionBuilder.BuildTransaction(
            context.Presets, new[] { AttributeClass.Position, AttributeClass.Color }, 9,
            new List<PatchedFixture> { fixture }, precedingCommands: new List<IConsoleCommand>(), overwrite: false);

        var result = dispatcher.Dispatch(operation);
        Assert.True(result.Success);
        Assert.NotNull(context.Presets.FindByNumber(AttributeClass.Position, 9));
        Assert.NotNull(context.Presets.FindByNumber(AttributeClass.Color, 9));

        // Both Presets were newly created, so this composite's Undo is Destructive (CLAUDE.md
        // §14: "Destructive Undo requires an explicit confirmed option id from PeekUndo") -
        // CompositeCommand aggregates that risk from its StorePresetCommand children (see its own
        // PrepareUndo doc comment), so a bare Undo() without confirmation must be blocked first.
        var proposal = undoRedo.PeekUndo()!;
        var destructiveId = proposal.Options.Single(o => o.Risk == UndoRisk.Destructive).Id;

        var blocked = undoRedo.Undo(); // no confirmation - must NOT mutate anything
        Assert.False(blocked.Performed);
        Assert.NotNull(context.Presets.FindByNumber(AttributeClass.Position, 9));
        Assert.NotNull(context.Presets.FindByNumber(AttributeClass.Color, 9));

        undoRedo.Undo(destructiveId); // one confirmed Undo() call must revert BOTH Presets - proof it's one transaction
        Assert.Null(context.Presets.FindByNumber(AttributeClass.Position, 9));
        Assert.Null(context.Presets.FindByNumber(AttributeClass.Color, 9));
    }

    // ---------- Malformed grammar never partially mutates ----------

    [Fact]
    public void StoreGroup_WithTrailingJunkAfterTheNumber_IsRejected_NoMutation()
    {
        var (context, _) = BuildMultiFamilyRig();
        context.Selection.Add(context.Patch.Fixtures.Single());

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Group));
        composer.Push(CommandToken.Number(1));
        var final = composer.Push(CommandToken.Number(2)); // a second number - malformed

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Empty(context.Groups.Groups);
    }

    [Fact]
    public void StoreGroup_WithNoResolvedTargets_FailsHonestly_NoMutation()
    {
        var (context, _) = BuildMultiFamilyRig();
        // Deliberately no Selection at all.

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Store));
        composer.Push(CommandToken.Simple(CommandTokenKind.Group));
        composer.Push(CommandToken.Number(1));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.NotNull(final.Error);
        Assert.Empty(context.Groups.Groups);
    }
}

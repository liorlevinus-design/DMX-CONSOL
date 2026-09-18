using System.Collections.ObjectModel;
using DmxConsole.Application;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;
using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>Milestone 1 gesture-fix commit (2026-09-16): the corrected, per-target-snapshot
/// EncoderGesture/EncoderDrawerViewModel.BeginGesture/PreviewGesture/CommitGesture/CancelGesture
/// mechanism - the replacement for the rejected single-shared-byte design. Covers exactly the
/// acceptance criteria the user's review demanded: Preview never touches Undo, a whole gesture
/// commits as one Undo step, Cancel/Undo on a Mixed selection restore each fixture to its own
/// exact prior state (including "had no Programmer value at all" and knockout), selection changes
/// and repatch/removal mid-gesture are safe, and multi-channel (Position/Color-style) gestures
/// commit as a single Undo step across every channel together.</summary>
public class EncoderGestureTests
{
    private static FixtureProfile Dimmer1(byte defaultValue = 0) => new()
    {
        Id = "test-dimmer-" + Guid.NewGuid(), Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = defaultValue } } } },
    };

    private static FixtureProfile PanTilt() => new()
    {
        Id = "test-pantilt-" + Guid.NewGuid(), Manufacturer = "Test", Model = "PanTilt",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "2ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 },
                    new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 1 },
                },
            },
        },
    };

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo, EncoderDrawerViewModel Drawer) Build()
    {
        var patch = new Patch();
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        var programmerVm = new ProgrammerViewModel(context, dispatcher, new ObservableCollection<ChannelFaderViewModel>());
        var drawer = new EncoderDrawerViewModel(dispatcher, programmerVm);
        return (context, dispatcher, undoRedo, drawer);
    }

    [Fact]
    public void PreviewGesture_NeverPushesUndo()
    {
        var (context, _, undoRedo, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);

        var gesture = drawer.BeginGesture(ChannelType.Dimmer);
        for (byte v = 10; v < 250; v += 20)
            drawer.PreviewGesture(gesture, ChannelType.Dimmer, v);

        Assert.False(undoRedo.Undo().Performed); // nothing was ever pushed
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var live));
        Assert.Equal(230, live); // last preview value did take effect live
    }

    [Fact]
    public void FullGesture_ManyPreviewsThenCommit_CreatesExactlyOneUndoStep()
    {
        var (context, _, undoRedo, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 5);

        var gesture = drawer.BeginGesture(ChannelType.Dimmer);
        for (byte v = 10; v < 250; v += 5)
            drawer.PreviewGesture(gesture, ChannelType.Dimmer, v);
        drawer.CommitGesture(gesture, new Dictionary<ChannelType, byte> { [ChannelType.Dimmer] = 245 });

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var committed));
        Assert.Equal(245, committed);

        var undone = undoRedo.Undo();
        Assert.True(undone.Performed);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var restored));
        Assert.Equal(5, restored); // one Undo returns to the pre-gesture value, not any intermediate preview

        Assert.False(undoRedo.Undo().Performed); // exactly one Undo step existed
    }

    [Fact]
    public void CancelGesture_RestoresWithNoUndoEntry()
    {
        var (context, _, undoRedo, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 42);

        var gesture = drawer.BeginGesture(ChannelType.Dimmer);
        drawer.PreviewGesture(gesture, ChannelType.Dimmer, 200);
        drawer.CancelGesture(gesture);

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var restored));
        Assert.Equal(42, restored);
        Assert.False(undoRedo.Undo().Performed); // nothing to undo
    }

    [Fact]
    public void CancelGesture_MixedSelection_RestoresEachFixtureToItsOwnOriginalValue()
    {
        var (context, _, _, drawer) = Build();
        var touched = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        var untouched = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 1, 1); // never had a Programmer value
        context.Patch.Add(touched);
        context.Patch.Add(untouched);
        context.Selection.Add(touched);
        context.Selection.Add(untouched);
        context.Programmer.SetChannel(0, 0, 77); // only "touched" has a stored value

        var gesture = drawer.BeginGesture(ChannelType.Dimmer);
        drawer.PreviewGesture(gesture, ChannelType.Dimmer, 200); // unifies both onto one live value
        drawer.CancelGesture(gesture);

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var restoredTouched));
        Assert.Equal(77, restoredTouched); // its own original value, not the other fixture's
        Assert.False(context.Programmer.HasStoredValue(1, 0, out _)); // back to "no value at all", not 0
    }

    [Fact]
    public void Undo_AfterCommit_RestoresOneFixtureToPriorValue_AndAnotherToNoValueAtAll()
    {
        var (context, _, undoRedo, drawer) = Build();
        var hadValue = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        var hadNoValue = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 1, 1);
        context.Patch.Add(hadValue);
        context.Patch.Add(hadNoValue);
        context.Selection.Add(hadValue);
        context.Selection.Add(hadNoValue);
        context.Programmer.SetChannel(0, 0, 60);

        var gesture = drawer.BeginGesture(ChannelType.Dimmer);
        drawer.PreviewGesture(gesture, ChannelType.Dimmer, 180);
        drawer.CommitGesture(gesture, new Dictionary<ChannelType, byte> { [ChannelType.Dimmer] = 180 });

        var undone = undoRedo.Undo();
        Assert.True(undone.Performed);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var restored));
        Assert.Equal(60, restored);
        Assert.False(context.Programmer.HasStoredValue(1, 0, out _)); // correctly returns to "never had a value"
    }

    [Fact]
    public void Knockout_PreservedAfterCancel()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 90);
        context.Programmer.Knockout(0, 0);

        var gesture = drawer.BeginGesture(ChannelType.Dimmer);
        drawer.PreviewGesture(gesture, ChannelType.Dimmer, 200);
        drawer.CancelGesture(gesture);

        Assert.True(context.Programmer.IsKnockedOut(0, 0));
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(90, value);
    }

    [Fact]
    public void Knockout_PreservedAfterUndo()
    {
        var (context, _, undoRedo, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 90);
        context.Programmer.Knockout(0, 0);

        var gesture = drawer.BeginGesture(ChannelType.Dimmer);
        drawer.PreviewGesture(gesture, ChannelType.Dimmer, 200);
        drawer.CommitGesture(gesture, new Dictionary<ChannelType, byte> { [ChannelType.Dimmer] = 200 });

        // SetAttributeValueCommand only ever calls SetChannel, never Restore - knockout is
        // orthogonal to the stored value and stays exactly as RestoreSnapshot put it back.
        Assert.True(context.Programmer.IsKnockedOut(0, 0));
        undoRedo.Undo();

        Assert.True(context.Programmer.IsKnockedOut(0, 0)); // Undo restores the knockout too
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(90, value);
    }

    [Fact]
    public void SelectionChanged_MidGesture_PreviewBecomesNoOp_AndCommitDoesNotApply()
    {
        var (context, _, undoRedo, drawer) = Build();
        var fixtureA = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        var fixtureB = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 1, 1);
        context.Patch.Add(fixtureA);
        context.Patch.Add(fixtureB);
        context.Selection.Add(fixtureA);
        context.Programmer.SetChannel(0, 0, 20);

        var gesture = drawer.BeginGesture(ChannelType.Dimmer);
        drawer.PreviewGesture(gesture, ChannelType.Dimmer, 100);

        context.Selection.Clear();
        context.Selection.Add(fixtureB); // selection changed mid-gesture

        drawer.PreviewGesture(gesture, ChannelType.Dimmer, 150); // must be a no-op now
        drawer.CommitGesture(gesture, new Dictionary<ChannelType, byte> { [ChannelType.Dimmer] = 150 });

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var valueA));
        Assert.Equal(20, valueA); // restored to its pre-gesture value, never left at 100/150
        Assert.False(context.Programmer.HasStoredValue(1, 0, out _)); // never applied to the new selection
        Assert.False(undoRedo.Undo().Performed); // no Command was ever dispatched
    }

    [Fact]
    public void FixtureRemovedMidGesture_DoesNotThrow_StopsWritingOnceInvalid()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);

        var gesture = drawer.BeginGesture(ChannelType.Dimmer);
        drawer.PreviewGesture(gesture, ChannelType.Dimmer, 100); // valid at this point - takes effect

        context.Patch.Remove(fixture); // repatch/deletion mid-gesture

        var exception = Record.Exception(() =>
        {
            drawer.PreviewGesture(gesture, ChannelType.Dimmer, 150); // must not throw, must not write
            drawer.CommitGesture(gesture, new Dictionary<ChannelType, byte> { [ChannelType.Dimmer] = 150 });
        });

        Assert.Null(exception);
        // The preview written while the fixture was still valid stands (that was a legitimate
        // write at the time); no FURTHER write happens once the fixture is gone - neither the
        // later Preview(150) nor the Commit ever reach address (0,0) again.
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var value));
        Assert.Equal(100, value);
    }

    [Fact]
    public void FixtureRemovedBeforeFirstPreview_NeverWritesToItsAddress()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);

        var gesture = drawer.BeginGesture(ChannelType.Dimmer);
        context.Patch.Remove(fixture); // removed before any preview ever reaches it

        var exception = Record.Exception(() =>
        {
            drawer.PreviewGesture(gesture, ChannelType.Dimmer, 150);
            drawer.CommitGesture(gesture, new Dictionary<ChannelType, byte> { [ChannelType.Dimmer] = 150 });
        });

        Assert.Null(exception);
        Assert.False(context.Programmer.HasStoredValue(0, 0, out _)); // never written at all
    }

    [Fact]
    public void MultiChannelGesture_PanAndTilt_CommitsAsOneUndoStep()
    {
        var (context, _, undoRedo, drawer) = Build();
        var fixture = new PatchedFixture(PanTilt(), PanTilt().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 10); // Pan
        context.Programmer.SetChannel(0, 1, 20); // Tilt

        var gesture = drawer.BeginGesture(ChannelType.Pan, ChannelType.Tilt);
        drawer.PreviewGesture(gesture, ChannelType.Pan, 100);
        drawer.PreviewGesture(gesture, ChannelType.Tilt, 200);
        drawer.CommitGesture(gesture, new Dictionary<ChannelType, byte>
        {
            [ChannelType.Pan] = 100,
            [ChannelType.Tilt] = 200,
        });

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var pan));
        Assert.Equal(100, pan);
        Assert.True(context.Programmer.HasStoredValue(0, 1, out var tilt));
        Assert.Equal(200, tilt);

        var undone = undoRedo.Undo(); // ONE Undo for both channels together
        Assert.True(undone.Performed);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var panRestored));
        Assert.Equal(10, panRestored);
        Assert.True(context.Programmer.HasStoredValue(0, 1, out var tiltRestored));
        Assert.Equal(20, tiltRestored);

        Assert.False(undoRedo.Undo().Performed); // exactly one Undo step for the whole gesture
    }

    [Fact]
    public void BeginGesture_WhileOneAlreadyActive_SafelyCancelsThePrevious()
    {
        var (context, _, undoRedo, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 5);

        var first = drawer.BeginGesture(ChannelType.Dimmer);
        drawer.PreviewGesture(first, ChannelType.Dimmer, 200);

        var second = drawer.BeginGesture(ChannelType.Dimmer); // starting a new gesture cancels the first
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var afterCancel));
        Assert.Equal(5, afterCancel); // rolled back to pre-first-gesture value

        // The stale first gesture's calls are now harmless no-ops, not corrupting `second`.
        drawer.PreviewGesture(first, ChannelType.Dimmer, 250);
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var stillFive));
        Assert.Equal(5, stillFive);

        drawer.CommitGesture(second, new Dictionary<ChannelType, byte> { [ChannelType.Dimmer] = 90 });
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var committed));
        Assert.Equal(90, committed);

        drawer.CommitGesture(first, new Dictionary<ChannelType, byte> { [ChannelType.Dimmer] = 250 }); // stale - no-op
        Assert.True(context.Programmer.HasStoredValue(0, 0, out var stillCommitted));
        Assert.Equal(90, stillCommitted);
    }

    [Fact]
    public void CloseDrawer_CancelsActiveGesture()
    {
        var (context, _, _, drawer) = Build();
        var fixture = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 1);
        context.Patch.Add(fixture);
        context.Selection.Add(fixture);
        context.Programmer.SetChannel(0, 0, 15);

        var gesture = drawer.BeginGesture(ChannelType.Dimmer);
        drawer.PreviewGesture(gesture, ChannelType.Dimmer, 220);

        drawer.Close();

        Assert.True(context.Programmer.HasStoredValue(0, 0, out var restored));
        Assert.Equal(15, restored);
    }
}

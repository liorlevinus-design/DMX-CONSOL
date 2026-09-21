using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;
using DmxConsole.Web.EditorToolBar;
using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>
/// STORE-audit slice + Store-grammar slice: UPDATE/DELETE/GO TO remain central console actions
/// routed by the shared EditorContext to whichever screen's ViewModel is in context (unchanged by
/// the Store-grammar slice). STORE itself is no longer EditorContext-routed at all - pressing
/// STORE alone now pushes real CommandComposer grammar (see CommandSurfaceViewModelStoreGrammarTests
/// for the full grammar coverage); PressStore_* tests here only prove it converges on the exact
/// same GroupsViewModel/CueListViewModel-visible state (StoreGroupCommand/StoreCueCommand,
/// dispatched through CommandDispatcher) once a full "STORE &lt;target&gt; &lt;number&gt; ENTER"
/// sequence is driven through the Command Surface, never a second implementation.
/// </summary>
public class CommandSurfaceViewModelStoreTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private static (ConsoleContext Context, CommandDispatcher Dispatcher, EditorContextStack EditorContext,
        GroupsViewModel GroupsVm, CueListViewModel CueListVm, CommandSurfaceViewModel Surface, PatchedFixture Fixture) BuildRig()
    {
        var patch = new Patch();
        var profile = Dimmer1();
        var fixture = new PatchedFixture(profile, profile.Modes[0], 0, 1) { Number = 1 };
        patch.Add(fixture);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var editorContext = new EditorContextStack();
        var groupsVm = new GroupsViewModel(context, dispatcher);
        var cueList = new CueList();
        context.PrimaryCueList = cueList; // Store-grammar slice: "STORE CUE n" grammar's target
        var executor = new Executor(-1);
        var cueListVm = new CueListViewModel(patch, context.Programmer, context.Selection, engine, cueList, dispatcher, executor);
        var surface = new CommandSurfaceViewModel(context, dispatcher, editorContext, groupsVm, cueListVm, () => { });
        return (context, dispatcher, editorContext, groupsVm, cueListVm, surface, fixture);
    }

    [Fact]
    public void PressStore_Alone_NeverImmediatelyStoresAnything()
    {
        // The exact bug this slice fixes: STORE alone (even with fixtures already selected and a
        // Group EditorContext already active from a previous, unrelated press) must NOT
        // immediately create/overwrite anything - it only enters the Task line and waits.
        var (context, _, editorContext, groupsVm, cueListVm, surface, fixture) = BuildRig();
        context.Selection.Add(fixture);
        editorContext.EnterObject(EditorObjectType.Group, "GROUP");

        surface.PressStore();

        Assert.Empty(groupsVm.Groups.Groups);
        Assert.Empty(cueListVm.CueList.Cues);
        Assert.Equal("Store", surface.DisplayPreview);
    }

    [Fact]
    public void StoreGroupGrammar_CreatesGroupFromTheCurrentSelection()
    {
        var (context, _, _, groupsVm, _, surface, fixture) = BuildRig();
        context.Selection.Add(fixture);

        surface.PressStore();
        surface.PressToken(CommandTokenKind.Group);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);

        var group = Assert.Single(groupsVm.Groups.Groups);
        Assert.Equal(1, group.Number);
        Assert.Contains(fixture, group.Fixtures);
    }

    [Fact]
    public void PressUpdate_InGroupContext_WithASelectedGroup_UpdatesThatGroup()
    {
        var (context, dispatcher, editorContext, groupsVm, _, surface, fixture) = BuildRig();
        context.Selection.Add(fixture);
        var group = context.Groups.CreateFromSelection("Original", context.Selection, number: 1);
        var other = new PatchedFixture(Dimmer1(), Dimmer1().Modes[0], 0, 10) { Number = 2 };
        context.Patch.Add(other);
        context.Selection.Clear();
        context.Selection.Add(other); // a DIFFERENT selection now, to prove Update re-stores it

        editorContext.EnterObject(EditorObjectType.Group, "GROUP", group, "Group 1");

        surface.PressUpdate();

        Assert.Contains(other, group.Fixtures);
        Assert.DoesNotContain(fixture, group.Fixtures);
    }

    [Fact]
    public void PressUpdate_InGroupContext_WithNoSelectedGroup_IsANoOp_NeverThrows()
    {
        var (_, _, editorContext, groupsVm, _, surface, _) = BuildRig();
        editorContext.EnterObject(EditorObjectType.Group, "GROUP"); // no SelectedObject

        var exception = Record.Exception(() => surface.PressUpdate());

        Assert.Null(exception);
        Assert.Empty(groupsVm.Groups.Groups);
    }

    [Fact]
    public void PressDelete_InGroupContext_WithASelectedGroup_RemovesIt()
    {
        var (context, _, editorContext, groupsVm, _, surface, fixture) = BuildRig();
        context.Selection.Add(fixture);
        var group = context.Groups.CreateFromSelection("ToDelete", context.Selection, number: 1);
        editorContext.EnterObject(EditorObjectType.Group, "GROUP", group, "Group 1");

        surface.PressDelete();

        Assert.Empty(groupsVm.Groups.Groups);
    }

    [Fact]
    public void StoreCueGrammar_CreatesACue()
    {
        var (context, _, _, _, cueListVm, surface, fixture) = BuildRig();
        context.Selection.Add(fixture);

        surface.PressStore();
        surface.PressToken(CommandTokenKind.Cue);
        surface.PressDigit('1');
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Single(cueListVm.CueList.Cues);
    }

    [Fact]
    public void StoreGroupGrammar_WithoutANumber_FailsWithGroupNumberMissing_NeverCreatesAnything()
    {
        var (context, _, _, groupsVm, _, surface, fixture) = BuildRig();
        context.Selection.Add(fixture);

        surface.PressStore();
        surface.PressToken(CommandTokenKind.Group);
        surface.PressToken(CommandTokenKind.Enter);

        Assert.Equal("GROUP NUMBER IS MISSING", surface.DispatchError ?? surface.Current.Error);
        Assert.Empty(groupsVm.Groups.Groups);
    }

    [Fact]
    public void PressGoTo_InCueContext_WithASelectedCue_JumpsTheCueList()
    {
        var (context, _, editorContext, _, cueListVm, surface, fixture) = BuildRig();
        context.Selection.Add(fixture);
        cueListVm.NewCueNumber = 1;
        cueListVm.StoreCueCommand.Execute(null);
        var cue = Assert.Single(cueListVm.CueList.Cues);

        editorContext.EnterObject(EditorObjectType.Cue, "CUE", cue, "Cue 1");
        surface.PressGoTo();

        Assert.Same(cue, cueListVm.CueList.CurrentCue);
    }
}

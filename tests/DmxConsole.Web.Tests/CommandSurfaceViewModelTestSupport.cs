using DmxConsole.Application;
using DmxConsole.Core.Engine;
using DmxConsole.Web.EditorToolBar;
using DmxConsole.Web.Services;

namespace DmxConsole.Web.Tests;

/// <summary>
/// STORE-audit slice: CommandSurfaceViewModel now takes GroupsViewModel/CueListViewModel/an
/// onPatchApplied callback (it owns PressStore/PressUpdate/PressDelete/PressGoTo routing and
/// Quick Patch's fader-sync callback - see that type's own constructor doc comment). Every
/// existing CommandSurfaceViewModel test rig needs these three extra dependencies, but almost
/// none of them exercise Group/Cue/Patch behavior - this is the one shared, minimal-viable
/// construction helper, rather than repeating the same CueListViewModel/Executor/CueList
/// boilerplate in ten files. The Executor/CueList built here are deliberately NOT registered
/// with context.Executors (constructed directly, not via ExecutorBank.Add) so they can never
/// collide with a number a test separately assigns for its own playback-reveal scenario.
/// </summary>
internal static class CommandSurfaceViewModelTestSupport
{
    public static CommandSurfaceViewModel BuildCommandSurfaceViewModel(
        ConsoleContext context, CommandDispatcher dispatcher, EditorContextStack editorContext)
    {
        var groupsVm = new GroupsViewModel(context, dispatcher);
        var cueList = new CueList();
        var executor = new Executor(-1);
        var cueListVm = new CueListViewModel(context.Patch, context.Programmer, context.Selection,
            context.EffectiveOutput, cueList, dispatcher, executor);
        return new CommandSurfaceViewModel(context, dispatcher, editorContext, groupsVm, cueListVm, onPatchApplied: () => { });
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Application;
using DmxConsole.Application.Commands.Executors;
using DmxConsole.Application.Commands.Playback;
using DmxConsole.Core.Engine;

namespace DmxConsole.Web.Services;

/// <summary>
/// Drives the Executor bank - the smallest visible proof of Step F's Executor/Playback-Source
/// model: Create/Remove, Assign (today, only the app's single CueList exists as a source),
/// Level, and press/release Flash. Deliberately thin - a full playback-bank UI (Pages, multiple
/// source kinds) is future work once more source kinds exist.
/// </summary>
public partial class ExecutorViewModel : ObservableObject
{
    private readonly ConsoleContext _context;
    private readonly CommandDispatcher _dispatcher;
    private readonly CueList _cueList;

    public ExecutorBank Bank => _context.Executors;

    [ObservableProperty] private string _statusMessage = string.Empty;

    public ExecutorViewModel(ConsoleContext context, CommandDispatcher dispatcher, CueList cueList)
    {
        _context = context;
        _dispatcher = dispatcher;
        _cueList = cueList;
    }

    [RelayCommand]
    private void CreateNew()
    {
        var result = _dispatcher.Dispatch(new CreateExecutorCommand());
        StatusMessage = result.Success ? $"Created Executor {result.Executor!.Number}." : result.Error ?? "Failed.";
    }

    [RelayCommand]
    private void Remove(Executor executor)
    {
        var result = _dispatcher.Dispatch(new RemoveExecutorCommand(executor));
        if (!result.Success) StatusMessage = result.Error ?? "Could not remove that Executor.";
    }

    /// <summary>Assigns the app's one existing CueList onto this Executor - the only Playback
    /// Source kind that exists yet (Effect/Preset sources are future work).</summary>
    [RelayCommand]
    private void AssignCueList(Executor executor)
    {
        var result = _dispatcher.Dispatch(new AssignExecutorCommand(executor, _cueList));
        StatusMessage = result.Success ? $"Assigned the Cue List to Executor {executor.Number}." : result.Error ?? "Failed.";
    }

    [RelayCommand]
    private void Unassign(Executor executor) => _dispatcher.Dispatch(new AssignExecutorCommand(executor, null));

    public void SetLevel(Executor executor, double level) => _dispatcher.Dispatch(new SetExecutorLevelCommand(executor, level));

    [RelayCommand]
    private void FlashPress(Executor executor) => _dispatcher.DispatchAction(new FlashPressAction(executor));

    [RelayCommand]
    private void FlashRelease(Executor executor) => _dispatcher.DispatchAction(new FlashReleaseAction(executor));
}

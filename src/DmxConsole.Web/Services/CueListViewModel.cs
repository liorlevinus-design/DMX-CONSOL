using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Application;
using DmxConsole.Application.Commands.Playback;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Web.Services;

/// <summary>
/// Drives one CueList: the "record current look as a cue" form, Go/Back/Stop transport,
/// and a polled progress readout for the active fade (CueList only raises Changed on
/// discrete actions, not continuously while a fade runs, so a timer fills the gap).
/// Go/Back/Stop dispatch through the Application layer (Step F) - never a direct
/// CueList.Go()/Back()/Stop() call from the UI - so every frontend (Touch/CLI/NL/MIDI) goes
/// through the same door. RecordCue/RemoveCue/GoToCue stay direct CueList calls - Cue *editing*
/// as real Commands is future work, out of scope for Step F.
/// </summary>
public partial class CueListViewModel : ObservableObject, IDisposable
{
    private readonly Patch _patch;
    private readonly Programmer _programmer;
    private readonly CommandDispatcher _dispatcher;
    private readonly Executor _executor;
    private readonly Timer _pollTimer;

    public CueList CueList { get; }

    public string ListName => CueList.Name;

    [ObservableProperty] private string _newCueName = string.Empty;
    [ObservableProperty] private double _newCueNumber = 1;
    [ObservableProperty] private double _newFadeInSeconds = 3;
    [ObservableProperty] private double _newFadeOutSeconds = 3;

    [ObservableProperty] private Cue? _selectedCue;

    [ObservableProperty] private int _currentCueIndex = -1;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private double _transitionProgress;
    [ObservableProperty] private string _remainingTimeText = string.Empty;
    [ObservableProperty] private string _currentCueLabel = "(none)";

    public CueListViewModel(Patch patch, Programmer programmer, CueList cueList, CommandDispatcher dispatcher, Executor executor)
    {
        _patch = patch;
        _programmer = programmer;
        _dispatcher = dispatcher;
        _executor = executor;
        CueList = cueList;
        CueList.Changed += OnCueListChanged;

        // A plain thread-pool timer (no WPF dispatcher here) polling every 100ms -
        // ObservableObject's setters below raise PropertyChanged, which Blazor
        // components subscribe to and marshal onto their own render sync context.
        _pollTimer = new Timer(_ => RefreshStatus(), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        RefreshStatus();
    }

    private void OnCueListChanged() => RefreshStatus();

    private void RefreshStatus()
    {
        CurrentCueIndex = CueList.CurrentCueIndex;
        IsActive = CueList.IsActive;
        var (progress, remaining) = CueList.GetTransitionStatus();
        TransitionProgress = progress;
        RemainingTimeText = IsActive ? $"{remaining:mm\\:ss\\.f}" : string.Empty;
        CurrentCueLabel = CueList.CurrentCue is { } c ? $"{c.Number:0.##} - {c.Name}" : "(none)";
    }

    [RelayCommand]
    private void RecordCue()
    {
        var name = string.IsNullOrWhiteSpace(NewCueName) ? $"Cue {NewCueNumber:0.##}" : NewCueName;
        CueList.RecordCue(_patch, _programmer, name, NewCueNumber,
            TimeSpan.FromSeconds(NewFadeInSeconds), TimeSpan.FromSeconds(NewFadeOutSeconds));

        NewCueNumber = Math.Floor(NewCueNumber) + 1;
        NewCueName = string.Empty;
    }

    [RelayCommand]
    private void Go() => _dispatcher.DispatchAction(new GoAction(_executor));

    [RelayCommand]
    private void Back() => _dispatcher.DispatchAction(new BackAction(_executor));

    [RelayCommand]
    private void Stop() => _dispatcher.DispatchAction(new StopAction(_executor));

    [RelayCommand]
    private void GoToCue(Cue? cue)
    {
        if (cue is null) return;
        CueList.GoToCue(cue);
    }

    [RelayCommand]
    private void RemoveCue(Cue? cue)
    {
        if (cue is null) return;
        CueList.RemoveCue(cue);
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        CueList.Changed -= OnCueListChanged;
    }
}

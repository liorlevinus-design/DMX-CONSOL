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
    [ObservableProperty] private string _statusMessage = string.Empty;

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

    /// <summary>
    /// UX correction F: Store creates a brand-new Cue at the explicit NewCueNumber - and only
    /// that. A number already in use is an explicit, blocking conflict (never a silent
    /// duplicate-numbered Cue, never a silent substitute number) - the operator corrects the
    /// number or uses Update instead. Mirrors the same "explicit collision, no guessing" rule
    /// this milestone already applied to Patch fixture numbering and Group numbers.
    /// </summary>
    [RelayCommand]
    private void StoreCue()
    {
        if (CueList.FindByNumber(NewCueNumber) is not null)
        {
            StatusMessage = $"Cue {NewCueNumber:0.##} already exists - Update it instead, or choose a different number.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewCueName) ? $"Cue {NewCueNumber:0.##}" : NewCueName;
        CueList.RecordCue(_patch, _programmer, name, NewCueNumber,
            TimeSpan.FromSeconds(NewFadeInSeconds), TimeSpan.FromSeconds(NewFadeOutSeconds));

        StatusMessage = $"Stored Cue {NewCueNumber:0.##}.";
        NewCueNumber = Math.Floor(NewCueNumber) + 1;
        NewCueName = string.Empty;
    }

    /// <summary>Update requires an already-existing Cue at NewCueNumber - an explicit "no such
    /// cue" error if there isn't one, never silently creating it instead (that's what Store is
    /// for). Replaces the target Cue's captured levels/name/timing in place via
    /// CueList.UpdateCue - its Number and list position are unchanged.</summary>
    [RelayCommand]
    private void UpdateCue()
    {
        var existing = CueList.FindByNumber(NewCueNumber);
        if (existing is null)
        {
            StatusMessage = $"Cue {NewCueNumber:0.##} doesn't exist yet - Store it first.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewCueName) ? existing.Name : NewCueName;
        CueList.UpdateCue(existing, _patch, _programmer, name,
            TimeSpan.FromSeconds(NewFadeInSeconds), TimeSpan.FromSeconds(NewFadeOutSeconds));

        StatusMessage = $"Updated Cue {NewCueNumber:0.##}.";
    }

    /// <summary>Loads an existing Cue's number/name/timing into the Store/Update form fields, so
    /// "Update Cue N" always targets exactly the cue the operator clicked, never whatever number
    /// happened to be left in the box from a previous action.</summary>
    [RelayCommand]
    private void LoadForEdit(Cue? cue)
    {
        if (cue is null) return;
        NewCueNumber = cue.Number;
        NewCueName = cue.Name;
        NewFadeInSeconds = cue.GeneralTiming.FadeInTime.TotalSeconds;
        NewFadeOutSeconds = cue.GeneralTiming.FadeOutTime.TotalSeconds;
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

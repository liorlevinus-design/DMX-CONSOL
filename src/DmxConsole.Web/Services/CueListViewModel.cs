using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Application;
using DmxConsole.Application.Commands;
using DmxConsole.Application.Commands.Cues;
using DmxConsole.Application.Commands.Playback;
using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;

namespace DmxConsole.Web.Services;

/// <summary>
/// Drives one CueList: the "record current look as a cue" form, Go/Back/Stop transport,
/// and a polled progress readout for the active fade (CueList only raises Changed on
/// discrete actions, not continuously while a fade runs, so a timer fills the gap).
/// Go/Back/Stop dispatch through the Application layer (Step F) - never a direct
/// CueList.Go()/Back()/Stop() call from the UI - so every frontend (Touch/CLI/NL/MIDI) goes
/// through the same door. Store/Update also dispatch through the Application layer now (N1 slice,
/// CLAUDE.md §5) - via StoreCueCommand/UpdateCueCommand wrapped with ClearProgrammerCommand in one
/// CompositeCommand, never a direct CueList.RecordCue/UpdateCue call - so the Store/Update +
/// Programmer-clear is one atomic, undoable transaction. RemoveCue/GoToCue remain direct CueList
/// calls - out of scope for this slice (see CLAUDE.md's N1 audit).
/// </summary>
public partial class CueListViewModel : ObservableObject, IDisposable
{
    private readonly Patch _patch;
    private readonly Programmer _programmer;
    private readonly FixtureSelection _selection;
    private readonly IEffectiveOutputReader _effectiveOutput;
    private readonly CommandDispatcher _dispatcher;
    private readonly Executor _executor;
    private readonly ProgrammerViewModel _programmerVm;
    private readonly Timer _pollTimer;

    public CueList CueList { get; }

    public string ListName => CueList.Name;

    [ObservableProperty] private string _newCueName = string.Empty;
    [ObservableProperty] private double _newCueNumber = 1;
    [ObservableProperty] private double _newTimeInSeconds = 3;
    [ObservableProperty] private double _newTimeOutSeconds = 3;
    [ObservableProperty] private double _newDelayInSeconds;
    [ObservableProperty] private double _newDelayOutSeconds;
    [ObservableProperty] private double _newWaitSeconds;
    [ObservableProperty] private CueTriggerMode _newTriggerMode = CueTriggerMode.Manual;
    [ObservableProperty] private CueStoreFilter _storeFilter = CueStoreFilter.AllStage;

    [ObservableProperty] private Cue? _selectedCue;

    [ObservableProperty] private int _currentCueIndex = -1;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private double _transitionProgress;
    [ObservableProperty] private string _remainingTimeText = string.Empty;
    [ObservableProperty] private double _fadeInProgress;
    [ObservableProperty] private string _fadeInRemainingText = string.Empty;
    [ObservableProperty] private double _fadeOutProgress;
    [ObservableProperty] private string _fadeOutRemainingText = string.Empty;
    [ObservableProperty] private string _currentCueLabel = "(none)";
    [ObservableProperty] private string _statusMessage = string.Empty;

    public CueListViewModel(Patch patch, Programmer programmer, FixtureSelection selection,
        IEffectiveOutputReader effectiveOutput, CueList cueList, CommandDispatcher dispatcher, Executor executor,
        ProgrammerViewModel programmerVm)
    {
        _patch = patch;
        _programmer = programmer;
        _selection = selection;
        _effectiveOutput = effectiveOutput;
        _dispatcher = dispatcher;
        _executor = executor;
        _programmerVm = programmerVm;
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

        // Separate IN/OUT progress (docs/OPERATOR_UX_ROADMAP.md progress display) - pure derived
        // views off the SAME elapsed-time state GetTransitionStatus() already uses, exposed by
        // CueList.GetStatus() (CueListPlaybackStatus.FadeIn/FadeOut). No second clock, no change
        // to trigger-chain logic, which never reads GetStatus().
        if (CueList.GetStatus() is CueListPlaybackStatus status)
        {
            FadeInProgress = status.FadeIn.PercentComplete;
            FadeInRemainingText = IsActive ? $"{status.FadeIn.Remaining:mm\\:ss\\.f}" : string.Empty;
            FadeOutProgress = status.FadeOut.PercentComplete;
            FadeOutRemainingText = IsActive ? $"{status.FadeOut.Remaining:mm\\:ss\\.f}" : string.Empty;
        }
    }

    private CueStoreOptions BuildStoreOptions(CueStoreFilter filter) => new(
        new CueTiming(TimeSpan.FromSeconds(NewTimeInSeconds), TimeSpan.FromSeconds(NewTimeOutSeconds),
            TimeSpan.FromSeconds(NewDelayInSeconds), TimeSpan.FromSeconds(NewDelayOutSeconds)),
        NewTriggerMode, TimeSpan.FromSeconds(NewWaitSeconds), filter);

    /// <summary>
    /// UX correction F: Store creates a brand-new Cue at the explicit NewCueNumber - and only
    /// that. A number already in use is an explicit, blocking conflict (never a silent
    /// duplicate-numbered Cue, never a silent substitute number) - the operator corrects the
    /// number or uses Update instead. Mirrors the same "explicit collision, no guessing" rule
    /// this milestone already applied to Patch fixture numbering and Group numbers.
    /// </summary>
    [RelayCommand]
    private void StoreCue() => StoreWithFilter(StoreFilter);

    /// <summary>H1.6 Slice 2 - the five STORE OPTIONS soft keys (EditorToolBar) each set the
    /// filter and store in one press, same as pressing STORE would with that filter already
    /// selected in the panel's dropdown.</summary>
    [RelayCommand]
    private void StoreCueWithFilter(CueStoreFilter filter)
    {
        StoreFilter = filter;
        StoreWithFilter(filter);
    }

    private void StoreWithFilter(CueStoreFilter filter)
    {
        if (CueList.FindByNumber(NewCueNumber) is not null)
        {
            StatusMessage = $"Cue {NewCueNumber:0.##} already exists - Update it instead, or choose a different number.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewCueName) ? $"Cue {NewCueNumber:0.##}" : NewCueName;
        var storeCommand = new StoreCueCommand(CueList, name, NewCueNumber, BuildStoreOptions(filter));

        // N1 (CLAUDE.md §5): the Store and the Programmer clear are one atomic, undoable
        // transaction - never a bare StoreCueCommand dispatch. If the Store fails (shouldn't
        // happen here, since FindByNumber above already checked), CompositeCommand's rollback
        // guarantees the Programmer clear never runs either.
        var result = _dispatcher.Dispatch(new CompositeCommand(new IConsoleCommand[] { storeCommand, new ClearProgrammerCommand() }));

        if (!result.Success)
        {
            StatusMessage = result.Error ?? "Failed to store Cue.";
            return;
        }

        _programmerVm.RefreshAllFaders(); // Blazor convention: a Programmer-mutating dispatch must refresh fader display
        StatusMessage = $"Stored Cue {NewCueNumber:0.##} ({filter}).";
        NewCueNumber = Math.Floor(NewCueNumber) + 1;
        NewCueName = string.Empty;
    }

    /// <summary>Update requires an already-existing Cue at NewCueNumber - an explicit "no such
    /// cue" error if there isn't one, never silently creating it instead (that's what Store is
    /// for). Replaces the target Cue's captured levels/name/timing in place via
    /// <see cref="UpdateCueCommand"/> (overwrite: false) - its Number and list position are
    /// unchanged. N1 (CLAUDE.md §5): dispatched together with a ClearProgrammerCommand as one
    /// atomic, undoable CompositeCommand - never a bare UpdateCueCommand, never a direct
    /// CueList.UpdateCue call.</summary>
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
        var updateCommand = new UpdateCueCommand(CueList, existing, name, BuildStoreOptions(StoreFilter));
        var result = _dispatcher.Dispatch(new CompositeCommand(new IConsoleCommand[] { updateCommand, new ClearProgrammerCommand() }));

        if (!result.Success)
        {
            StatusMessage = result.Error ?? "Failed to update Cue.";
            return;
        }

        _programmerVm.RefreshAllFaders(); // Blazor convention: a Programmer-mutating dispatch must refresh fader display
        StatusMessage = $"Updated Cue {NewCueNumber:0.##}.";
    }

    /// <summary>H1.6 Slice 2 - toggles an already-stored Cue's FOLLOW ON / MANUAL trigger mode
    /// directly (EditorToolBar's Cue &gt; Time &gt; FOLLOW ON / MANUAL keys), independent of the
    /// Store/Update form.</summary>
    [RelayCommand]
    private void SetTriggerMode((Cue Cue, CueTriggerMode Mode) arg)
    {
        if (!CueList.SetTriggerMode(arg.Cue, arg.Mode))
        {
            StatusMessage = $"Cue {arg.Cue.Number:0.##} is no longer in this list.";
            return;
        }
        StatusMessage = $"Cue {arg.Cue.Number:0.##}: {arg.Mode switch { CueTriggerMode.AutoFollow => "AUTOFOLLOW", CueTriggerMode.Wait => "WAIT", _ => "MANUAL" }}.";
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
        NewTimeInSeconds = cue.Timing.TimeIn.TotalSeconds;
        NewTimeOutSeconds = cue.Timing.TimeOut.TotalSeconds;
        NewDelayInSeconds = cue.Timing.DelayIn.TotalSeconds;
        NewDelayOutSeconds = cue.Timing.DelayOut.TotalSeconds;
        NewWaitSeconds = cue.WaitTime.TotalSeconds;
        NewTriggerMode = cue.TriggerMode;
    }

    [RelayCommand]
    private void Go() => _dispatcher.DispatchAction(new GoAction(_executor));

    [RelayCommand]
    private void Back() => _dispatcher.DispatchAction(new BackAction(_executor));

    /// <summary>SHIFT+GO slice: a zero-time jump to the next cue - same shared GoAction/CueList
    /// path as a normal Go, just with instant:true. A separate parameterless RelayCommand (rather
    /// than adding a bool parameter to Go() above) so existing callers of GoCommand.Execute(null)
    /// (the Cues panel's own GO button) are never at risk of a null-to-bool cast.</summary>
    [RelayCommand]
    private void GoInstant() => _dispatcher.DispatchAction(new GoAction(_executor, instant: true));

    /// <summary>SHIFT+BACK slice - see GoInstant's own doc comment.</summary>
    [RelayCommand]
    private void BackInstant() => _dispatcher.DispatchAction(new BackAction(_executor, instant: true));

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

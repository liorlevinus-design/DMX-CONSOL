using CommunityToolkit.Mvvm.Input;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using DmxConsole.Web.EditorToolBar;

namespace DmxConsole.Web.Services;

/// <summary>
/// Drives the Editor Tool Bar (H1.6): owns nothing but the interpretation of a soft-key press.
/// EnterContext keys push/replace the shared EditorContextStack. ContextAction keys (STORE/
/// UPDATE/DELETE/ODD/EVEN/...) are resolved generically from (action, CurrentContext.ObjectType)
/// onto an ALREADY-EXISTING RelayCommand on the relevant ViewModel (CueListVm/GroupsVm/
/// SelectionVm/MainViewModel's own RemoveFixtureCommand) - this class never duplicates their
/// business logic, only dispatches to it. A key with no matching (action, ObjectType) case, or
/// whose target command's CanExecute currently refuses, reports "not available" - never a silent
/// no-op, never fake success (H1.6 §16).
/// </summary>
public sealed class EditorToolBarViewModel
{
    private readonly MainViewModel _main;
    private readonly CueListViewModel _cueListVm;
    private readonly GroupsViewModel _groupsVm;
    private readonly SelectionViewModel _selectionVm;

    public EditorContextStack Context { get; }
    public SoftKeyRegistry Registry { get; }

    public string? StatusMessage { get; private set; }

    public event Action? Changed;

    public EditorToolBarViewModel(EditorContextStack context, SoftKeyRegistry registry,
        MainViewModel main, CueListViewModel cueListVm, GroupsViewModel groupsVm, SelectionViewModel selectionVm)
    {
        Context = context;
        Registry = registry;
        _main = main;
        _cueListVm = cueListVm;
        _groupsVm = groupsVm;
        _selectionVm = selectionVm;

        Context.Changed += () => Changed?.Invoke();
    }

    public void Press(SoftKeyDefinition key)
    {
        StatusMessage = null;

        switch (key.ActionType)
        {
            case SoftKeyActionType.EnterContext:
                EnterContext(key);
                break;
            case SoftKeyActionType.ContextAction:
                RunContextAction(key);
                break;
            default:
                StatusMessage = $"{key.Label}: not implemented yet.";
                break;
        }

        Changed?.Invoke();
    }

    public void Back() => Context.Back();

    public void Root() => Context.Root();

    private void EnterContext(SoftKeyDefinition key)
    {
        if (key.NextContextIdSegment is null || key.NextContextLabel is null) return;

        if (Context.IsAtRoot)
        {
            if (Enum.TryParse<EditorObjectType>(key.NextContextIdSegment, out var objectType))
                Context.EnterObject(objectType, key.NextContextLabel);
        }
        else
        {
            Context.Push(key.NextContextIdSegment, key.NextContextLabel);
        }
    }

    private void RunContextAction(SoftKeyDefinition key)
    {
        var frame = Context.Current;

        // Keyed on (action-suffix, ObjectType), never on the key's own identity beyond that -
        // this is the "same key, context-dependent behavior" rule (H1.6 §3) made literal: adding
        // a case here is the only place object-specific behavior is allowed to live.
        string action = key.Id[(key.Id.LastIndexOf('.') + 1)..];

        bool handled = (action, frame.ObjectType) switch
        {
            ("store", EditorObjectType.Cue) => Invoke(_cueListVm.StoreCueCommand),
            ("update", EditorObjectType.Cue) => Invoke(_cueListVm.UpdateCueCommand),
            ("delete", EditorObjectType.Cue) when frame.SelectedObject is Cue cue => Invoke(_cueListVm.RemoveCueCommand, cue),

            // H1.6 Slice 2 - Vector's Store Options mode: each button sets the filter and stores
            // in one press (Invoke<T> already exists below for exactly this shape).
            ("alleditor", EditorObjectType.Cue) => Invoke(_cueListVm.StoreCueWithFilterCommand, CueStoreFilter.AllEditor),
            ("activeonly", EditorObjectType.Cue) => Invoke(_cueListVm.StoreCueWithFilterCommand, CueStoreFilter.ActiveOnly),
            ("allstage", EditorObjectType.Cue) => Invoke(_cueListVm.StoreCueWithFilterCommand, CueStoreFilter.AllStage),
            ("allparamsforselected", EditorObjectType.Cue) => Invoke(_cueListVm.StoreCueWithFilterCommand, CueStoreFilter.AllParamsForSelected),
            ("allparamsifactive", EditorObjectType.Cue) => Invoke(_cueListVm.StoreCueWithFilterCommand, CueStoreFilter.AllParamsIfActive),

            // H1.6 Slice 2 - Vector's Time mode FOLLOW ON / MANUAL toggle a specific, already
            // stored Cue's trigger mode directly (not part of the Store/Update form).
            ("followon", EditorObjectType.Cue) when frame.SelectedObject is Cue followCue
                => Invoke(_cueListVm.SetTriggerModeCommand, (followCue, CueTriggerMode.Follow)),
            ("manual", EditorObjectType.Cue) when frame.SelectedObject is Cue manualCue
                => Invoke(_cueListVm.SetTriggerModeCommand, (manualCue, CueTriggerMode.Manual)),

            ("store", EditorObjectType.Group) => Invoke(_groupsVm.StoreCommand),
            ("update", EditorObjectType.Group) when frame.SelectedObject is FixtureGroup group => Invoke(_groupsVm.UpdateCommand, group),
            ("delete", EditorObjectType.Group) when frame.SelectedObject is FixtureGroup group => Invoke(_groupsVm.RemoveCommand, group),

            ("delete", EditorObjectType.Fixture) when frame.SelectedObject is PatchedFixture fixture => Invoke(_main.RemoveFixtureCommand, fixture),
            ("odd", EditorObjectType.Fixture) => Invoke(_selectionVm.SelectOddCommand),
            ("even", EditorObjectType.Fixture) => Invoke(_selectionVm.SelectEvenCommand),

            _ => false,
        };

        if (!handled) StatusMessage = $"{key.Label}: not available for {frame.Label} right now.";
    }

    private static bool Invoke(IRelayCommand command)
    {
        if (!command.CanExecute(null)) return false;
        command.Execute(null);
        return true;
    }

    private static bool Invoke<T>(IRelayCommand<T> command, T arg)
    {
        if (!command.CanExecute(arg)) return false;
        command.Execute(arg);
        return true;
    }
}

namespace DmxConsole.Application.Macros;

/// <summary>
/// LEARN MACRO workflow (docs/COMMAND_SURFACE_KEY_SPEC.md MACROS §3/§11/§12). Recording attaches
/// to the SAME <see cref="CommandDispatcher"/> every UI surface already dispatches through
/// (<see cref="CommandDispatcher.CommandExecuted"/>/<see cref="CommandDispatcher.ActionExecuted"/>)
/// - so whatever successfully dispatches while recording, from ANY surface (touch keypad,
/// physical keyboard, a GUI panel button), is recorded in order, with zero changes needed to any
/// of those surfaces. Nothing that doesn't go through CommandDispatcher can ever become a Macro
/// step - workspace/pane/tab UI, hover/focus, and soft-key navigation that performs no console
/// operation never call Dispatch/DispatchAction at all (see CommandDispatcher's own "single entry
/// point" doc comment), so they are excluded by construction, not by a maintained filter list.
/// </summary>
public sealed class MacroRecorder
{
    private readonly CommandDispatcher _dispatcher;
    private readonly MacroBank _bank;
    private readonly List<MacroStep> _steps = new();
    private bool _attached;

    public bool IsRecording { get; private set; }
    public int? RecordingSlot { get; private set; }

    public MacroRecorder(CommandDispatcher dispatcher, MacroBank bank)
    {
        _dispatcher = dispatcher;
        _bank = bank;
    }

    /// <summary>
    /// Starts recording into <paramref name="slot"/>. If the slot already holds a non-empty
    /// Macro and <paramref name="confirmOverwrite"/> is false, recording does NOT start - returns
    /// a structured "already exists" result (§11: no silent overwrite) carrying the existing
    /// Macro so a caller can show its size/name before asking the operator to explicitly confirm.
    /// </summary>
    public CommandResult Start(int slot, bool confirmOverwrite = false)
    {
        if (!MacroBank.IsValidSlot(slot))
            return CommandResult.Failed(ConsoleActionType.LearnMacroStart, $"Invalid Macro slot {slot} - must be {MacroBank.MinSlot}-{MacroBank.MaxSlot}.");

        if (IsRecording)
            return CommandResult.Failed(ConsoleActionType.LearnMacroStart, "Already recording a Macro - stop or cancel it first.");

        var existing = _bank.FindBySlot(slot);
        if (existing is { Steps.Count: > 0 } && !confirmOverwrite)
        {
            return new CommandResult
            {
                ActionType = ConsoleActionType.LearnMacroStart,
                Success = false,
                Error = $"Macro {slot} already has {existing.Steps.Count} step(s) - overwrite requires explicit confirmation.",
                Macro = existing,
            };
        }

        IsRecording = true;
        RecordingSlot = slot;
        _steps.Clear();
        _dispatcher.CommandExecuted += OnCommandExecuted;
        _dispatcher.ActionExecuted += OnActionExecuted;
        _attached = true;

        return new CommandResult { ActionType = ConsoleActionType.LearnMacroStart, Success = true };
    }

    private void OnCommandExecuted(IConsoleCommand command) => _steps.Add(MacroStep.ForCommand(command));
    private void OnActionExecuted(IConsoleAction action) => _steps.Add(MacroStep.ForAction(action));

    /// <summary>
    /// Stops recording and saves - even a zero-step recording is saved as a valid, empty Macro
    /// (an intentional recording where nothing happened to be executed), never silently
    /// discarded. Playing an empty Macro is handled separately by <see cref="MacroPlaybackService"/>
    /// (§4's "clear structured Macro empty result").
    /// </summary>
    public CommandResult Stop()
    {
        if (!IsRecording) return CommandResult.Failed(ConsoleActionType.LearnMacroStop, "Not currently recording a Macro.");

        Detach();
        int slot = RecordingSlot!.Value;
        var macro = new Macro(slot);
        macro.Steps.AddRange(_steps);
        _bank.Set(slot, macro);

        IsRecording = false;
        RecordingSlot = null;
        _steps.Clear();

        return new CommandResult
        {
            ActionType = ConsoleActionType.LearnMacroStop,
            Macro = macro,
            Message = $"Macro {slot} saved ({macro.Steps.Count} step(s)).",
        };
    }

    /// <summary>Cancels recording without saving - the slot's previous Macro (if any) is left
    /// completely unchanged (§12).</summary>
    public CommandResult Cancel()
    {
        if (!IsRecording) return CommandResult.Failed(ConsoleActionType.LearnMacroCancel, "Not currently recording a Macro.");

        Detach();
        int slot = RecordingSlot!.Value;
        IsRecording = false;
        RecordingSlot = null;
        _steps.Clear();

        return new CommandResult
        {
            ActionType = ConsoleActionType.LearnMacroCancel,
            Message = $"Macro {slot} recording canceled - previous definition unchanged.",
        };
    }

    private void Detach()
    {
        if (!_attached) return;
        _dispatcher.CommandExecuted -= OnCommandExecuted;
        _dispatcher.ActionExecuted -= OnActionExecuted;
        _attached = false;
    }
}

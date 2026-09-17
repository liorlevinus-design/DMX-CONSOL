namespace DmxConsole.Application.Macros;

/// <summary>
/// MACRO N playback (docs/COMMAND_SURFACE_KEY_SPEC.md MACROS §4/§8/§9). Replays each recorded
/// step through the SAME <see cref="CommandDispatcher.Dispatch"/>/<see cref="CommandDispatcher.DispatchAction"/>
/// calls a live operator press would use - so every recorded <see cref="IConsoleCommand"/> enters
/// Undo history exactly as if the operator had executed it manually (one Undo entry per step,
/// never one giant Macro-transaction - §7), and every <see cref="IConsoleAction"/> stays
/// non-undoable. This type is deliberately NOT itself an IConsoleCommand/IConsoleAction dispatched
/// through CommandDispatcher - it is its own small orchestration on top of it, exactly so it never
/// invents a new Undo model.
///
/// REPLAY INSTANCE SAFETY: a Command step's stored template is NEVER dispatched directly - each
/// playback calls MacroStep.CreateCommandForPlayback() to get a brand-new IConsoleCommand
/// instance first (see IReplayableCommand/MacroStep's own doc comments). This is what makes
/// "record once, play the same Macro N times, Undo N times" restore each execution's own state
/// correctly - two Undo-stack entries never share one mutable snapshot object. An Action step's
/// instance IS dispatched directly - Actions hold no per-execution mutable state.
/// </summary>
public sealed class MacroPlaybackService
{
    private readonly CommandDispatcher _dispatcher;
    private readonly MacroBank _bank;
    private readonly MacroRecorder _recorder;
    private bool _isPlaying;

    public MacroPlaybackService(CommandDispatcher dispatcher, MacroBank bank, MacroRecorder recorder)
    {
        _dispatcher = dispatcher;
        _bank = bank;
        _recorder = recorder;
    }

    public CommandResult Play(int slot)
    {
        if (!MacroBank.IsValidSlot(slot))
            return CommandResult.Failed(ConsoleActionType.PlayMacro, $"Invalid Macro slot {slot} - must be {MacroBank.MinSlot}-{MacroBank.MaxSlot}.");

        // §8/§9: reject cleanly rather than embedding nested/recursive playback. A Macro can
        // never contain a recorded "play a macro" step in v1 in the first place (playback is
        // rejected below while recording, so that step is never created) - the _isPlaying guard
        // is a second, independent, defensive layer against Play() somehow being re-entered
        // (e.g. a future caller change), so self-recursion can never stack-overflow either way.
        if (_recorder.IsRecording)
            return CommandResult.Failed(ConsoleActionType.PlayMacro, $"Cannot play Macro {slot} while LEARN MACRO is recording.");

        if (_isPlaying)
            return CommandResult.Failed(ConsoleActionType.PlayMacro, "A Macro is already playing back - nested Macro playback is not supported.");

        var macro = _bank.FindBySlot(slot);
        if (macro is null || macro.Steps.Count == 0)
            return CommandResult.Failed(ConsoleActionType.PlayMacro, $"Macro {slot} is empty.");

        _isPlaying = true;
        try
        {
            var childResults = new List<CommandResult>();
            foreach (var step in macro.Steps)
            {
                var result = step.Kind == MacroStepKind.Command
                    ? _dispatcher.Dispatch(step.CreateCommandForPlayback()) // fresh instance every time - never the template
                    : _dispatcher.DispatchAction(step.Action!);
                childResults.Add(result);
            }

            return new CommandResult
            {
                ActionType = ConsoleActionType.PlayMacro,
                Success = true,
                Macro = macro,
                ChildResults = childResults,
            };
        }
        finally
        {
            _isPlaying = false;
        }
    }
}

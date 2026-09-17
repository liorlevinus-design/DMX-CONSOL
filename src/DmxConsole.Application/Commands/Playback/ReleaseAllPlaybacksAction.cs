using DmxConsole.Core.Engine;

namespace DmxConsole.Application.Commands.Playback;

/// <summary>
/// SHIFT+RELEASE (docs/COMMAND_SURFACE_KEY_SPEC.md §7/§18): stops every currently active
/// playback source - every Executor whose Source supports ISequencedPlayback, and every
/// enabled Effect - without deleting any show data, touching Editor/Programmer values, or
/// altering Selection. Operational/runtime (IConsoleAction), so it never enters Undo history
/// and never clears the Redo stack (see IConsoleAction's own doc comment).
///
/// Reuses exactly what StopAction/StopEffectAction already do per-object (Executor.Stop(),
/// EffectPhaser.Enabled = false) rather than duplicating that logic - this is simply "do it to
/// all of them, once."
///
/// Future Submasters (not built yet) are not addressed here - there is nothing to release yet;
/// when they exist, this action's loop gains one more collection to walk, not a new mechanism.
/// </summary>
public sealed class ReleaseAllPlaybacksAction : IConsoleAction
{
    public CommandResult Execute(ConsoleContext context)
    {
        foreach (var executor in context.Executors.Executors)
            if (executor.Source is ISequencedPlayback)
                executor.Stop();

        foreach (var effect in context.Effects.Effects)
            effect.Enabled = false;

        return new CommandResult { ActionType = ConsoleActionType.ReleaseAllPlaybacks };
    }
}

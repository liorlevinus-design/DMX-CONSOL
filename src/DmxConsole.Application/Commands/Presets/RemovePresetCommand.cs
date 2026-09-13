using DmxConsole.Core.Presets;

namespace DmxConsole.Application.Commands.Presets;

/// <summary>Removes a saved Preset. Undo re-inserts it at (as close as possible to) its original position.</summary>
public sealed class RemovePresetCommand : IConsoleCommand
{
    private readonly Preset _preset;
    private int _previousIndex = -1;

    public RemovePresetCommand(Preset preset) => _preset = preset;

    public CommandResult Execute(ConsoleContext context)
    {
        _previousIndex = context.Presets.Presets.IndexOf(_preset);
        if (_previousIndex < 0)
        {
            return CommandResult.Failed(ConsoleActionType.RemovePreset,
                $"Preset '{_preset.Name}' is not in the current preset library.");
        }

        context.Presets.Remove(_preset);

        return new CommandResult
        {
            ActionType = ConsoleActionType.RemovePreset,
            Preset = _preset,
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_previousIndex < 0) return; // Execute failed or was never called - nothing to undo

        int index = Math.Min(_previousIndex, context.Presets.Presets.Count);
        context.Presets.Presets.Insert(index, _preset);
    }
}

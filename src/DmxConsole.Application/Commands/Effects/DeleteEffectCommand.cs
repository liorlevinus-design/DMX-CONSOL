using DmxConsole.Core.Effects;

namespace DmxConsole.Application.Commands.Effects;

public sealed class DeleteEffectCommand : IConsoleCommand
{
    private readonly EffectPhaser _effect;
    private int _previousIndex = -1;

    public DeleteEffectCommand(EffectPhaser effect) => _effect = effect;

    public CommandResult Execute(ConsoleContext context)
    {
        _previousIndex = context.Effects.Effects.IndexOf(_effect);
        if (_previousIndex < 0)
            return CommandResult.Failed(ConsoleActionType.DeleteEffect, "Effect is not in the current show.");
        context.Effects.Remove(_effect);
        return new CommandResult { ActionType = ConsoleActionType.DeleteEffect, Effect = _effect };
    }

    public void Undo(ConsoleContext context)
    {
        if (_previousIndex >= 0) context.Effects.Insert(_previousIndex, _effect);
    }
}

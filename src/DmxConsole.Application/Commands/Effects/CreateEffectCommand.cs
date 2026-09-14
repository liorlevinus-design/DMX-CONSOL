using DmxConsole.Core.Effects;

namespace DmxConsole.Application.Commands.Effects;

public sealed class CreateEffectCommand : IConsoleCommand, IHasUndoRisk
{
    private readonly EffectPhaser _effect;
    public CreateEffectCommand(EffectPhaser effect) => _effect = effect;

    public CommandResult Execute(ConsoleContext context)
    {
        if (context.Effects.Effects.Contains(_effect))
            return CommandResult.Failed(ConsoleActionType.CreateEffect, "Effect already exists.");
        _effect.MarkAllControlledChannelsChanged();
        context.Effects.Add(_effect);
        return new CommandResult { ActionType = ConsoleActionType.CreateEffect, Effect = _effect };
    }

    public void Undo(ConsoleContext context) => context.Effects.Remove(_effect);

    public UndoProposal PrepareUndo()
    {
        string description = $"Delete effect '{_effect.Name}'";
        return new UndoProposal(description,
            new[] { new UndoOption("delete", description, UndoRisk.Destructive) });
    }
}

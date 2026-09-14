using DmxConsole.Core.Effects;

namespace DmxConsole.Application.Commands.Playback;

public sealed class StartEffectAction(EffectPhaser effect) : IConsoleAction
{
    public CommandResult Execute(ConsoleContext context)
    {
        if (!context.Effects.Effects.Contains(effect))
            return CommandResult.Failed(ConsoleActionType.StartEffect, "Effect is not in the current show.");
        if (!effect.Enabled)
        {
            effect.Enabled = true;
            effect.MarkAllControlledChannelsChanged();
        }
        return new CommandResult { ActionType = ConsoleActionType.StartEffect, Effect = effect };
    }
}

public sealed class StopEffectAction(EffectPhaser effect) : IConsoleAction
{
    public CommandResult Execute(ConsoleContext context)
    {
        if (!context.Effects.Effects.Contains(effect))
            return CommandResult.Failed(ConsoleActionType.StopEffect, "Effect is not in the current show.");
        effect.Enabled = false;
        return new CommandResult { ActionType = ConsoleActionType.StopEffect, Effect = effect };
    }
}

public sealed class SetEffectRateAction(EffectPhaser effect, double speedHz) : IConsoleAction
{
    public CommandResult Execute(ConsoleContext context)
    {
        if (!context.Effects.Effects.Contains(effect))
            return CommandResult.Failed(ConsoleActionType.SetEffectRate, "Effect is not in the current show.");
        if (!double.IsFinite(speedHz) || speedHz < 0)
            return CommandResult.Failed(ConsoleActionType.SetEffectRate, "Effect speed must be finite and non-negative.");
        if (effect.SpeedHz != speedHz)
        {
            effect.SpeedHz = speedHz;
            effect.MarkAllControlledChannelsChanged();
        }
        return new CommandResult { ActionType = ConsoleActionType.SetEffectRate, Effect = effect };
    }
}

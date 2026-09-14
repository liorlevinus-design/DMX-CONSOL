using DmxConsole.Application.Commands.Effects;
using DmxConsole.Application.Commands.Playback;
using DmxConsole.Core;
using DmxConsole.Core.Effects;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Tests;

public sealed class EffectCommandTests
{
    [Fact]
    public void CreateEffect_UndoRequiresDestructiveConfirmation()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(1);
        var effect = BuildEffect(context);

        var result = dispatcher.Dispatch(new CreateEffectCommand(effect));

        Assert.True(result.Success);
        Assert.Same(effect, result.Effect);
        Assert.Contains(effect, context.Effects.Effects);
        Assert.False(undoRedo.Undo().Performed);

        var option = undoRedo.PeekUndo()!.Options.Single(o => o.Risk == UndoRisk.Destructive);
        Assert.True(undoRedo.Undo(option.Id).Performed);
        Assert.DoesNotContain(effect, context.Effects.Effects);
    }

    [Fact]
    public void DeleteEffect_UndoRestoresAtOriginalPositionSafely()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(1);
        var first = BuildEffect(context, "First");
        var second = BuildEffect(context, "Second");
        context.Effects.Add(first);
        context.Effects.Add(second);

        var result = dispatcher.Dispatch(new DeleteEffectCommand(first));

        Assert.True(result.Success);
        Assert.Single(context.Effects.Effects);
        Assert.True(undoRedo.Undo().Performed);
        Assert.Same(first, context.Effects.Effects[0]);
    }

    [Fact]
    public void RuntimeActions_DoNotEnterUndoHistory_AndRateBumpsOwnership()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(1);
        var effect = BuildEffect(context);
        context.Effects.Add(effect);
        effect.Enabled = false;

        Assert.True(dispatcher.DispatchAction(new StartEffectAction(effect)).Success);
        Assert.True(effect.Enabled);
        Assert.False(undoRedo.CanUndo);
        Assert.True(effect.TryGetRevision(0, 0, out var startRevision));

        Assert.True(dispatcher.DispatchAction(new SetEffectRateAction(effect, 2.5)).Success);
        Assert.Equal(2.5, effect.SpeedHz);
        Assert.True(effect.TryGetRevision(0, 0, out var rateRevision));
        Assert.True(rateRevision > startRevision);
        Assert.False(undoRedo.CanUndo);

        Assert.True(dispatcher.DispatchAction(new StopEffectAction(effect)).Success);
        Assert.False(effect.Enabled);
        Assert.False(undoRedo.CanUndo);
    }

    [Fact]
    public void InvalidRate_FailsWithoutMutation()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(1);
        var effect = BuildEffect(context);
        context.Effects.Add(effect);

        var result = dispatcher.DispatchAction(new SetEffectRateAction(effect, -1));

        Assert.False(result.Success);
        Assert.Equal(1, effect.SpeedHz);
    }

    private static EffectPhaser BuildEffect(ConsoleContext context, string name = "Test") => new()
    {
        Name = name,
        Fixtures = new[] { context.Patch.Fixtures[0] },
        Steps = EffectPrimitiveBuilder.Build(EffectPrimitiveKind.Sine, ChannelType.Dimmer, 0, 255),
    };
}

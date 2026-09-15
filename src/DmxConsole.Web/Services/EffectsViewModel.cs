using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Application;
using DmxConsole.Application.Commands.Effects;
using DmxConsole.Application.Commands.Playback;
using DmxConsole.Core;
using DmxConsole.Core.Effects;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Web.Services;

/// <summary>
/// Builds and manages the running Effects: a "new effect" form (type + target fixture
/// group + parameters) plus commands to remove/toggle already-running ones.
/// </summary>
public partial class EffectsViewModel : ObservableObject
{
    private readonly Patch _patch;
    private readonly CommandDispatcher _dispatcher;

    public EffectBank EffectBank { get; }
    public ObservableCollection<EffectPhaser> Effects => EffectBank.Effects;
    public ObservableCollection<FixtureSelectionItem> AvailableFixtures { get; } = new();

    public string[] EffectTypeOptions { get; } = { "Chase", "Strobe", "Sine", "Rainbow" };
    public ChannelType[] TargetChannelOptions { get; } = Enum.GetValues<ChannelType>();

    [ObservableProperty] private string _selectedEffectType = "Chase";
    [ObservableProperty] private string _newEffectName = string.Empty;
    [ObservableProperty] private double _speedHz = 1.0;
    [ObservableProperty] private double _spread = 0.25;
    [ObservableProperty] private ChannelType _targetChannel = ChannelType.Dimmer;
    [ObservableProperty] private byte _min = 0;
    [ObservableProperty] private byte _max = 255;
    [ObservableProperty] private double _dutyCycle = 0.2;
    [ObservableProperty] private int _width = 1;
    [ObservableProperty] private double _brightness = 1.0;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public EffectsViewModel(Patch patch, EffectBank effectBank, CommandDispatcher dispatcher)
    {
        _patch = patch;
        EffectBank = effectBank;
        _dispatcher = dispatcher;
        RebuildFixtureList();
        _patch.Fixtures.CollectionChanged += (_, _) => RebuildFixtureList();
    }

    private void RebuildFixtureList()
    {
        var previouslySelected = AvailableFixtures.Where(f => f.IsSelected).Select(f => f.Fixture.Id).ToHashSet();
        AvailableFixtures.Clear();
        foreach (var fixture in _patch.Fixtures)
        {
            var item = new FixtureSelectionItem(fixture);
            if (previouslySelected.Contains(fixture.Id)) item.IsSelected = true;
            AvailableFixtures.Add(item);
        }
    }

    [RelayCommand]
    private void AddEffect()
    {
        var selected = AvailableFixtures.Where(f => f.IsSelected).Select(f => f.Fixture).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Select at least one fixture for the effect group first.";
            return;
        }

        IReadOnlyList<EffectStep> steps = SelectedEffectType switch
        {
            "Sine" => EffectPrimitiveBuilder.Build(EffectPrimitiveKind.Sine, TargetChannel, Min, Max),
            "Chase" => EffectPrimitiveBuilder.Build(EffectPrimitiveKind.Square, TargetChannel, Min, Max,
                dutyCyclePercent: Math.Clamp(Width, 1, selected.Count) * 100.0 / selected.Count),
            "Strobe" => EffectPrimitiveBuilder.Build(EffectPrimitiveKind.Square, TargetChannel, Min, Max,
                dutyCyclePercent: DutyCycle * 100),
            "Rainbow" => EffectPrimitiveBuilder.BuildRainbow(Brightness),
            _ => throw new InvalidOperationException($"Unknown effect type '{SelectedEffectType}'."),
        };

        var effect = new EffectPhaser
        {
            Name = string.IsNullOrWhiteSpace(NewEffectName) ? $"{SelectedEffectType} {Effects.Count + 1}" : NewEffectName,
            EffectType = SelectedEffectType,
            Fixtures = selected,
            Steps = steps,
            SpeedHz = SpeedHz,
            // Legacy Chase advanced exactly one fixture per step; the equivalent phaser offset
            // is one evenly-spread cycle over the selected group. Other primitives keep the
            // operator-entered Spread value.
            Spread = SelectedEffectType == "Chase" ? 1.0 / selected.Count : Spread,
        };

        var result = _dispatcher.Dispatch(new CreateEffectCommand(effect));
        StatusMessage = result.Success
            ? $"Added effect '{effect.Name}' on {selected.Count} fixture(s)."
            : result.Error ?? "Could not add effect.";
        if (result.Success) NewEffectName = string.Empty;
    }

    [RelayCommand]
    private void RemoveEffect(EffectPhaser? effect)
    {
        if (effect is null) return;
        var result = _dispatcher.Dispatch(new DeleteEffectCommand(effect));
        StatusMessage = result.Success ? $"Removed effect '{effect.Name}'." : result.Error ?? "Could not remove effect.";
    }

    public void SetEnabled(EffectPhaser effect, bool enabled)
    {
        var result = _dispatcher.DispatchAction(enabled
            ? new StartEffectAction(effect)
            : new StopEffectAction(effect));
        StatusMessage = result.Success ? $"Effect '{effect.Name}' {(enabled ? "started" : "stopped")}." : result.Error!;
    }

    public void SetSpeed(EffectPhaser effect, double speedHz)
    {
        var result = _dispatcher.DispatchAction(new SetEffectRateAction(effect, speedHz));
        StatusMessage = result.Success ? $"Effect '{effect.Name}' speed: {speedHz:0.##} Hz." : result.Error!;
    }
}

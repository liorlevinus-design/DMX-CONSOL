using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Core;
using DmxConsole.Core.Effects;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.App.ViewModels;

/// <summary>
/// Builds and manages the running Effects: a "new effect" form (type + target fixture
/// group + parameters) plus commands to remove/toggle already-running ones.
/// </summary>
public partial class EffectsViewModel : ObservableObject
{
    private readonly Patch _patch;

    public EffectsEngine EffectsEngine { get; }
    public ObservableCollection<Effect> Effects => EffectsEngine.Effects;
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

    public EffectsViewModel(Patch patch, EffectsEngine effectsEngine)
    {
        _patch = patch;
        EffectsEngine = effectsEngine;
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

        Effect effect = SelectedEffectType switch
        {
            "Sine" => new SineEffect { TargetChannel = TargetChannel, Min = Min, Max = Max },
            "Chase" => new ChaseEffect { TargetChannel = TargetChannel, OnValue = Max, OffValue = Min, Width = Width },
            "Strobe" => new StrobeEffect { TargetChannel = TargetChannel, OnValue = Max, OffValue = Min, DutyCycle = DutyCycle },
            "Rainbow" => new RainbowEffect { Brightness = Brightness },
            _ => throw new InvalidOperationException($"Unknown effect type '{SelectedEffectType}'."),
        };

        effect.Name = string.IsNullOrWhiteSpace(NewEffectName) ? $"{SelectedEffectType} {Effects.Count + 1}" : NewEffectName;
        effect.Fixtures = selected;
        effect.SpeedHz = SpeedHz;
        effect.Spread = Spread;

        Effects.Add(effect);
        StatusMessage = $"Added effect '{effect.Name}' on {selected.Count} fixture(s).";
        NewEffectName = string.Empty;
    }

    [RelayCommand]
    private void RemoveEffect(Effect? effect)
    {
        if (effect is null) return;
        Effects.Remove(effect);
    }
}

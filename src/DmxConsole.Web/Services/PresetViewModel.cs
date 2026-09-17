using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Application;
using DmxConsole.Application.Commands.Presets;
using DmxConsole.Core;
using DmxConsole.Core.Presets;

namespace DmxConsole.Web.Services;

/// <summary>
/// Drives the Preset/Palette library: a class picker (Intensity/Position/Color/Beam), the
/// list of presets in that class (Apply/Update/Remove), and a "record new" form. Store/Apply
/// always act on the console's current selection - same pattern as SelectionViewModel/
/// ProgrammerViewModel.
/// </summary>
public partial class PresetViewModel : ObservableObject
{
    private readonly ConsoleContext _context;
    private readonly CommandDispatcher _dispatcher;
    private readonly ProgrammerViewModel _programmerVm;

    public PresetLibrary Library => _context.Presets;

    public AttributeClass[] AttributeClassOptions { get; } =
    {
        AttributeClass.Intensity, AttributeClass.Position, AttributeClass.Color,
        AttributeClass.Beam, AttributeClass.Image, AttributeClass.Shape,
    };

    [ObservableProperty] private AttributeClass _selectedClass = AttributeClass.Color;
    [ObservableProperty] private string _newPresetName = string.Empty;
    [ObservableProperty] private Preset? _selectedPreset;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public IEnumerable<Preset> PresetsForSelectedClass => Library.ForClass(SelectedClass);

    public PresetViewModel(ConsoleContext context, CommandDispatcher dispatcher, ProgrammerViewModel programmerVm)
    {
        _context = context;
        _dispatcher = dispatcher;
        _programmerVm = programmerVm;
    }

    [RelayCommand]
    private void RecordNew()
    {
        var targets = _context.Selection.Items.ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Select at least one fixture first.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewPresetName)
            ? $"{SelectedClass} {Library.NextFreeNumber(SelectedClass)}"
            : NewPresetName;
        var number = Library.NextFreeNumber(SelectedClass);

        var result = _dispatcher.Dispatch(new StorePresetCommand(Library, targets, SelectedClass, name, number));
        StatusMessage = result.Success
            ? $"Recorded {SelectedClass} preset {result.Preset!.Number} - {result.Preset.Name}."
            : result.Error ?? "Failed.";

        NewPresetName = string.Empty;
    }

    [RelayCommand]
    private void UpdateSelected()
    {
        if (SelectedPreset is null) { StatusMessage = "Select a preset to update first."; return; }

        var targets = _context.Selection.Items.ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Select at least one fixture first.";
            return;
        }

        var result = _dispatcher.Dispatch(new StorePresetCommand(
            Library, targets, SelectedPreset.Class, SelectedPreset.Name, SelectedPreset.Number, SelectedPreset));
        StatusMessage = result.Success
            ? $"Updated {result.Preset!.Class} preset {result.Preset.Number}."
            : result.Error ?? "Failed.";
    }

    [RelayCommand]
    private void Apply(Preset preset)
    {
        var targets = _context.Selection.Items.ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Select at least one fixture first.";
            return;
        }

        var result = _dispatcher.Dispatch(new ApplyPresetCommand(targets, preset));
        StatusMessage = result.Success
            ? $"Applied {preset.Class} preset {preset.Number} to {result.AffectedFixtures.Count} fixture(s)."
            : result.Error ?? "Failed.";

        // Same fix as Step C1: a Programmer-writing command needs the fader display refreshed explicitly.
        if (result.Success) _programmerVm.RefreshAllFaders();
    }

    [RelayCommand]
    private void Remove(Preset preset)
    {
        var result = _dispatcher.Dispatch(new RemovePresetCommand(preset));
        if (!result.Success) StatusMessage = result.Error ?? "Could not remove that preset.";
        if (ReferenceEquals(SelectedPreset, preset)) SelectedPreset = null;
    }
}

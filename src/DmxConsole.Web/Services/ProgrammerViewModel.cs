using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Application;
using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Application.Live;
using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Web.Services;

/// <summary>
/// Drives Programmer-level operations (Release/Clear/Knockout/Restore/Adjust Intensity) on
/// the console's current selection, plus the shared LIVE filter state
/// (DmxConsole.Application.Live.LiveFilter) - the same enum and Matches() logic Channels/
/// Fixtures use, per OPERATOR_UX_ROADMAP.md §1-2: "don't duplicate business logic by building a
/// separate live model and programmer model in the UI." Every button here just builds the
/// matching IConsoleCommand and dispatches it - this class holds no DMX logic of its own.
/// </summary>
public partial class ProgrammerViewModel : ObservableObject
{
    private readonly ConsoleContext _context;
    private readonly CommandDispatcher _dispatcher;
    private readonly ObservableCollection<ChannelFaderViewModel> _faders;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private double _adjustPercent = 10;
    [ObservableProperty] private LiveFilter _liveFilter = LiveFilter.All;

    public ConsoleContext Context => _context;

    public ProgrammerViewModel(ConsoleContext context, CommandDispatcher dispatcher, ObservableCollection<ChannelFaderViewModel> faders)
    {
        _context = context;
        _dispatcher = dispatcher;
        _faders = faders;
    }

    private IReadOnlyList<PatchedFixture> Targets => _context.Selection.Items.ToList();

    [RelayCommand] private void Release() => Dispatch(new ReleaseCommand(Targets));
    [RelayCommand] private void ClearIntensity() => Dispatch(new ReleaseCommand(Targets, AttributeClass.Intensity));
    [RelayCommand] private void ClearPosition() => Dispatch(new ReleaseCommand(Targets, AttributeClass.Position));
    [RelayCommand] private void ClearColor() => Dispatch(new ReleaseCommand(Targets, AttributeClass.Color));
    [RelayCommand] private void ClearBeam() => Dispatch(new ReleaseCommand(Targets, AttributeClass.Beam));
    [RelayCommand] private void Knockout() => Dispatch(new KnockoutCommand(Targets));
    [RelayCommand] private void Restore() => Dispatch(new RestoreCommand(Targets));

    [RelayCommand]
    private void RaiseIntensity() => Dispatch(new AdjustIntensityCommand(Targets, AdjustOperation.Relative, AdjustPercent));

    [RelayCommand]
    private void LowerIntensity() => Dispatch(new AdjustIntensityCommand(Targets, AdjustOperation.Relative, -AdjustPercent));

    private void Dispatch(IConsoleCommand command)
    {
        if (Targets.Count == 0)
        {
            StatusMessage = "Select at least one fixture first.";
            return;
        }

        var result = _dispatcher.Dispatch(command);
        StatusMessage = result.Success
            ? $"{result.ActionType}: {result.AffectedFixtures.Count} fixture(s) affected."
            : result.Error ?? "Failed.";

        if (result.Success) RefreshAllFaders();
    }

    /// <summary>
    /// Re-reads every fader's displayed value from the Programmer. Needed because a Command
    /// (or an Undo/Redo of one) can change the Programmer directly, bypassing the fader's own
    /// drag-driven Value setter - without this, the slider position would silently go stale.
    /// A knocked-out channel still shows its remembered value here (HasStoredValue ignores
    /// knockout); a fully-released channel falls back to the fixture's channel default.
    /// </summary>
    public void RefreshAllFaders()
    {
        foreach (var fader in _faders)
        {
            fader.RefreshFromProgrammer(_context.Programmer.HasStoredValue(fader.UniverseId, fader.ChannelIndex, out var stored)
                ? stored
                : fader.Channel.DefaultValue);
        }
    }

    public void SetEncoderValue(ChannelType channelType, byte value)
    {
        if (Targets.Count == 0) return;
        Dispatch(new SetAttributeValueCommand(Targets, channelType, value));
    }
}

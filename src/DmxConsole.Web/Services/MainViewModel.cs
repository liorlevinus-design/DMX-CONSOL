using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Application;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;
using DmxConsole.Fixtures;
using DmxConsole.Protocols;
using DmxConsole.Protocols.ArtNet;
using DmxConsole.Protocols.Sacn;
using DmxConsole.Protocols.Usb;

namespace DmxConsole.Web.Services;

/// <summary>
/// The console's root state: patch, live Programmer, the DMX engine, and the network/USB
/// output fan-out. Registered as a singleton so every connected browser/tablet drives and
/// observes the same live show - exactly like a real console with multiple control surfaces.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    public Patch Patch { get; } = new();
    public Programmer Programmer { get; } = new();
    public DmxOutputEngine Engine { get; }
    public CueListViewModel CueListVm { get; }
    public EffectsViewModel EffectsVm { get; }
    public SelectionViewModel SelectionVm { get; }
    public ProgrammerViewModel ProgrammerVm { get; }
    public PresetViewModel PresetVm { get; }
    public ExecutorViewModel ExecutorVm { get; }

    private readonly UndoRedoService _undoRedo;

    public ObservableCollection<ChannelFaderViewModel> Faders { get; } = new();
    public IReadOnlyList<FixtureProfile> AvailableProfiles { get; } = GenericFixtureLibrary.All;

    private ArtNetSender? _artNetSender;
    private SacnSender? _sacnSender;
    private IDmxSender? _usbSender;

    [ObservableProperty] private FixtureProfile? _selectedProfile;
    [ObservableProperty] private FixtureMode? _selectedMode;
    [ObservableProperty] private int _newUniverseId;
    [ObservableProperty] private int _newStartAddress = 1;
    [ObservableProperty] private string _newFixtureName = string.Empty;

    public ObservableCollection<FixtureMode> AvailableModes { get; } = new();

    [ObservableProperty] private bool _isEngineRunning;
    [ObservableProperty] private bool _artNetEnabled;
    [ObservableProperty] private bool _sacnEnabled;
    [ObservableProperty] private string _artNetTargetIp = "255.255.255.255";
    [ObservableProperty] private string _statusMessage = "Ready.";

    public string[] UsbModeOptions { get; } = { "Enttec DMX USB PRO", "Enttec Open DMX USB" };
    public ObservableCollection<string> AvailableComPorts { get; } = new();

    [ObservableProperty] private bool _usbEnabled;
    [ObservableProperty] private string _usbMode = "Enttec DMX USB PRO";
    [ObservableProperty] private string? _selectedComPort;
    [ObservableProperty] private int _usbUniverseId;

    public MainViewModel()
    {
        Engine = new DmxOutputEngine(Patch);
        var presets = new PresetLibrary();
        var cueList = new CueList(presetResolver: presets); // resolves CueValue.PresetRef entries at playback
        var effectsEngine = new EffectsEngine();

        // Step F: the CueList is no longer registered with the engine directly - it's assigned
        // onto Executor 1 (the handle), which is what actually gets registered. FaderLevel
        // defaults to 1.0 and Flash to None, so this is a byte-for-byte passthrough of what the
        // CueList already produced - a pure wrapping change, zero behavior change.
        var executors = new ExecutorBank(channelTypeLookup: Patch);
        var mainExecutor = executors.Add(1);
        mainExecutor.Assign(cueList);
        Engine.AddLayer(mainExecutor);   // instead of Engine.AddLayer(cueList)
        Engine.AddLayer(effectsEngine); // priority 300 - above cues, below the live Programmer
        Engine.AddLayer(Programmer);
        Engine.UniverseOutputReady += OnUniverseOutputReady;

        var consoleContext = new ConsoleContext(Patch, Programmer, new FixtureSelection(), new GroupManager(), Engine, presets, executors);
        _undoRedo = new UndoRedoService(consoleContext);
        var dispatcher = new CommandDispatcher(consoleContext, _undoRedo);

        CueListVm = new CueListViewModel(Patch, Programmer, cueList, dispatcher, mainExecutor);
        EffectsVm = new EffectsViewModel(Patch, effectsEngine);
        SelectionVm = new SelectionViewModel(consoleContext, dispatcher);
        ProgrammerVm = new ProgrammerViewModel(consoleContext, dispatcher, Faders);
        PresetVm = new PresetViewModel(consoleContext, dispatcher, ProgrammerVm);
        ExecutorVm = new ExecutorViewModel(consoleContext, dispatcher, cueList);

        _artNetSender = new ArtNetSender();
        _sacnSender = new SacnSender();

        SelectedProfile = AvailableProfiles.FirstOrDefault();
        RefreshComPorts();
    }

    partial void OnSelectedProfileChanged(FixtureProfile? value)
    {
        AvailableModes.Clear();
        if (value is null) return;
        foreach (var mode in value.Modes) AvailableModes.Add(mode);
        SelectedMode = AvailableModes.FirstOrDefault();
    }

    [RelayCommand]
    private void AddFixture()
    {
        if (SelectedProfile is null || SelectedMode is null)
        {
            StatusMessage = "Select a fixture profile and mode first.";
            return;
        }

        try
        {
            var name = string.IsNullOrWhiteSpace(NewFixtureName) ? SelectedProfile.DisplayName : NewFixtureName;
            var fixture = new PatchedFixture(SelectedProfile, SelectedMode, NewUniverseId, NewStartAddress, name);
            Patch.Add(fixture);

            foreach (var channel in fixture.Mode.Channels)
                Faders.Add(new ChannelFaderViewModel(fixture, channel, Programmer));

            StatusMessage = $"Patched '{fixture.Name}' at Universe {fixture.UniverseId} / Address {fixture.StartAddress}.";

            // Bump the suggested next start address past this fixture's footprint, for a faster patch flow.
            NewStartAddress += fixture.Footprint;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void RemoveFixture(PatchedFixture? fixture)
    {
        if (fixture is null) return;
        Patch.Remove(fixture);
        var toRemove = Faders.Where(f => f.Fixture == fixture).ToList();
        foreach (var f in toRemove) Faders.Remove(f);
        StatusMessage = $"Removed '{fixture.Name}'.";
    }

    [RelayCommand]
    private void ClearProgrammer()
    {
        Programmer.ClearAll();
        foreach (var fader in Faders) fader.Value = fader.Channel.DefaultValue;
        StatusMessage = "Programmer cleared.";
    }

    /// <summary>Set when Undo() was blocked pending explicit confirmation of a destructive
    /// inverse (e.g. Undo-ing a freshly Created Executor/Group/Preset would delete it) - the
    /// toolbar shows a confirm/cancel prompt while this is non-null.</summary>
    [ObservableProperty] private UndoProposal? _pendingDestructiveUndo;

    [RelayCommand]
    private void Undo()
    {
        var outcome = _undoRedo.Undo();
        if (!outcome.Performed && outcome.Proposal is not null)
        {
            PendingDestructiveUndo = outcome.Proposal;
            StatusMessage = $"Undo requires confirmation: {outcome.Proposal.Description}";
            return;
        }

        ProgrammerVm.RefreshAllFaders();
        StatusMessage = outcome.Performed ? "Undo." : "Nothing to undo.";
    }

    /// <summary>Proceeds with the destructive option offered by the pending Undo prompt.</summary>
    [RelayCommand]
    private void ConfirmDestructiveUndo()
    {
        if (PendingDestructiveUndo is not { } proposal) return;
        var destructive = proposal.Options.FirstOrDefault(o => o.Risk == UndoRisk.Destructive);
        if (destructive is null) return;

        _undoRedo.Undo(destructive.Id);
        PendingDestructiveUndo = null;
        ProgrammerVm.RefreshAllFaders();
        StatusMessage = "Undo (confirmed).";
    }

    [RelayCommand]
    private void CancelDestructiveUndo()
    {
        PendingDestructiveUndo = null;
        StatusMessage = "Undo cancelled.";
    }

    [RelayCommand]
    private void Redo()
    {
        _undoRedo.Redo();
        ProgrammerVm.RefreshAllFaders();
        StatusMessage = "Redo.";
    }

    [RelayCommand]
    private void StartEngine()
    {
        Engine.Start();
        IsEngineRunning = true;
        StatusMessage = $"Engine running at {Engine.RefreshRateHz:0} Hz.";
    }

    [RelayCommand]
    private void StopEngine()
    {
        Engine.Stop();
        IsEngineRunning = false;
        StatusMessage = "Engine stopped.";
    }

    [RelayCommand]
    private void ApplyArtNetTarget()
    {
        try
        {
            var address = IPAddress.Parse(ArtNetTargetIp);
            _artNetSender?.Dispose();
            _artNetSender = new ArtNetSender(address);
            StatusMessage = $"Art-Net target set to {address}.";
        }
        catch (FormatException)
        {
            StatusMessage = $"'{ArtNetTargetIp}' is not a valid IP address.";
        }
    }

    [RelayCommand]
    private void RefreshComPorts()
    {
        var current = SelectedComPort;
        AvailableComPorts.Clear();
        foreach (var port in EnttecProSender.ListAvailablePorts().OrderBy(p => p)) AvailableComPorts.Add(port);
        SelectedComPort = current is not null && AvailableComPorts.Contains(current) ? current : AvailableComPorts.FirstOrDefault();
    }

    [RelayCommand]
    private void ApplyUsbTarget()
    {
        _usbSender?.Dispose();
        _usbSender = null;

        if (!UsbEnabled) { StatusMessage = "USB-DMX disabled."; return; }

        if (string.IsNullOrEmpty(SelectedComPort))
        {
            StatusMessage = "Select a COM port for the USB-DMX widget first.";
            UsbEnabled = false;
            return;
        }

        try
        {
            _usbSender = UsbMode == "Enttec Open DMX USB"
                ? new EnttecOpenDmxSender(SelectedComPort, UsbUniverseId)
                : new EnttecProSender(SelectedComPort, UsbUniverseId);
            StatusMessage = $"{UsbMode} connected on {SelectedComPort}, carrying Universe {UsbUniverseId}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open {SelectedComPort}: {ex.Message}";
            UsbEnabled = false;
            _usbSender = null;
        }
    }

    private void OnUniverseOutputReady(int universeId, byte[] data)
    {
        // Runs on the engine's own background thread - senders are just UDP/serial writes, no UI access here.
        if (ArtNetEnabled)
        {
            try { _artNetSender?.Send(universeId, data); } catch { /* best-effort network I/O */ }
        }

        if (SacnEnabled)
        {
            try { _sacnSender?.Send(universeId, data); } catch { /* best-effort network I/O */ }
        }

        if (UsbEnabled)
        {
            try { _usbSender?.Send(universeId, data); } catch { /* best-effort serial I/O */ }
        }
    }

    public void Dispose()
    {
        Engine.Stop();
        Engine.UniverseOutputReady -= OnUniverseOutputReady;
        Engine.Dispose();
        _artNetSender?.Dispose();
        _sacnSender?.Dispose();
        _usbSender?.Dispose();
        CueListVm.Dispose();
    }
}

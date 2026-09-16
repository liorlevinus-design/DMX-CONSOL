using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Application;
using DmxConsole.Application.Live;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Effects;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;
using DmxConsole.Fixtures;
using DmxConsole.Protocols;
using DmxConsole.Protocols.ArtNet;
using DmxConsole.Protocols.Sacn;
using DmxConsole.Protocols.Usb;
using DmxConsole.Web.EditorToolBar;

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
    public GroupsViewModel GroupsVm { get; }
    public CommandSurfaceViewModel CommandSurfaceVm { get; }
    public EncoderDrawerViewModel EncoderDrawerVm { get; }

    /// <summary>The ONE shared operator context (H1.6 §19) - View row clicks, the Command
    /// Surface, and EditorToolBarVm all read/write this same instance. Never construct a second
    /// one anywhere.</summary>
    public EditorContextStack EditorContext { get; } = new();

    public EditorToolBarViewModel EditorToolBarVm { get; }

    private readonly UndoRedoService _undoRedo;

    public ObservableCollection<ChannelFaderViewModel> Faders { get; } = new();
    public IReadOnlyList<FixtureProfile> AvailableProfiles { get; } = GenericFixtureLibrary.All;

    private ArtNetSender? _artNetSender;
    private SacnSender? _sacnSender;
    private IDmxSender? _usbSender;

    [ObservableProperty] private FixtureProfile? _selectedProfile;
    [ObservableProperty] private FixtureMode? _selectedMode;

    /// <summary>Explicit, operator-controlled starting fixture Number for the next patch batch -
    /// pre-filled with a suggestion (the next free number) but never auto-assigned silently;
    /// see AddFixture's own doc comment for why this is validated up front rather than falling
    /// back to Patch.Add's own auto-numbering.</summary>
    [ObservableProperty] private int _newFixtureNumber = 1;

    /// <summary>How many fixtures this single patch action creates, numbered
    /// NewFixtureNumber..NewFixtureNumber+NewFixtureCount-1 - "fast batch patching" per the
    /// UX correction, not one form submission per fixture.</summary>
    [ObservableProperty] private int _newFixtureCount = 1;

    [ObservableProperty] private int _newUniverseId;
    [ObservableProperty] private int _newStartAddress = 1;

    /// <summary>Address spacing between consecutive fixtures in a batch - 0 means "pack them
    /// back-to-back using the mode's own footprint" (today's implicit behavior, now an explicit,
    /// visible, overridable field instead of a hidden assumption). A truss of movers spaced 20
    /// channels apart for future expansion is "Offset: 20", not something the operator has no
    /// way to ask for.</summary>
    [ObservableProperty] private int _newAddressOffset;

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

    /// <summary>The operator's chosen LIVE filter for Channels/Fixtures - held here (the one
    /// long-lived singleton every view shares), not as local Razor component state, because
    /// Blazor recreates a pane's inactive-tab components (including their private fields) every
    /// time the operator switches away and back - a local field would silently reset to All on
    /// every revisit. Defaults to All from first load, per the roadmap's own requirement that the
    /// initial visit stays unaffected by this fix.</summary>
    [ObservableProperty] private LiveFilter _channelsLiveFilter = LiveFilter.All;
    [ObservableProperty] private LiveFilter _fixturesLiveFilter = LiveFilter.All;

    public MainViewModel()
    {
        Engine = new DmxOutputEngine(Patch);
        var presets = new PresetLibrary();
        var cueList = new CueList(presetResolver: presets); // resolves CueValue.PresetRef entries at playback
        var effects = new EffectBank(Engine);

        // Step F: the CueList is no longer registered with the engine directly - it's assigned
        // onto Executor 1 (the handle), which is what actually gets registered. FaderLevel
        // defaults to 1.0 and Flash to None, so this is a byte-for-byte passthrough of what the
        // CueList already produced - a pure wrapping change, zero behavior change.
        var executors = new ExecutorBank(channelTypeLookup: Patch);
        var mainExecutor = executors.Add(1);
        mainExecutor.Assign(cueList);
        Engine.AddLayer(mainExecutor);   // instead of Engine.AddLayer(cueList)
        Engine.AddLayer(Programmer);
        Engine.UniverseOutputReady += OnUniverseOutputReady;

        var consoleContext = new ConsoleContext(Patch, Programmer, new FixtureSelection(), new GroupManager(), Engine, presets, executors, effects);
        _undoRedo = new UndoRedoService(consoleContext);
        var dispatcher = new CommandDispatcher(consoleContext, _undoRedo);

        CueListVm = new CueListViewModel(Patch, Programmer, consoleContext.Selection, Engine, cueList, dispatcher, mainExecutor);
        EffectsVm = new EffectsViewModel(Patch, effects, dispatcher);
        SelectionVm = new SelectionViewModel(consoleContext, dispatcher);
        ProgrammerVm = new ProgrammerViewModel(consoleContext, dispatcher, Faders);
        PresetVm = new PresetViewModel(consoleContext, dispatcher, ProgrammerVm);
        ExecutorVm = new ExecutorViewModel(consoleContext, dispatcher, cueList);
        GroupsVm = new GroupsViewModel(consoleContext, dispatcher);
        CommandSurfaceVm = new CommandSurfaceViewModel(consoleContext, dispatcher, EditorContext);
        EditorToolBarVm = new EditorToolBarViewModel(EditorContext, SoftKeyRegistryBuilder.Build(), this, CueListVm, GroupsVm, SelectionVm);
        EncoderDrawerVm = new EncoderDrawerViewModel(dispatcher, ProgrammerVm);

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
        NewFixtureNumber = Patch.SuggestNextNumber();
    }

    partial void OnSelectedModeChanged(FixtureMode? value)
    {
        // A visible, overridable default - "pack them back-to-back" - not a silent assumption:
        // the field is right there in the UI for the operator to change to any spacing.
        if (value is not null) NewAddressOffset = value.FootprintSize;
    }

    /// <summary>
    /// Patches NewFixtureCount fixtures at once, numbered NewFixtureNumber..+Count-1, spaced
    /// NewAddressOffset DMX channels apart starting at NewStartAddress - all operator-controlled,
    /// explicit fields (the UX correction: numbering must never appear to "just happen"
    /// automatically). Every requested fixture Number is validated as free BEFORE any fixture is
    /// created - Patch.Add's own fallback (silently renumbering onto the next free number if a
    /// requested one is taken) is deliberately bypassed here, since a console operator asking for
    /// fixture #105 must get #105 or an explicit conflict, never a silent substitute. The whole
    /// batch is all-or-nothing: an address-overlap failure partway through rolls back everything
    /// already patched in this call, so "patch 8" never leaves 3 half-applied on failure.
    /// </summary>
    [RelayCommand]
    private void AddFixture()
    {
        if (SelectedProfile is null || SelectedMode is null)
        {
            StatusMessage = "Select a fixture profile and mode first.";
            return;
        }

        int count = Math.Max(1, NewFixtureCount);
        int addressOffset = NewAddressOffset > 0 ? NewAddressOffset : SelectedMode.FootprintSize;

        var requestedNumbers = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            int number = NewFixtureNumber + i;
            if (Patch.FindByNumber(number) is not null)
            {
                StatusMessage = $"Fixture #{number} is already patched - choose a different starting number or count.";
                return;
            }
            requestedNumbers.Add(number);
        }

        var patchedFixtures = new List<PatchedFixture>(count);
        try
        {
            string baseName = string.IsNullOrWhiteSpace(NewFixtureName) ? SelectedProfile.DisplayName : NewFixtureName;

            for (int i = 0; i < count; i++)
            {
                int address = NewStartAddress + i * addressOffset;
                string name = count == 1 ? baseName : $"{baseName} {i + 1}";

                var fixture = new PatchedFixture(SelectedProfile, SelectedMode, NewUniverseId, address, name) { Number = requestedNumbers[i] };
                Patch.Add(fixture);
                patchedFixtures.Add(fixture);

                foreach (var channel in fixture.Mode.Channels)
                    Faders.Add(new ChannelFaderViewModel(fixture, channel, Programmer));
            }

            StatusMessage = count == 1
                ? $"Patched '{patchedFixtures[0].Name}' (#{patchedFixtures[0].Number}) at Universe {NewUniverseId} / Address {NewStartAddress}."
                : $"Patched {count} fixtures: #{requestedNumbers[0]}-#{requestedNumbers[^1]}, Universe {NewUniverseId}, addresses {NewStartAddress}-{NewStartAddress + (count - 1) * addressOffset} (offset {addressOffset}).";

            // Bump suggestions for the next batch - fast batch patching stays fast.
            NewFixtureNumber += count;
            NewStartAddress += count * addressOffset;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            foreach (var fixture in patchedFixtures)
            {
                Patch.Remove(fixture);
                foreach (var f in Faders.Where(fv => fv.Fixture == fixture).ToList()) Faders.Remove(f);
            }
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
        foreach (var fader in Faders) fader.RefreshFromProgrammer(fader.Channel.DefaultValue);
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

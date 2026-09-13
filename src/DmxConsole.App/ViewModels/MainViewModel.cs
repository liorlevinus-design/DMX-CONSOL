using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Fixtures;
using DmxConsole.Protocols.ArtNet;
using DmxConsole.Protocols.Sacn;

namespace DmxConsole.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public Patch Patch { get; } = new();
    public Programmer Programmer { get; } = new();
    public DmxOutputEngine Engine { get; }
    public CueListViewModel CueListVm { get; }

    public ObservableCollection<ChannelFaderViewModel> Faders { get; } = new();
    public IReadOnlyList<FixtureProfile> AvailableProfiles { get; } = GenericFixtureLibrary.All;

    private ArtNetSender? _artNetSender;
    private SacnSender? _sacnSender;

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

    public MainViewModel()
    {
        Engine = new DmxOutputEngine(Patch);
        var cueList = new CueList();
        Engine.AddLayer(cueList); // below the Programmer, so live fader grabs still win
        Engine.AddLayer(Programmer);
        Engine.UniverseOutputReady += OnUniverseOutputReady;

        CueListVm = new CueListViewModel(Patch, Programmer, cueList);

        _artNetSender = new ArtNetSender();
        _sacnSender = new SacnSender();

        SelectedProfile = AvailableProfiles.FirstOrDefault();
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

    private void OnUniverseOutputReady(int universeId, byte[] data)
    {
        // Runs on the engine's own background thread - senders are just UDP writes, no UI access here.
        if (ArtNetEnabled)
        {
            try { _artNetSender?.Send(universeId, data); } catch { /* best-effort network I/O */ }
        }

        if (SacnEnabled)
        {
            try { _sacnSender?.Send(universeId, data); } catch { /* best-effort network I/O */ }
        }
    }

    public void Dispose()
    {
        Engine.Stop();
        Engine.UniverseOutputReady -= OnUniverseOutputReady;
        Engine.Dispose();
        _artNetSender?.Dispose();
        _sacnSender?.Dispose();
        CueListVm.Dispose();
    }
}

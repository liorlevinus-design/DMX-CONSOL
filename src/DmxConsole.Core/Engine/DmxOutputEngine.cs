using System.Collections.Concurrent;
using System.Diagnostics;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Engine;

/// <summary>
/// The heart of the console: on a fixed-rate background loop, merges every active
/// <see cref="IOutputLayer"/> (Programmer, CueList, Effects, ...) into one buffer per
/// universe, applies per-fixture pan/tilt calibration, and publishes the result via
/// <see cref="UniverseOutputReady"/> for protocol senders to transmit.
/// </summary>
public sealed class DmxOutputEngine : IDisposable
{
    private readonly Patch _patch;
    private readonly ConcurrentDictionary<int, Universe> _universes = new();
    private readonly List<IOutputLayer> _layers = new();
    private readonly object _layersLock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private PatchChannelMap _channelMap;
    private readonly byte[] _scratch = new byte[Core.Universe.ChannelCount];

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>How many times per second the engine recomputes and publishes output. 40Hz is the DMX512 refresh convention.</summary>
    public double RefreshRateHz { get; }

    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <summary>Raised on every tick with a fresh 512-byte snapshot for one universe.</summary>
    public event Action<int, byte[]>? UniverseOutputReady;

    public DmxOutputEngine(Patch patch, double refreshRateHz = 40.0)
    {
        _patch = patch ?? throw new ArgumentNullException(nameof(patch));
        RefreshRateHz = refreshRateHz;
        _channelMap = new PatchChannelMap(_patch);
        _patch.Fixtures.CollectionChanged += (_, _) => RebuildChannelMap();
        foreach (var id in _patch.UsedUniverseIds) EnsureUniverse(id);
    }

    public Universe EnsureUniverse(int universeId) =>
        _universes.GetOrAdd(universeId, id => new Universe(id));

    public IReadOnlyCollection<Universe> Universes => _universes.Values.ToList();

    public void RebuildChannelMap()
    {
        var map = new PatchChannelMap(_patch);
        foreach (var id in map.UniverseIds) EnsureUniverse(id);
        _channelMap = map;
    }

    public void AddLayer(IOutputLayer layer)
    {
        lock (_layersLock)
        {
            _layers.Add(layer);
            _layers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }
    }

    public void RemoveLayer(IOutputLayer layer)
    {
        lock (_layersLock) { _layers.Remove(layer); }
    }

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _loopTask?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { /* expected on cancel */ }
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        var period = TimeSpan.FromSeconds(1.0 / RefreshRateHz);
        using var timer = new PeriodicTimer(period);
        while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            Tick();
        }
    }

    /// <summary>Runs one merge+publish cycle synchronously. Public so tests and the UI can force a deterministic tick.</summary>
    public void Tick()
    {
        List<IOutputLayer> layersSnapshot;
        lock (_layersLock) { layersSnapshot = new List<IOutputLayer>(_layers); }

        var elapsed = _clock.Elapsed;
        foreach (var layer in layersSnapshot)
        {
            if (layer is ITickable tickable) tickable.Tick(elapsed);
        }

        var activeLayers = layersSnapshot.Where(l => l.IsActive).ToList();

        foreach (var universe in _universes.Values)
        {
            ComputeUniverse(universe.Id, activeLayers, _scratch);
            universe.CopyFrom(_scratch);
            UniverseOutputReady?.Invoke(universe.Id, universe.Snapshot());
        }
    }

    private void ComputeUniverse(int universeId, List<IOutputLayer> activeLayers, byte[] destination)
    {
        Array.Clear(destination);

        for (int channel = 0; channel < Core.Universe.ChannelCount; channel++)
        {
            bool isPatched = _channelMap.TryGet(universeId, channel, out var entry);
            byte baseValue = isPatched ? entry.Channel.DefaultValue : (byte)0;
            var blendMode = isPatched ? entry.Channel.EffectiveBlendMode : BlendMode.Ltp;

            byte result = baseValue;
            bool anyLayerContributed = false;

            foreach (var layer in activeLayers)
            {
                if (!layer.TryGetChannelValue(universeId, channel, out var contributed)) continue;

                if (blendMode == BlendMode.Htp)
                {
                    result = Math.Max(result, contributed);
                }
                else // Ltp: last (highest-priority, since activeLayers is priority-sorted) contribution wins
                {
                    result = contributed;
                }
                anyLayerContributed = true;
            }

            if (!anyLayerContributed) result = baseValue;

            destination[channel] = result;
        }

        // Second pass: apply pan/tilt calibration per patched fixture (needs the merged
        // raw value of both channels, so it runs after the generic merge above).
        foreach (var fixture in _patch.InUniverse(universeId))
        {
            var pan = fixture.FindChannel(ChannelType.Pan);
            var tilt = fixture.FindChannel(ChannelType.Tilt);
            if (pan is null || tilt is null) continue;

            int panIdx = fixture.AbsoluteIndex(pan);
            int tiltIdx = fixture.AbsoluteIndex(tilt);
            byte panRaw = destination[panIdx];
            byte tiltRaw = destination[tiltIdx];

            if (fixture.Calibration.SwapPanTilt) (panRaw, tiltRaw) = (tiltRaw, panRaw);

            destination[panIdx] = fixture.Calibration.ApplyPan(panRaw);
            destination[tiltIdx] = fixture.Calibration.ApplyTilt(tiltRaw);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

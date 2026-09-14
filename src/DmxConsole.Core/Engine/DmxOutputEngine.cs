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
public sealed class DmxOutputEngine : IDisposable, IEffectiveOutputReader
{
    private readonly Patch _patch;
    private readonly ConcurrentDictionary<int, Universe> _universes = new();
    private readonly List<IOutputLayer> _layers = new();
    private readonly object _layersLock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>Which layer produced the currently-winning value for each channel, as of the most
    /// recent Tick() - see TryGetOwningLayer/GetOwner. Populated as a side effect of the same
    /// merge loop that already computes the value, no extra pass.</summary>
    private readonly ConcurrentDictionary<(int Universe, int Channel), IOutputLayer?> _owningLayer = new();

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

    /// <summary>
    /// The merged value the most recent Tick() actually computed for one channel - see
    /// <see cref="IEffectiveOutputReader"/>. Deliberately a read-only lookup (no
    /// EnsureUniverse side effect): a universe nothing has ever ticked simply reads as 0.
    /// </summary>
    public byte GetEffectiveValue(int universeId, int channelIndex) =>
        _universes.TryGetValue(universeId, out var universe) ? universe[channelIndex] : (byte)0;

    /// <summary>The raw IOutputLayer reference that produced the currently-winning value for a
    /// channel, as of the most recent Tick() - null/false if nothing has contributed to it yet.
    /// The layer reference itself is a stable identity (no new property forced onto Programmer/
    /// EffectsEngine just for this). Prefer <see cref="GetOwner"/> for a structured, presentation-
    /// independent identity.</summary>
    public bool TryGetOwningLayer(int universeId, int channelIndex, out IOutputLayer? layer) =>
        _owningLayer.TryGetValue((universeId, channelIndex), out layer) && layer is not null;

    /// <summary>Structured ownership identity for a channel - see OutputOwner. Pattern-matches on
    /// the winning layer's concrete type (Executor carries its own stable Id/Number; Programmer/
    /// EffectsEngine are singletons in this app) rather than trusting a mutable display Name.</summary>
    public OutputOwner? GetOwner(int universeId, int channelIndex)
    {
        if (!TryGetOwningLayer(universeId, channelIndex, out var layer) || layer is null) return null;

        return layer switch
        {
            Executor exec => new OutputOwner($"executor:{exec.Id:N}",
                exec.Name.Length > 0 ? exec.Name : $"Executor {exec.Number}", OwnerKind.Executor, exec.Id, exec.Number),
            Programmer => new OutputOwner("programmer", "Programmer", OwnerKind.Programmer, null, null),
            EffectsEngine fx => new OutputOwner("effects", fx.Name, OwnerKind.Effect, null, null),
            _ => new OutputOwner(layer.Name, layer.Name, OwnerKind.Other, null, null),
        };
    }

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
        var priorityTiers = activeLayers
            .GroupBy(layer => layer.Priority)
            .OrderBy(group => group.Key)
            .Select(group => group.ToArray())
            .ToArray();

        for (int channel = 0; channel < Core.Universe.ChannelCount; channel++)
        {
            bool isPatched = _channelMap.TryGet(universeId, channel, out var entry);
            byte baseValue = isPatched ? entry.Channel.DefaultValue : (byte)0;
            var blendMode = isPatched ? entry.Channel.EffectiveBlendMode : BlendMode.Ltp;

            byte result = baseValue;
            bool anyLayerContributed = false;
            IOutputLayer? winningLayer = null;

            // Process a whole priority tier at a time. Every base-aware contributor in a tier
            // receives the same immutable result of STRICTLY lower tiers; contributors at the
            // same priority never become one another's base. Each layer is still sampled once
            // per channel per merge computation.
            foreach (var tier in priorityTiers)
            {
                byte baseBelowTier = result;
                bool tierContributed = false;
                byte tierResult = baseBelowTier;
                long tierWinningRevision = long.MinValue;
                IOutputLayer? tierWinningLayer = null;

                foreach (var layer in tier)
                {
                    bool contributed = layer is IBaseAwareLayer baseAware
                        ? baseAware.TryGetChannelValue(universeId, channel, baseBelowTier, out var value)
                        : layer.TryGetChannelValue(universeId, channel, out value);
                    if (!contributed) continue;

                    if (blendMode == BlendMode.Htp)
                    {
                        if (!tierContributed || value > tierResult)
                        {
                            tierResult = value;
                            tierWinningLayer = layer;
                        }
                    }
                    else
                    {
                        long revision = (layer as IMergeAwareLayer)?.TryGetRevision(
                            universeId, channel, out var r) == true ? r : 0;
                        if (!tierContributed || revision >= tierWinningRevision)
                        {
                            tierResult = value;
                            tierWinningRevision = revision;
                            tierWinningLayer = layer;
                        }
                    }

                    tierContributed = true;
                }

                if (tierContributed)
                {
                    result = tierResult;
                    winningLayer = tierWinningLayer;
                    anyLayerContributed = true;
                }
            }

            if (!anyLayerContributed) result = baseValue;
            _owningLayer[(universeId, channel)] = anyLayerContributed ? winningLayer : null;

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

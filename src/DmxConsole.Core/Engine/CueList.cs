using System.Collections.ObjectModel;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;

namespace DmxConsole.Core.Engine;

/// <summary>
/// An ordered list of Cues with Go/Back playback: fades from wherever the output
/// currently sits into the target cue's recorded levels, using that cue's fade-in
/// time (plus delay) for channels going up and fade-out time (plus delay) for channels
/// going down. Sits as an <see cref="IOutputLayer"/> below the Programmer, so live fader
/// grabs still win. Implements <see cref="ITickable"/> to auto-advance a Follow-mode cue
/// once its WaitTime elapses (Vector's FOLLOW ON).
/// </summary>
public sealed class CueList : IOutputLayer, IPlaybackSource, ISequencedPlayback, IPausablePlayback, IMergeAwareLayer, ITickable
{
    private readonly object _lock = new();
    private readonly Dictionary<(int Universe, int Channel), byte> _fadeFrom = new();
    private readonly Dictionary<(int Universe, int Channel), byte> _currentOutput = new();

    /// <summary>Guards Tick()'s Follow auto-advance so it fires exactly once per cue arrival -
    /// re-armed in StartTransitionTo, cleared the moment it fires.</summary>
    private bool _followArmed;

    /// <summary>Per-channel semantic revision - see IMergeAwareLayer. Updated in StartTransitionTo:
    /// every channel present in the newly-active cue's Levels gets the SAME new revision (they all
    /// received their instruction from the same single Go/Back/GoToCue event). Today (no Tracking
    /// yet - RecordCue always full-snapshots every patched channel per Step E) this means every
    /// patched channel's revision bumps on every Go, which is correct right now since every stored
    /// channel genuinely is a fresh instruction under full-snapshot recording. Once Tracking exists
    /// and a cue's Levels only contains channels that actually got a new move (others inherited),
    /// this exact same per-key update naturally bumps only the channels really touched - no
    /// merge-system rewrite needed when Tracking arrives.</summary>
    private readonly Dictionary<(int Universe, int Channel), long> _channelRevisions = new();

    private Cue? _currentCue;
    private bool _isReleased = true;
    private DateTime _fadeStartUtc;
    private DateTime? _pausedAtUtc;
    private TimeSpan _accumulatedPause;
    private readonly IPresetResolver? _presetResolver;

    public string Name { get; }
    public int Priority { get; }

    public ObservableCollection<Cue> Cues { get; } = new();

    /// <summary>Raised after any playback or list change the UI should refresh for.</summary>
    public event Action? Changed;

    /// <summary>presetResolver resolves CueValue.PresetRef entries at playback time (never cached - a
    /// Preset update or deletion is reflected on the very next tick). Optional and defaults to null so
    /// existing callers (and every current test) are unaffected; without one, PresetRef entries simply
    /// never contribute, same as any other unresolvable channel.</summary>
    public CueList(string name = "Cue List", int priority = 150, IPresetResolver? presetResolver = null)
    {
        Name = name;
        Priority = priority;
        _presetResolver = presetResolver;
    }

    public bool IsActive
    {
        get { lock (_lock) { return !_isReleased && _currentCue is not null; } }
    }

    public Cue? CurrentCue { get { lock (_lock) { return _currentCue; } } }

    /// <summary>-1 when nothing has ever played.</summary>
    public int CurrentCueIndex
    {
        get
        {
            lock (_lock)
            {
                return _currentCue is null ? -1 : Cues.IndexOf(_currentCue);
            }
        }
    }

    /// <summary>Records the console's current live state as a new cue, per the given
    /// <see cref="CueStoreFilter"/> (Vector's five STORE OPTIONS meanings - see CueStoreFilter's
    /// own doc comment). Every channel is stored as an Absolute CueValue - the Programmer has no
    /// concept of "this value came from a Preset" (by design, see Step C1), so a plain recording
    /// can never produce a PresetRef; use <see cref="RecordCueWithPresetRefs"/> for that.</summary>
    public Cue RecordCue(Patch patch, Programmer programmer, Selection.FixtureSelection selection,
        IEffectiveOutputReader effectiveOutput, string name, double number, CueStoreOptions options)
        => RecordCue(patch, programmer, selection, effectiveOutput, name, number, options, presetOverrides: null);

    /// <summary>Like <see cref="RecordCue"/>, but for fixtures whose AttributeClass appears in
    /// <paramref name="presetOverrides"/> and whose channel ChannelType is present in that Preset's
    /// Values, stores a live PresetRef instead of an Absolute byte. Channels not covered by an override
    /// (or whose ChannelType the override preset doesn't contain) are recorded as Absolute exactly as
    /// before - so one Cue can freely mix Absolute and PresetRef entries.</summary>
    public Cue RecordCueWithPresetRefs(Patch patch, Programmer programmer, Selection.FixtureSelection selection,
        IEffectiveOutputReader effectiveOutput, string name, double number, CueStoreOptions options,
        IReadOnlyDictionary<AttributeClass, Presets.Preset> presetOverrides)
        => RecordCue(patch, programmer, selection, effectiveOutput, name, number, options, presetOverrides);

    private Cue RecordCue(Patch patch, Programmer programmer, Selection.FixtureSelection selection,
        IEffectiveOutputReader effectiveOutput, string name, double number, CueStoreOptions options,
        IReadOnlyDictionary<AttributeClass, Presets.Preset>? presetOverrides)
    {
        var cue = BuildCue(patch, programmer, selection, effectiveOutput, name, number, options, presetOverrides);
        InsertSorted(cue);
        Changed?.Invoke();
        return cue;
    }

    /// <summary>True if any of this fixture's Intensity-class channels currently reads above zero
    /// in effectiveOutput - the "dimmer above zero" gate STORE OPTIONS' two ALL PARAMS variants
    /// use. A fixture with no Intensity channel at all can never be gated by a dimmer that doesn't
    /// exist - it passes unconditionally (documented, not a silent exclusion).</summary>
    private static bool HasIntensityAboveZero(PatchedFixture fixture, IEffectiveOutputReader effectiveOutput)
    {
        var intensityChannels = fixture.ChannelsForAttribute(AttributeClass.Intensity).ToList();
        if (intensityChannels.Count == 0) return true;
        return intensityChannels.Any(c => effectiveOutput.GetEffectiveValue(fixture.UniverseId, fixture.AbsoluteIndex(c)) > 0);
    }

    private static bool IsActiveForStore(PatchedFixture fixture, Programmer programmer, HashSet<PatchedFixture> selected,
        IEffectiveOutputReader effectiveOutput)
    {
        if (selected.Contains(fixture)) return true;
        foreach (var channel in fixture.Mode.Channels)
        {
            int idx = fixture.AbsoluteIndex(channel);
            if (programmer.HasStoredValue(fixture.UniverseId, idx, out _)) return true;
            if (effectiveOutput.GetEffectiveValue(fixture.UniverseId, idx) > 0) return true;
        }
        return false;
    }

    private static Cue BuildCue(Patch patch, Programmer programmer, Selection.FixtureSelection selection,
        IEffectiveOutputReader effectiveOutput, string name, double number, CueStoreOptions options,
        IReadOnlyDictionary<AttributeClass, Presets.Preset>? presetOverrides)
    {
        var levels = new Dictionary<(int, int), CueValue>();
        var selected = new HashSet<PatchedFixture>(selection.Items);

        foreach (var fixture in patch.Fixtures)
        {
            bool isSelected = selected.Contains(fixture);

            bool includeFixture = options.Filter switch
            {
                CueStoreFilter.AllStage => true,
                CueStoreFilter.AllEditor => true, // per-channel Programmer-touch gate applied below
                CueStoreFilter.ActiveOnly => isSelected, // per-channel Programmer-touch gate applied below
                CueStoreFilter.AllParamsForSelected => isSelected && HasIntensityAboveZero(fixture, effectiveOutput),
                CueStoreFilter.AllParamsIfActive => IsActiveForStore(fixture, programmer, selected, effectiveOutput)
                    && HasIntensityAboveZero(fixture, effectiveOutput),
                _ => true,
            };
            if (!includeFixture) continue;

            bool requireProgrammerTouch = options.Filter is CueStoreFilter.AllEditor or CueStoreFilter.ActiveOnly;
            bool readLiveMerged = options.Filter is CueStoreFilter.AllParamsForSelected or CueStoreFilter.AllParamsIfActive;

            foreach (var channel in fixture.Mode.Channels)
            {
                int idx = fixture.AbsoluteIndex(channel);
                var key = (fixture.UniverseId, idx);

                if (requireProgrammerTouch && !programmer.HasStoredValue(fixture.UniverseId, idx, out _)) continue;

                if (presetOverrides is not null
                    && presetOverrides.TryGetValue(channel.Type.ToAttributeClass(), out var preset)
                    && preset.Values.ContainsKey(channel.Type))
                {
                    levels[key] = CueValue.FromPreset(channel.Type, preset.Id);
                    continue;
                }

                byte value = readLiveMerged
                    ? effectiveOutput.GetEffectiveValue(fixture.UniverseId, idx)
                    : programmer.TryGetChannelValue(fixture.UniverseId, idx, out var live) ? live : channel.DefaultValue;
                levels[key] = CueValue.Absolute(channel.Type, value);
            }
        }

        return new Cue
        {
            Number = number,
            Name = name,
            Timing = options.Timing,
            TriggerMode = options.TriggerMode,
            WaitTime = options.WaitTime,
            Levels = levels,
        };
    }

    /// <summary>Looks up a Cue by its exact stored Number - the "does Cue N already exist"
    /// question the Store/Update workflow needs to answer explicitly, never guessed.</summary>
    public Cue? FindByNumber(double number) => Cues.FirstOrDefault(c => c.Number == number);

    /// <summary>Replaces an already-recorded Cue's captured levels/name/timing in place - the
    /// "Update Cue N" workflow (as opposed to RecordCue, which always creates a new Cue object).
    /// The Cue's list position and Number are preserved; if it happens to be the cue currently
    /// playing/active, the live pointer is updated too so playback doesn't go stale referencing
    /// a replaced object. Returns null (no mutation) if the given Cue is no longer in this list -
    /// an explicit, checkable failure rather than silently doing nothing.</summary>
    public Cue? UpdateCue(Cue existing, Patch patch, Programmer programmer, Selection.FixtureSelection selection,
        IEffectiveOutputReader effectiveOutput, string name, CueStoreOptions options)
    {
        Cue updated;
        lock (_lock)
        {
            int index = Cues.IndexOf(existing);
            if (index < 0) return null;

            updated = BuildCue(patch, programmer, selection, effectiveOutput, name, existing.Number, options, presetOverrides: null);
            Cues[index] = updated;
            if (ReferenceEquals(_currentCue, existing)) _currentCue = updated;
        }

        Changed?.Invoke();
        return updated;
    }

    /// <summary>Toggles an already-recorded Cue's FOLLOW ON / MANUAL trigger mode in place - no-op
    /// (returns false) if the Cue is no longer in this list.</summary>
    public bool SetTriggerMode(Cue cue, CueTriggerMode mode)
    {
        lock (_lock)
        {
            if (!Cues.Contains(cue)) return false;
            cue.TriggerMode = mode;
        }
        Changed?.Invoke();
        return true;
    }

    private void InsertSorted(Cue cue)
    {
        int i = 0;
        while (i < Cues.Count && Cues[i].Number < cue.Number) i++;
        Cues.Insert(i, cue);
    }

    public void RemoveCue(Cue cue)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_currentCue, cue))
            {
                _currentCue = null;
                _isReleased = true;
            }
        }
        Cues.Remove(cue);
        Changed?.Invoke();
    }

    /// <summary>Advances to the next cue in the list, fading from the current output.</summary>
    public void Go()
    {
        lock (_lock)
        {
            int idx = _currentCue is null ? -1 : Cues.IndexOf(_currentCue);
            int next = idx + 1;
            if (next < 0 || next >= Cues.Count) return;
            StartTransitionTo(Cues[next]);
        }
        Changed?.Invoke();
    }

    /// <summary>Returns to the previous cue in the list, fading from the current output.</summary>
    public void Back()
    {
        lock (_lock)
        {
            int idx = _currentCue is null ? -1 : Cues.IndexOf(_currentCue);
            int prev = idx - 1;
            if (prev < 0) return;
            StartTransitionTo(Cues[prev]);
        }
        Changed?.Invoke();
    }

    public void GoToCue(Cue cue)
    {
        lock (_lock)
        {
            if (!Cues.Contains(cue)) return;
            StartTransitionTo(cue);
        }
        Changed?.Invoke();
    }

    /// <summary>Releases the cue list's contribution entirely - channels fall back to lower layers/defaults.</summary>
    public void Stop()
    {
        lock (_lock) { _isReleased = true; }
        Changed?.Invoke();
    }

    /// <summary>ITickable: Vector's FOLLOW ON - once a Follow-mode cue's WaitTime has elapsed
    /// since arrival, advance to the next cue automatically, without waiting for GO. Fires exactly
    /// once per cue arrival (_followArmed), never repeatedly for the same cue, and does nothing at
    /// all for a Manual-mode cue, while paused, or while released.</summary>
    public void Tick(TimeSpan elapsed)
    {
        bool shouldAdvance;
        lock (_lock)
        {
            shouldAdvance = _followArmed && !_isReleased && _currentCue is not null && _pausedAtUtc is null
                && _currentCue.TriggerMode == CueTriggerMode.Follow
                && EffectiveElapsed() >= _currentCue.WaitTime;
            if (shouldAdvance) _followArmed = false;
        }
        if (shouldAdvance) Go();
    }

    private void StartTransitionTo(Cue target)
    {
        _fadeFrom.Clear();
        foreach (var key in target.Levels.Keys)
            _fadeFrom[key] = _currentOutput.TryGetValue(key, out var v) ? v : (byte)0;

        _currentCue = target;
        _isReleased = false;
        _fadeStartUtc = DateTime.UtcNow;
        _pausedAtUtc = null;
        _accumulatedPause = TimeSpan.Zero;
        _followArmed = true;

        // Every channel in this cue received its instruction from this SAME Go/Back/GoToCue event -
        // they share one revision value (see the field's doc comment for why this is correct today
        // and stays correct once Tracking exists).
        long newRevision = RevisionClock.Next();
        foreach (var key in target.Levels.Keys)
            _channelRevisions[key] = newRevision;
    }

    /// <summary>Wall-clock elapsed since the fade started, minus any time spent paused (including
    /// time currently being spent paused, if paused right now) - the single place both the fade
    /// interpolation and status reporting read "how much time has really passed".</summary>
    private TimeSpan EffectiveElapsed()
    {
        var now = DateTime.UtcNow;
        var paused = _accumulatedPause;
        if (_pausedAtUtc is { } pausedAt) paused += now - pausedAt;
        var elapsed = now - _fadeStartUtc - paused;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    public bool IsPaused { get { lock (_lock) { return _pausedAtUtc is not null; } } }

    /// <summary>Freezes the running fade (and status) in place. No-op if already paused or nothing
    /// is playing - graceful, never throws.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_currentCue is null || _pausedAtUtc is not null) return;
            _pausedAtUtc = DateTime.UtcNow;
        }
        Changed?.Invoke();
    }

    /// <summary>Continues the fade from exactly where Pause() froze it - not from the start, not
    /// skipping the paused duration. No-op if not currently paused.</summary>
    public void Resume()
    {
        lock (_lock)
        {
            if (_pausedAtUtc is not { } pausedAt) return;
            _accumulatedPause += DateTime.UtcNow - pausedAt;
            _pausedAtUtc = null;
        }
        Changed?.Invoke();
    }

    /// <summary>Overall fade progress (0..1) and time remaining for the active transition - for a UI progress bar.</summary>
    public (double Progress, TimeSpan Remaining) GetTransitionStatus()
    {
        lock (_lock)
        {
            if (_currentCue is null) return (1.0, TimeSpan.Zero);
            // Worst case of the two directions (each including its own delay) - a per-channel-accurate
            // progress bar would need every channel's own direction; this is a reasonable overall estimate.
            var timing = _currentCue.Timing;
            var inTotal = timing.DelayIn + timing.TimeIn;
            var outTotal = timing.DelayOut + timing.TimeOut;
            var maxDuration = inTotal > outTotal ? inTotal : outTotal;
            double elapsed = EffectiveElapsed().TotalSeconds;
            double progress = maxDuration.TotalSeconds <= 0
                ? 1.0
                : Math.Clamp(elapsed / maxDuration.TotalSeconds, 0.0, 1.0);
            var remaining = TimeSpan.FromSeconds(Math.Max(0, maxDuration.TotalSeconds - elapsed));
            return (progress, remaining);
        }
    }

    /// <summary>Structured, time-first introspection snapshot (UX_PHILOSOPHY §9). Overall is the
    /// worst-case of the two directions; FadeIn/FadeOut are each direction's own Delay+Time progress
    /// separately - the real distinction left once timing is one flat set per Cue (H1.6 Slice 2),
    /// replacing the old per-AttributeClass breakdown that no longer has anything to differentiate.</summary>
    public PlaybackStatus GetStatus()
    {
        lock (_lock)
        {
            if (_currentCue is null)
            {
                var zero = new TimingProgress(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
                return new CueListPlaybackStatus(null, null, false, false, zero, zero, zero);
            }

            var elapsed = EffectiveElapsed();
            var timing = _currentCue.Timing;
            var fadeIn = ProgressFor(timing.DelayIn, timing.TimeIn, elapsed);
            var fadeOut = ProgressFor(timing.DelayOut, timing.TimeOut, elapsed);
            var overall = fadeIn.Total > fadeOut.Total ? fadeIn : fadeOut;

            bool isRunning = !_isReleased && _pausedAtUtc is null;
            return new CueListPlaybackStatus(_currentCue, null, isRunning, _pausedAtUtc is not null, overall, fadeIn, fadeOut);
        }
    }

    private static TimingProgress ProgressFor(TimeSpan delay, TimeSpan duration, TimeSpan elapsed)
    {
        var total = delay + duration;
        var clampedElapsed = elapsed > total ? total : elapsed;
        var remaining = total - clampedElapsed;
        return new TimingProgress(clampedElapsed, remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining, total);
    }

    /// <summary>See IMergeAwareLayer - false for a channel no recorded cue has ever stored.</summary>
    public bool TryGetRevision(int universeId, int channelIndex, out long revision) =>
        _channelRevisions.TryGetValue((universeId, channelIndex), out revision);

    public bool TryGetChannelValue(int universeId, int channelIndex, out byte value)
    {
        lock (_lock)
        {
            if (_isReleased || _currentCue is null || _pausedAtUtc is not null)
            {
                // Paused: freeze at whatever _currentOutput last held for this channel (if any) -
                // no exception, no recompute, no progression while frozen.
                if (_pausedAtUtc is not null && _currentOutput.TryGetValue((universeId, channelIndex), out var frozen))
                {
                    value = frozen;
                    return true;
                }
                value = 0;
                return false;
            }

            var key = (universeId, channelIndex);
            if (!_currentCue.Levels.TryGetValue(key, out var cueValue))
            {
                value = 0;
                return false;
            }

            // Resolved fresh every tick (never cached): a PresetRef that no longer resolves (Preset
            // deleted, or it doesn't contain this ChannelType) means this channel simply doesn't
            // contribute this tick - same as the key-not-found branch above, never a thrown exception
            // or a stale fabricated value.
            if (!cueValue.TryResolve(_presetResolver, out var target))
            {
                value = 0;
                return false;
            }

            byte from = _fadeFrom.TryGetValue(key, out var f) ? f : (byte)0;
            double elapsedSeconds = EffectiveElapsed().TotalSeconds;
            var timing = _currentCue.Timing;
            bool goingUp = target >= from;
            var delay = goingUp ? timing.DelayIn : timing.DelayOut;
            var duration = goingUp ? timing.TimeIn : timing.TimeOut;
            double afterDelaySeconds = elapsedSeconds - delay.TotalSeconds;
            double t = afterDelaySeconds <= 0
                ? 0.0
                : duration.TotalSeconds <= 0
                    ? 1.0
                    : Math.Clamp(afterDelaySeconds / duration.TotalSeconds, 0.0, 1.0);

            byte result = (byte)Math.Round(from + (target - from) * t);
            _currentOutput[key] = result;
            value = result;
            return true;
        }
    }
}

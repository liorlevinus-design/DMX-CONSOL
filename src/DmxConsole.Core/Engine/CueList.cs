using System.Collections.ObjectModel;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;

namespace DmxConsole.Core.Engine;

/// <summary>
/// An ordered list of Cues with Go/Back playback: fades from wherever the output
/// currently sits into the target cue's recorded levels, using that cue's fade-in
/// time (plus delay) for channels going up and fade-out time (plus delay) for channels
/// going down. Sits as an <see cref="IOutputLayer"/> below the Programmer, so live fader
/// grabs still win. Implements <see cref="ITickable"/> to auto-advance once the CURRENT cue's
/// own transition fully completes, per the NEXT cue's own Trigger (AutoFollow correction slice -
/// see Tick()'s own doc comment for the full state machine; Trigger is never a property a cue
/// applies to itself).
/// </summary>
public sealed class CueList : IOutputLayer, IPlaybackSource, ISequencedPlayback, IPausablePlayback, IMergeAwareLayer, ITickable
{
    private readonly object _lock = new();
    private readonly Dictionary<(int Universe, int Channel), byte> _fadeFrom = new();
    private readonly Dictionary<(int Universe, int Channel), byte> _currentOutput = new();

    /// <summary>AutoFollow correction slice: true only when the CURRENT transition arrived via
    /// genuine forward playback progression (CueTransitionReason.Go, non-instant) - set in
    /// StartTransitionTo, cleared the moment an auto-advance actually fires. Gates whether THIS
    /// transition's eventual completion is even allowed to trigger consulting the NEXT cue's own
    /// Trigger (see Tick()'s own doc comment for the full state machine) - never whether this cue
    /// auto-advances itself, which was the wrong model. BACK/GO TO/SHIFT-navigation all leave this
    /// false, so landing on a cue that way can never seed an automatic chain, regardless of that
    /// cue's own Trigger or the next cue's.</summary>
    private bool _chainEligible;

    /// <summary>Set ONCE, the first tick where the current transition's own fade is detected as
    /// fully complete - the value of the SAME pause-aware EffectiveElapsed() clock the fade itself
    /// uses, at that instant. The WAIT trigger's timer is measured from THIS anchor, never from
    /// _fadeStartUtc directly - which is exactly what keeps it from running concurrently with the
    /// previous cue's own fade (a confirmed bug in the old model). Null again after the next
    /// StartTransitionTo.</summary>
    private double? _transitionCompletedAtElapsedSeconds;

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

    /// <summary>SHIFT+GO/SHIFT+BACK slice: true for the CURRENT transition only when it was an
    /// explicit zero-time jump - set in StartTransitionTo, orthogonal to CueTransitionReason (a
    /// transition has both a WHY and a HOW). When true, TryGetChannelValue/GetStatus/
    /// GetTransitionStatus all skip fade interpolation entirely and report the target cue as
    /// already fully applied - never a second "instant cue application" code path.</summary>
    private bool _instantTransition;

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

                // Parameter TIME slice (CLAUDE.md §16/ROADMAP §9a): whatever per-channel TimeIn/
                // TimeOut override the operator programmed (independent of whether this channel's
                // VALUE was itself touched) rides along into the stored CueValue - captured
                // regardless of the Store filter's value-inclusion rules, since a channel included
                // here for its VALUE may carry a timing override set at a different moment.
                programmer.TryGetTiming(fixture.UniverseId, idx, out var timeInOverride, out var timeOutOverride);

                if (presetOverrides is not null
                    && presetOverrides.TryGetValue(channel.Type.ToAttributeClass(), out var preset)
                    && preset.Values.ContainsKey(channel.Type))
                {
                    levels[key] = CueValue.FromPreset(channel.Type, preset.Id, timeInOverride, timeOutOverride);
                    continue;
                }

                byte value = readLiveMerged
                    ? effectiveOutput.GetEffectiveValue(fixture.UniverseId, idx)
                    : programmer.TryGetChannelValue(fixture.UniverseId, idx, out var live) ? live : channel.DefaultValue;
                levels[key] = CueValue.Absolute(channel.Type, value, timeInOverride, timeOutOverride);
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

    /// <summary>Every raw (universe, channel) address any Cue in this list stores a value for -
    /// the "Used in Show" query's raw material (see DmxConsole.Application.Live.ShowUsageQuery).
    /// A plain union of every Cue's own Levels.Keys, recomputed on every call rather than cached -
    /// this list can be edited/recorded into at any time and the answer must never go stale.
    /// Deliberately raw-address, not fixture-aware: resolving back to fixtures is the caller's
    /// job (via Patch), keeping this type free of any dependency on Patch.</summary>
    public IReadOnlySet<(int Universe, int Channel)> ReferencedAddresses()
    {
        var addresses = new HashSet<(int, int)>();
        foreach (var cue in Cues)
            foreach (var key in cue.Levels.Keys)
                addresses.Add(key);
        return addresses;
    }

    /// <summary>Replaces an already-recorded Cue's captured levels/name/timing in place - the
    /// "Update Cue N" workflow (as opposed to RecordCue, which always creates a new Cue object).
    /// The Cue's list position and Number are preserved; if it happens to be the cue currently
    /// playing/active, the live pointer is updated too so playback doesn't go stale referencing
    /// a replaced object. Returns null (no mutation) if the given Cue is no longer in this list -
    /// an explicit, checkable failure rather than silently doing nothing.</summary>
    public Cue? UpdateCue(Cue existing, Patch patch, Programmer programmer, Selection.FixtureSelection selection,
        IEffectiveOutputReader effectiveOutput, string name, CueStoreOptions options)
    {
        var updated = BuildCue(patch, programmer, selection, effectiveOutput, name, existing.Number, options, presetOverrides: null);
        return ReplaceCue(existing, updated) ? updated : null;
    }

    /// <summary>Low-level swap: replaces whichever Cue object currently occupies <paramref
    /// name="replaceThis"/>'s slot in the list with <paramref name="replacement"/>, preserving
    /// list position and updating the live playback pointer if <paramref name="replaceThis"/>
    /// happens to be the currently active Cue (so playback never goes stale referencing a
    /// replaced object). This is the same primitive <see cref="UpdateCue"/> uses internally, and
    /// is also exactly what UpdateCueCommand's Undo needs: since UpdateCue never mutates its input
    /// Cue (only ever swaps it out for a freshly-built one), putting the ORIGINAL object back is
    /// just this same swap run in the other direction. Returns false (no mutation) if <paramref
    /// name="replaceThis"/> is no longer in this list.</summary>
    public bool ReplaceCue(Cue replaceThis, Cue replacement)
    {
        lock (_lock)
        {
            int index = Cues.IndexOf(replaceThis);
            if (index < 0) return false;

            Cues[index] = replacement;
            if (ReferenceEquals(_currentCue, replaceThis)) _currentCue = replacement;
        }

        Changed?.Invoke();
        return true;
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

    /// <summary>Sets an already-recorded Cue's flat In/Out fade timing in place (Command Surface's
    /// CUE ... TIME ... grammar) - same shape as <see cref="SetTriggerMode"/>, no-op (returns
    /// false) if the Cue is no longer in this list. Never touches TriggerMode/WaitTime - Cue
    /// Trigger Semantics are a completely separate concern (CLAUDE.md §9), untouched by this
    /// method. The new CueTiming is whatever the caller built (typically the existing Timing with
    /// only TimeIn/TimeOut replaced, DelayIn/DelayOut preserved) - this method performs no
    /// validation of its own, same as SetTriggerMode.</summary>
    public bool SetTiming(Cue cue, CueTiming timing)
    {
        lock (_lock)
        {
            if (!Cues.Contains(cue)) return false;
            cue.Timing = timing;
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

    /// <summary>Advances to the next cue in the list. Forward playback progression - eligible to
    /// arm AutoFollow on the target cue, UNLESS <paramref name="instant"/> is true (SHIFT+GO): a
    /// zero-time jump straight to the target cue's values, no fade, and - per the explicit
    /// authoritative rule - never arms AutoFollow even though it's still forward progression in
    /// every other sense (index-wise, this is identical to a normal GO).</summary>
    public void Go(bool instant = false)
    {
        lock (_lock)
        {
            int idx = _currentCue is null ? -1 : Cues.IndexOf(_currentCue);
            int next = idx + 1;
            if (next < 0 || next >= Cues.Count) return;
            StartTransitionTo(Cues[next], CueTransitionReason.Go, instant);
        }
        Changed?.Invoke();
    }

    /// <summary>Returns to the previous cue in the list. Never forward progression - never arms
    /// AutoFollow on the target cue, regardless of its own TriggerMode. <paramref name="instant"/>
    /// true (SHIFT+BACK) makes it a zero-time jump, no fade - existing Back/Pause semantics
    /// otherwise unchanged.</summary>
    public void Back(bool instant = false)
    {
        lock (_lock)
        {
            int idx = _currentCue is null ? -1 : Cues.IndexOf(_currentCue);
            int prev = idx - 1;
            if (prev < 0) return;
            StartTransitionTo(Cues[prev], CueTransitionReason.Back, instant);
        }
        Changed?.Invoke();
    }

    /// <summary>Jumps directly to an arbitrary cue (GO TO CUE X) - direct navigation, not forward
    /// progression. Holds on the target even if it's AutoFollow; a subsequent GO resumes normal
    /// forward playback from there.</summary>
    public void GoToCue(Cue cue)
    {
        lock (_lock)
        {
            if (!Cues.Contains(cue)) return;
            StartTransitionTo(cue, CueTransitionReason.Jump);
        }
        Changed?.Invoke();
    }

    /// <summary>Releases the cue list's contribution entirely - channels fall back to lower layers/defaults.</summary>
    public void Stop()
    {
        lock (_lock) { _isReleased = true; }
        Changed?.Invoke();
    }

    /// <summary>
    /// AutoFollow correction slice: Trigger belongs to the NEXT/TARGET cue, describing how IT is
    /// entered once the CURRENT cue's own transition has fully completed - never a property the
    /// current cue applies to itself. State machine, evaluated every tick while a chain-eligible
    /// transition is active:
    ///
    ///   1. Not chain-eligible (arrived via BACK/GO TO/SHIFT-instant), released, or paused ->
    ///      do nothing at all, regardless of anyone's Trigger.
    ///   2. Chain-eligible, but the current cue's OWN fade hasn't finished yet -> do nothing yet
    ///      (never race the fade - this is the bug the old model had).
    ///   3. Current cue's fade just finished (detected once) -> record the completion instant on
    ///      the SAME pause-aware clock the fade uses (_transitionCompletedAtElapsedSeconds) - the
    ///      anchor every WAIT timer measures from, so it can never overlap the fade.
    ///   4. Look at the NEXT cue (Cues[index+1]) - not the current one - and its OWN TriggerMode:
    ///        Manual     -> hold; do nothing further this transition.
    ///        AutoFollow -> fire Go() on the very next tick after completion (WaitTime irrelevant).
    ///        Wait       -> fire Go() once (now - completion instant) >= next cue's own WaitTime.
    ///
    /// Firing Go() re-enters StartTransitionTo with reason=Go, which naturally makes the NEW
    /// current cue chain-eligible again too - a MANUAL -> AF -> WAIT 5 -> AF chain therefore keeps
    /// advancing correctly from a single initial GO, without Tick() needing any notion of "am I
    /// still inside a chain" beyond this one flag re-arming itself each hop.
    /// </summary>
    public void Tick(TimeSpan elapsed)
    {
        bool shouldAdvance;
        lock (_lock)
        {
            shouldAdvance = false;
            if (_chainEligible && !_isReleased && _currentCue is not null && _pausedAtUtc is null
                && CurrentTransitionHasCompletedUnlocked())
            {
                _transitionCompletedAtElapsedSeconds ??= EffectiveElapsed().TotalSeconds;

                int idx = Cues.IndexOf(_currentCue);
                var next = idx >= 0 && idx + 1 < Cues.Count ? Cues[idx + 1] : null;
                if (next is not null)
                {
                    shouldAdvance = next.TriggerMode switch
                    {
                        CueTriggerMode.AutoFollow => true,
                        CueTriggerMode.Wait => EffectiveElapsed().TotalSeconds - _transitionCompletedAtElapsedSeconds.Value >= next.WaitTime.TotalSeconds,
                        _ => false, // Manual - hold, wait for an explicit GO
                    };
                }
            }
            if (shouldAdvance) _chainEligible = false;
        }
        if (shouldAdvance) Go();
    }

    /// <summary>True once the CURRENT cue's own fade has reached 100% progress (or was instant) -
    /// the same "is this transition finished" question GetTransitionStatus()'s Progress==1.0
    /// already answers, factored out so Tick() can ask it without re-entering the public,
    /// re-locking API. Must be called with _lock already held.</summary>
    private bool CurrentTransitionHasCompletedUnlocked()
    {
        if (_currentCue is null) return false;
        if (_instantTransition) return true;

        return EffectiveElapsed().TotalSeconds >= EffectiveTransitionDurationUnlocked().TotalSeconds;
    }

    /// <summary>
    /// Parameter TIME slice (CLAUDE.md §16/ROADMAP §9a): the TRUE overall transition duration this
    /// tick must wait for before the cue is allowed to count as complete - the Cue-level
    /// CueTiming.TimeIn/TimeOut/DelayIn/DelayOut baseline, widened to also cover the LONGEST
    /// per-channel TimeIn/TimeOut override actually present on this cue. This is the single place
    /// completion is computed - both <see cref="CurrentTransitionHasCompletedUnlocked"/> (which
    /// gates AutoFollow/Wait/chain-advance in Tick()) and <see cref="GetTransitionStatus"/> read
    /// the SAME value, so a per-parameter override can never let AutoFollow/Wait fire, or a
    /// progress bar report 100%, before the slowest overridden channel has genuinely finished -
    /// CLAUDE.md's Cue Trigger Semantics (§9) apply to the REAL completion instant, never a
    /// Cue-level-only approximation once overrides exist. Delay is never overridden per-channel
    /// (only TimeIn/TimeOut are, by design - see CueValue's own doc comment), so each channel's
    /// own direction still uses the Cue's flat DelayIn/DelayOut. Must be called with _lock held.
    /// </summary>
    private TimeSpan EffectiveTransitionDurationUnlocked()
    {
        if (_currentCue is null) return TimeSpan.Zero;

        var timing = _currentCue.Timing;
        var inTotal = timing.DelayIn + timing.TimeIn;
        var outTotal = timing.DelayOut + timing.TimeOut;
        var maxDuration = inTotal > outTotal ? inTotal : outTotal;

        foreach (var (key, cueValue) in _currentCue.Levels)
        {
            if (cueValue.TimeInOverride is null && cueValue.TimeOutOverride is null) continue;
            if (!cueValue.TryResolve(_presetResolver, out var target)) continue;

            byte from = _fadeFrom.TryGetValue(key, out var f) ? f : (byte)0;
            bool goingUp = target >= from;
            var delay = goingUp ? timing.DelayIn : timing.DelayOut;
            var duration = goingUp ? (cueValue.TimeInOverride ?? timing.TimeIn) : (cueValue.TimeOutOverride ?? timing.TimeOut);
            var total = delay + duration;
            if (total > maxDuration) maxDuration = total;
        }

        return maxDuration;
    }

    /// <summary>AutoFollow correction slice: chain-eligibility must only be granted when this
    /// transition arrived via FORWARD PLAYBACK PROGRESSION (a live GO, or an AutoFollow/Wait-fired
    /// chained advance - Tick() calls Go() for that, so it naturally reuses CueTransitionReason.Go,
    /// never a separate value) - never merely because a cue became current. BACK and GO TO/Jump
    /// both hold on arrival and can never seed a chain, regardless of the target cue's own
    /// TriggerMode or the NEXT cue's.
    ///
    /// SHIFT+GO/SHIFT+BACK slice: <paramref name="instant"/> is a SECOND, orthogonal dimension -
    /// WHY (reason) is normally what governs chain-eligibility, HOW (instant) governs fade timing.
    /// A reason of Go is still forward progression in every structural sense (same next-cue index
    /// as a normal GO), but the authoritative rule is explicit that an instant jump must never
    /// seed a chain even then - so both must be true for _chainEligible to be set.</summary>
    private void StartTransitionTo(Cue target, CueTransitionReason reason, bool instant = false)
    {
        _fadeFrom.Clear();
        foreach (var key in target.Levels.Keys)
            _fadeFrom[key] = _currentOutput.TryGetValue(key, out var v) ? v : (byte)0;

        _currentCue = target;
        _isReleased = false;
        _fadeStartUtc = DateTime.UtcNow;
        _pausedAtUtc = null;
        _accumulatedPause = TimeSpan.Zero;
        _instantTransition = instant;
        _chainEligible = reason == CueTransitionReason.Go && !instant;
        _transitionCompletedAtElapsedSeconds = null;

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
            // SHIFT+GO/SHIFT+BACK: an instant jump has no transition to report progress on - it
            // is already fully complete the instant it happens, never a misleading "0%, N seconds
            // remaining" for a fade that will never actually run.
            if (_instantTransition) return (1.0, TimeSpan.Zero);
            // Worst case of the two directions (each including its own delay), WIDENED to also
            // cover any per-channel Parameter TIME override (EffectiveTransitionDurationUnlocked) -
            // so this can never report 100%/zero-remaining before the slowest overridden channel
            // has actually finished (CLAUDE.md's "must not lie about completion"). A per-channel-
            // accurate progress BREAKDOWN (which channel is at what %) is a separate, larger UI
            // concern intentionally left as a follow-up - only the overall completion instant is
            // guaranteed correct here.
            var maxDuration = EffectiveTransitionDurationUnlocked();
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
            TimingProgress fadeIn, fadeOut;
            if (_instantTransition)
            {
                // Already fully complete - Elapsed == Total, Remaining == Zero, for both
                // directions, regardless of the cue's own recorded Timing (never actually used).
                fadeIn = new TimingProgress(timing.DelayIn + timing.TimeIn, TimeSpan.Zero, timing.DelayIn + timing.TimeIn);
                fadeOut = new TimingProgress(timing.DelayOut + timing.TimeOut, TimeSpan.Zero, timing.DelayOut + timing.TimeOut);
            }
            else
            {
                fadeIn = ProgressFor(timing.DelayIn, timing.TimeIn, elapsed);
                fadeOut = ProgressFor(timing.DelayOut, timing.TimeOut, elapsed);
            }
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

            byte result;
            if (_instantTransition)
            {
                // SHIFT+GO/SHIFT+BACK: no fade - the target value IS the output, from the very
                // first read after the transition, unconditionally (never delay/duration-gated).
                result = target;
            }
            else
            {
                byte from = _fadeFrom.TryGetValue(key, out var f) ? f : (byte)0;
                double elapsedSeconds = EffectiveElapsed().TotalSeconds;
                var timing = _currentCue.Timing;
                bool goingUp = target >= from;
                var delay = goingUp ? timing.DelayIn : timing.DelayOut;
                // Parameter TIME slice: this channel's own TimeIn/TimeOut override (if any) wins
                // over the Cue-level flat timing for its direction - delay is never overridden
                // per-channel (see CueValue's own doc comment), only the duration.
                var duration = goingUp ? (cueValue.TimeInOverride ?? timing.TimeIn) : (cueValue.TimeOutOverride ?? timing.TimeOut);
                double afterDelaySeconds = elapsedSeconds - delay.TotalSeconds;
                double t = afterDelaySeconds <= 0
                    ? 0.0
                    : duration.TotalSeconds <= 0
                        ? 1.0
                        : Math.Clamp(afterDelaySeconds / duration.TotalSeconds, 0.0, 1.0);

                result = (byte)Math.Round(from + (target - from) * t);
            }

            _currentOutput[key] = result;
            value = result;
            return true;
        }
    }
}

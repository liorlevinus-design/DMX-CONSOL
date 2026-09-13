using System.Collections.ObjectModel;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Engine;

/// <summary>
/// An ordered list of Cues with Go/Back playback: fades from wherever the output
/// currently sits into the target cue's recorded levels, using that cue's fade-in
/// time for channels going up and fade-out time for channels going down. Sits as an
/// <see cref="IOutputLayer"/> below the Programmer, so live fader grabs still win.
/// </summary>
public sealed class CueList : IOutputLayer
{
    private readonly object _lock = new();
    private readonly Dictionary<(int Universe, int Channel), byte> _fadeFrom = new();
    private readonly Dictionary<(int Universe, int Channel), byte> _currentOutput = new();

    private Cue? _currentCue;
    private bool _isReleased = true;
    private DateTime _fadeStartUtc;

    public string Name { get; }
    public int Priority { get; }

    public ObservableCollection<Cue> Cues { get; } = new();

    /// <summary>Raised after any playback or list change the UI should refresh for.</summary>
    public event Action? Changed;

    public CueList(string name = "Cue List", int priority = 150)
    {
        Name = name;
        Priority = priority;
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

    /// <summary>Records the console's current live state (Programmer, falling back to fixture defaults) as a new cue.</summary>
    public Cue RecordCue(Patch patch, Programmer programmer, string name, double number, TimeSpan fadeInTime, TimeSpan fadeOutTime)
    {
        var levels = new Dictionary<(int, int), byte>();
        foreach (var fixture in patch.Fixtures)
        {
            foreach (var channel in fixture.Mode.Channels)
            {
                int idx = fixture.AbsoluteIndex(channel);
                byte value = programmer.TryGetChannelValue(fixture.UniverseId, idx, out var live)
                    ? live
                    : channel.DefaultValue;
                levels[(fixture.UniverseId, idx)] = value;
            }
        }

        var cue = new Cue
        {
            Number = number,
            Name = name,
            FadeInTime = fadeInTime,
            FadeOutTime = fadeOutTime,
            Levels = levels,
        };

        InsertSorted(cue);
        Changed?.Invoke();
        return cue;
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

    private void StartTransitionTo(Cue target)
    {
        _fadeFrom.Clear();
        foreach (var key in target.Levels.Keys)
            _fadeFrom[key] = _currentOutput.TryGetValue(key, out var v) ? v : (byte)0;

        _currentCue = target;
        _isReleased = false;
        _fadeStartUtc = DateTime.UtcNow;
    }

    /// <summary>Overall fade progress (0..1) and time remaining for the active transition - for a UI progress bar.</summary>
    public (double Progress, TimeSpan Remaining) GetTransitionStatus()
    {
        lock (_lock)
        {
            if (_currentCue is null) return (1.0, TimeSpan.Zero);
            var maxDuration = _currentCue.FadeInTime > _currentCue.FadeOutTime ? _currentCue.FadeInTime : _currentCue.FadeOutTime;
            double elapsed = (DateTime.UtcNow - _fadeStartUtc).TotalSeconds;
            double progress = maxDuration.TotalSeconds <= 0
                ? 1.0
                : Math.Clamp(elapsed / maxDuration.TotalSeconds, 0.0, 1.0);
            var remaining = TimeSpan.FromSeconds(Math.Max(0, maxDuration.TotalSeconds - elapsed));
            return (progress, remaining);
        }
    }

    public bool TryGetChannelValue(int universeId, int channelIndex, out byte value)
    {
        lock (_lock)
        {
            if (_isReleased || _currentCue is null)
            {
                value = 0;
                return false;
            }

            var key = (universeId, channelIndex);
            if (!_currentCue.Levels.TryGetValue(key, out var target))
            {
                value = 0;
                return false;
            }

            byte from = _fadeFrom.TryGetValue(key, out var f) ? f : (byte)0;
            double elapsedSeconds = (DateTime.UtcNow - _fadeStartUtc).TotalSeconds;
            var duration = target >= from ? _currentCue.FadeInTime : _currentCue.FadeOutTime;
            double t = duration.TotalSeconds <= 0
                ? 1.0
                : Math.Clamp(elapsedSeconds / duration.TotalSeconds, 0.0, 1.0);

            byte result = (byte)Math.Round(from + (target - from) * t);
            _currentOutput[key] = result;
            value = result;
            return true;
        }
    }
}

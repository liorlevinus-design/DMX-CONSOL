using System.Collections.ObjectModel;
using DmxConsole.Core.Effects;

namespace DmxConsole.Core.Engine;

/// <summary>
/// Runs a collection of <see cref="Effect"/>s and exposes their combined output as one
/// <see cref="IOutputLayer"/>, sitting above CueList (effects should override a static
/// cue look) but below the Programmer (a live fader grab always wins). Evaluates every
/// enabled effect once per tick and caches the result, rather than recomputing per channel.
/// </summary>
public sealed class EffectsEngine : IOutputLayer, ITickable
{
    private readonly object _lock = new();
    private Dictionary<(int Universe, int Channel), byte> _currentValues = new();

    public string Name { get; }
    public int Priority { get; }

    public ObservableCollection<Effect> Effects { get; } = new();

    public EffectsEngine(string name = "Effects", int priority = 300)
    {
        Name = name;
        Priority = priority;
    }

    public bool IsActive => Effects.Any(e => e.Enabled);

    public void Tick(TimeSpan elapsed)
    {
        var snapshot = new Dictionary<(int, int), byte>();
        double seconds = elapsed.TotalSeconds;

        // Later effects in the list win on any channel they share with an earlier one.
        foreach (var effect in Effects)
        {
            if (!effect.Enabled) continue;
            foreach (var (key, value) in effect.Evaluate(seconds))
                snapshot[key] = value;
        }

        lock (_lock) { _currentValues = snapshot; }
    }

    public bool TryGetChannelValue(int universeId, int channelIndex, out byte value)
    {
        lock (_lock)
        {
            return _currentValues.TryGetValue((universeId, channelIndex), out value);
        }
    }
}

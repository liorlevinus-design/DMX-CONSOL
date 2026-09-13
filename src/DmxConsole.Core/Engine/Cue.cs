namespace DmxConsole.Core.Engine;

/// <summary>
/// One recorded look: a full snapshot of DMX levels for every patched channel at the
/// moment it was recorded, plus how long it takes to fade into (and out of).
/// </summary>
public sealed class Cue
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Display/ordering number - supports fractional numbers (e.g. 1.5) like a real console.</summary>
    public double Number { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Time to fade a channel UP into its value in this cue (target &gt; where it came from).</summary>
    public TimeSpan FadeInTime { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Time to fade a channel DOWN into its value in this cue (target &lt; where it came from).</summary>
    public TimeSpan FadeOutTime { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Full snapshot: every patched (universe, channel) -> its recorded byte value.</summary>
    public IReadOnlyDictionary<(int Universe, int Channel), byte> Levels { get; init; }
        = new Dictionary<(int, int), byte>();

    public override string ToString() => $"Cue {Number} - {Name}";
}

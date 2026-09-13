using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Effects;

/// <summary>
/// A running, time-driven generator that drives one or more channels across a group
/// of fixtures - a chase, a strobe, a sine pulse, a rainbow chase. Evaluated once per
/// engine tick by <see cref="Engine.EffectsEngine"/>, never per-channel, since a whole
/// effect's output for a given instant is cheapest to compute all at once.
/// </summary>
public abstract class Effect
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>The group of fixtures this effect plays across.</summary>
    public IReadOnlyList<PatchedFixture> Fixtures { get; set; } = Array.Empty<PatchedFixture>();

    /// <summary>How many full cycles per second the effect runs at.</summary>
    public double SpeedHz { get; set; } = 1.0;

    /// <summary>
    /// Phase offset applied per fixture, as a fraction of one cycle (0..1). 0 = every
    /// fixture moves in lockstep; a value like 0.15 spreads the effect across the group
    /// so it visibly "chases" or "waves" from one fixture to the next.
    /// </summary>
    public double Spread { get; set; } = 0.0;

    public abstract string EffectType { get; }

    /// <summary>
    /// Computes this effect's contribution for the given moment in the engine's clock.
    /// Yields one (absolute channel key, value) pair per channel it touches.
    /// </summary>
    public abstract IEnumerable<((int Universe, int Channel) Key, byte Value)> Evaluate(double elapsedSeconds);

    /// <summary>Per-fixture phase (0..1), combining global cycle position with the fixture's index in the group.</summary>
    protected double PhaseFor(int fixtureIndex, double elapsedSeconds)
    {
        double raw = elapsedSeconds * SpeedHz + fixtureIndex * Spread;
        double phase = raw % 1.0;
        return phase < 0 ? phase + 1.0 : phase;
    }
}

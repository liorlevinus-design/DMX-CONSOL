using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Effects;

/// <summary>
/// Canonical multi-step effect model. It deliberately does not implement IPlaybackSource:
/// Step G keeps effects independently mergeable and does not assign them to Executors yet.
/// </summary>
public sealed class EffectPhaser : IOutputLayer, IBaseAwareLayer, IMergeAwareLayer, ITickable
{
    private readonly object _revisionLock = new();
    private readonly Dictionary<(int Universe, int Channel), long> _channelRevisions = new();
    private TimeSpan _elapsed;

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 300;
    public IReadOnlyList<PatchedFixture> Fixtures { get; set; } = Array.Empty<PatchedFixture>();
    public IReadOnlyList<EffectStep> Steps { get; set; } = Array.Empty<EffectStep>();
    public double SpeedHz { get; set; } = 1;
    public ISpeedSource? SpeedMaster { get; set; }
    public double Spread { get; set; }
    public int? Parts { get; set; }
    public int? Segments { get; set; }
    public EffectDirection Direction { get; set; } = EffectDirection.Forward;
    public bool Invert { get; set; }
    public AddMode AddMode { get; set; } = AddMode.Normal;

    public bool IsActive => Enabled && Fixtures.Count > 0 && Steps.Count > 0;

    /// <summary>The last engine time supplied to this phaser. Advancing time never changes ownership.</summary>
    public TimeSpan Elapsed => _elapsed;

    public void Tick(TimeSpan elapsed) => _elapsed = elapsed;

    public bool TryGetChannelValue(int universeId, int channelIndex, out byte value)
    {
        return TryGetChannelValue(universeId, channelIndex, 0, out value);
    }

    public bool TryGetChannelValue(
        int universeId, int channelIndex, byte baseValue, out byte value)
    {
        value = 0;
        if (!IsActive || !TryFindTarget(universeId, channelIndex, out var fixtureIndex, out var channelType))
            return false;

        var positiveWidthSteps = Steps.Where(step => step.Width > 0).ToArray();
        if (positiveWidthSteps.Length == 0) return false;

        double speedMultiplier = SpeedMaster?.GetSpeedMultiplier() ?? 1;
        double phaseOffset = EffectPhaseCalculator.OffsetFor(
            Fixtures, fixtureIndex, Spread, Parts, Segments, Direction, Id);
        double phase = Normalize(_elapsed.TotalSeconds * SpeedHz * speedMultiplier + phaseOffset);
        var (current, next, progress) = LocateStep(positiveWidthSteps, phase);

        double transition = Math.Clamp(current.Transition / 100.0, 0, 1);
        double mix = transition <= 0 ? 1 : Math.Clamp(progress / transition, 0, 1);
        mix = ApplySupportedCurve(mix, current.Accel, next.Decel);

        double? absolute = InterpolateOptional(
            current.AbsoluteValues.GetValueOrDefault(channelType),
            current.AbsoluteValues.ContainsKey(channelType),
            next.AbsoluteValues.GetValueOrDefault(channelType),
            next.AbsoluteValues.ContainsKey(channelType),
            mix);
        double relative = Interpolate(
            current.RelativeValues.GetValueOrDefault(channelType),
            next.RelativeValues.GetValueOrDefault(channelType),
            mix);

        if (absolute is null &&
            !current.RelativeValues.ContainsKey(channelType) &&
            !next.RelativeValues.ContainsKey(channelType))
            return false;

        value = EffectValueComposer.Compose(baseValue, absolute, relative, AddMode);
        return true;
    }

    public bool TryGetRevision(int universeId, int channelIndex, out long revision)
    {
        lock (_revisionLock)
        {
            return _channelRevisions.TryGetValue((universeId, channelIndex), out revision);
        }
    }

    /// <summary>
    /// Marks a parameter-specific semantic edit. Only actual fixture/channel addresses named by
    /// the edit receive a new revision; the same ChannelType on other fixtures remains untouched.
    /// </summary>
    public void MarkChannelsChanged(
        IEnumerable<(PatchedFixture Fixture, ChannelType ChannelType)> changedChannels)
    {
        long revision = RevisionClock.Next();
        lock (_revisionLock)
        {
            foreach (var (fixture, channelType) in changedChannels)
            {
                var channel = fixture.FindChannel(channelType);
                if (channel is null) continue;
                _channelRevisions[(fixture.UniverseId, fixture.AbsoluteIndex(channel))] = revision;
            }
        }
    }

    /// <summary>Marks every actual address controlled by the current fixture/step topology.</summary>
    public void MarkAllControlledChannelsChanged()
    {
        var channelTypes = Steps
            .SelectMany(step => step.AbsoluteValues.Keys.Concat(step.RelativeValues.Keys))
            .Distinct()
            .ToArray();

        MarkChannelsChanged(
            Fixtures.SelectMany(fixture => channelTypes.Select(type => (fixture, type))));
    }

    private bool TryFindTarget(
        int universeId, int channelIndex, out int fixtureIndex, out ChannelType channelType)
    {
        var declaredTypes = Steps
            .SelectMany(step => step.AbsoluteValues.Keys.Concat(step.RelativeValues.Keys))
            .Distinct()
            .ToHashSet();

        for (int i = 0; i < Fixtures.Count; i++)
        {
            var fixture = Fixtures[i];
            if (fixture.UniverseId != universeId) continue;
            foreach (var type in declaredTypes)
            {
                var channel = fixture.FindChannel(type);
                if (channel is not null && fixture.AbsoluteIndex(channel) == channelIndex)
                {
                    fixtureIndex = i;
                    channelType = type;
                    return true;
                }
            }
        }

        fixtureIndex = -1;
        channelType = default;
        return false;
    }

    private static (EffectStep Current, EffectStep Next, double Progress) LocateStep(
        IReadOnlyList<EffectStep> steps, double phase)
    {
        double totalWidth = steps.Sum(step => step.Width);
        double position = phase * totalWidth;
        double cursor = 0;

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            double end = cursor + step.Width;
            if (position < end || i == steps.Count - 1)
            {
                double progress = step.Width <= 0 ? 1 : (position - cursor) / step.Width;
                return (step, steps[(i + 1) % steps.Count], Math.Clamp(progress, 0, 1));
            }
            cursor = end;
        }

        return (steps[^1], steps[0], 1);
    }

    /// <summary>
    /// Implements the two curve forms grounded by the current Step G primitives: linear
    /// (0/0) and smooth cosine (-100/-100). Other stored values remain forward-compatible and
    /// interpolate continuously between those two forms instead of pretending to clone MA's
    /// proprietary spline-handle implementation.
    /// </summary>
    private static double ApplySupportedCurve(double progress, double accel, double decel)
    {
        double smoothAmount = Math.Clamp((-accel + -decel) / 200.0, 0, 1);
        double smooth = (1 - Math.Cos(Math.PI * progress)) / 2;
        return Interpolate(progress, smooth, smoothAmount);
    }

    private static double? InterpolateOptional(
        double from, bool hasFrom, double to, bool hasTo, double amount)
    {
        if (!hasFrom && !hasTo) return null;
        if (!hasFrom) from = to;
        if (!hasTo) to = from;
        return Interpolate(from, to, amount);
    }

    private static double Interpolate(double from, double to, double amount) =>
        from + (to - from) * amount;

    private static double Normalize(double value)
    {
        double normalized = value % 1;
        return normalized < 0 ? normalized + 1 : normalized;
    }
}

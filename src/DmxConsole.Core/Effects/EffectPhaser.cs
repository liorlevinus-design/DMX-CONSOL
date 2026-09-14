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

    // Evaluation is introduced in the next isolated Step G commit. Keeping the unwired skeleton
    // non-contributing is safer than silently shipping partial Width/Transition curve semantics.
    public bool TryGetChannelValue(int universeId, int channelIndex, out byte value)
    {
        value = 0;
        return false;
    }

    public bool TryGetChannelValue(
        int universeId, int channelIndex, byte baseValue, out byte value) =>
        TryGetChannelValue(universeId, channelIndex, out value);

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
}

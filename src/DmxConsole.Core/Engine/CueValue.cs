namespace DmxConsole.Core.Engine;

public enum CueValueKind
{
    Absolute,
    PresetRef,
}

/// <summary>Resolves a live value for a channel identified by (PresetId, ChannelType) - implemented by
/// PresetLibrary. Kept narrow so DmxConsole.Core.Engine does not need to know anything about
/// DmxConsole.Core.Presets beyond this contract.</summary>
public interface IPresetResolver
{
    bool TryResolve(Guid presetId, ChannelType channelType, out byte value);
}

/// <summary>What a Cue stores for a single (universe, channel) address: either a hard-coded byte, or a
/// live reference to a Preset. The ChannelType is always recorded - even for Absolute entries - because
/// it is needed to resolve a PresetRef, without CueList ever needing a Patch reference of its own.
///
/// TimeInOverride/TimeOutOverride (Parameter TIME slice, CLAUDE.md §16/ROADMAP §9a): an optional
/// per-(fixture, logical-parameter) fade timing override captured from the Programmer's parallel
/// timing store at STORE CUE time. Deliberately carried on THIS per-channel entry - not a second,
/// AttributeClass-keyed structure on Cue itself - so timing lives at exactly the same granularity
/// (Fixture x logical ChannelType) as the value it rides alongside. Null means "no override for
/// this side - fall back to the Cue's own flat CueTiming.TimeIn/TimeOut" (CueList's playback and
/// completion-detection both apply this same fallback). This is NOT a reintroduction of the
/// per-AttributeClass/family Cue timing removed in H1.6 Slice 2 (see CLAUDE.md's "Known
/// contradiction" callout) - that was family-keyed and lived on Cue; this is channel-keyed and
/// lives here, exactly the "only finer granularity planned" ROADMAP §9a already called out.</summary>
public sealed class CueValue
{
    public CueValueKind Kind { get; }
    public ChannelType ChannelType { get; }
    public byte AbsoluteValue { get; }
    public Guid PresetId { get; }
    public TimeSpan? TimeInOverride { get; }
    public TimeSpan? TimeOutOverride { get; }

    private CueValue(CueValueKind kind, ChannelType channelType, byte absoluteValue, Guid presetId,
        TimeSpan? timeInOverride, TimeSpan? timeOutOverride)
    {
        Kind = kind;
        ChannelType = channelType;
        AbsoluteValue = absoluteValue;
        PresetId = presetId;
        TimeInOverride = timeInOverride;
        TimeOutOverride = timeOutOverride;
    }

    public static CueValue Absolute(ChannelType channelType, byte value, TimeSpan? timeInOverride = null, TimeSpan? timeOutOverride = null) =>
        new(CueValueKind.Absolute, channelType, value, default, timeInOverride, timeOutOverride);

    public static CueValue FromPreset(ChannelType channelType, Guid presetId, TimeSpan? timeInOverride = null, TimeSpan? timeOutOverride = null) =>
        new(CueValueKind.PresetRef, channelType, default, presetId, timeInOverride, timeOutOverride);

    /// <summary>Resolved fresh on every call - never cached - so a Preset update or deletion is reflected
    /// immediately on the next read. Returns false (never throws) when a PresetRef no longer resolves
    /// (Preset deleted, or it doesn't contain this ChannelType): the channel simply contributes nothing
    /// this tick, exactly like ApplyPresetCommand silently skipping a non-matching channel in Step D.</summary>
    public bool TryResolve(IPresetResolver? resolver, out byte value)
    {
        if (Kind == CueValueKind.Absolute)
        {
            value = AbsoluteValue;
            return true;
        }

        if (resolver is not null && resolver.TryResolve(PresetId, ChannelType, out var resolved))
        {
            value = resolved;
            return true;
        }

        value = 0;
        return false;
    }
}

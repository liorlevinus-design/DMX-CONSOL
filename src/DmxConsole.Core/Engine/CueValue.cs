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
/// it is needed both to resolve a PresetRef and to look up per-AttributeClass timing overrides, without
/// CueList ever needing a Patch reference of its own.</summary>
public sealed class CueValue
{
    public CueValueKind Kind { get; }
    public ChannelType ChannelType { get; }
    public byte AbsoluteValue { get; }
    public Guid PresetId { get; }

    private CueValue(CueValueKind kind, ChannelType channelType, byte absoluteValue, Guid presetId)
    {
        Kind = kind;
        ChannelType = channelType;
        AbsoluteValue = absoluteValue;
        PresetId = presetId;
    }

    public static CueValue Absolute(ChannelType channelType, byte value) =>
        new(CueValueKind.Absolute, channelType, value, default);

    public static CueValue FromPreset(ChannelType channelType, Guid presetId) =>
        new(CueValueKind.PresetRef, channelType, default, presetId);

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

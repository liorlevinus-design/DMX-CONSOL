using DmxConsole.Core.Engine;

namespace DmxConsole.Application.Live;

/// <summary>
/// The unified per-channel LIVE read model (docs/OPERATOR_UX_ROADMAP.md §1) - the three
/// concepts LIVE must expose for any fixture/channel, computed once and shared by every
/// frontend (Channels grid, Fixtures grid, the Programmer/Editor inspector) instead of each
/// view re-deriving its own notion of "active"/"pending". Pure read data - never mutates
/// anything, safe to compute on every render.
/// </summary>
public readonly record struct LiveChannelState(
    /// <summary>Effective Live Value - what's actually winning at the output right now.</summary>
    byte EffectiveValue,
    /// <summary>Whichever layer currently owns the effective value - null if nothing is
    /// contributing to this channel yet.</summary>
    OutputOwner? Owner,
    /// <summary>Editor / Pending Value - the Programmer's own stored value for this channel, if
    /// any, independent of whether it's currently winning the merge.</summary>
    byte? EditorValue,
    /// <summary>True when the Editor/Pending value (if any) is ALSO the value currently winning
    /// the merge - i.e. the Programmer itself owns this channel right now.</summary>
    bool EditorValueIsLive,
    bool IsSelected,
    bool IsUsedInShow)
{
    public bool HasEditorValue => EditorValue is not null;

    /// <summary>Computes the full state for one raw address in one shot - the single place every
    /// LIVE-style consumer should call, so "what counts as pending/live/used" can never drift
    /// between Channels, Fixtures, and the Editor inspector.</summary>
    public static LiveChannelState For(ConsoleContext context, int universeId, int channelIndex,
        bool isSelected, IReadOnlySet<(int Universe, int Channel)> usedInShowAddresses)
    {
        byte effective = context.EffectiveOutput.GetEffectiveValue(universeId, channelIndex);
        var owner = context.EffectiveOutput.GetOwner(universeId, channelIndex);
        bool hasStored = context.Programmer.HasStoredValue(universeId, channelIndex, out var stored);
        bool editorIsLive = hasStored && owner?.Kind == OwnerKind.Programmer;
        bool usedInShow = usedInShowAddresses.Contains((universeId, channelIndex));

        return new LiveChannelState(effective, owner, hasStored ? stored : null, editorIsLive, isSelected, usedInShow);
    }

    public bool Matches(LiveFilter filter) => filter switch
    {
        LiveFilter.All => true,
        LiveFilter.EditorOnly => HasEditorValue,
        LiveFilter.Patched => true, // every LiveChannelState is built for a patched address today
        LiveFilter.UsedInShow => IsUsedInShow,
        LiveFilter.LiveOnStage => Owner is not null,
        LiveFilter.Selected => IsSelected,
        _ => true,
    };
}

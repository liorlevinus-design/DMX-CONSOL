using DmxConsole.Core;
using DmxConsole.Core.Engine;

namespace DmxConsole.Application.Live;

/// <summary>
/// One semantic parameter family (docs/OPERATOR_UX_ROADMAP.md §4: Intensity/Position/Color/
/// Beam/Image/Shape) for one fixture - a small bundle of the fixture's own <see
/// cref="LiveChannelState"/> rows for every channel that maps into this family. Groups by
/// AttributeClass (Core, the one authoritative six-family model shared by Release/Presets/the
/// Encoder Drawer/Fixtures LIVE - docs/COMMAND_SURFACE_KEY_SPEC.md §23.1) - NOT a new
/// classification invented for this view. Pure aggregation of already-computed per-channel
/// state; never re-derives "is this live/pending/used" itself.
/// </summary>
public sealed class FixtureFamilyState
{
    public AttributeClass Category { get; }

    /// <summary>Every channel of the fixture that maps to this family, each with its own already-
    /// computed LiveChannelState - e.g. Position typically holds Pan and Tilt (and PanFine/TiltFine
    /// if the profile has them) as separate entries, never blended into one.</summary>
    public IReadOnlyList<(ChannelType Type, LiveChannelState State)> Channels { get; }

    public FixtureFamilyState(AttributeClass category, IReadOnlyList<(ChannelType Type, LiveChannelState State)> channels)
    {
        Category = category;
        Channels = channels;
    }

    public bool HasEditorValue => Channels.Any(c => c.State.HasEditorValue);
    public bool IsLiveOnStage => Channels.Any(c => c.State.Owner is not null);
    public bool IsUsedInShow => Channels.Any(c => c.State.IsUsedInShow);

    /// <summary>True the moment two channels in this family are owned by genuinely different
    /// sources - e.g. Pan currently driven by a running Cue while Tilt was just grabbed in the
    /// Programmer. When true, the family has no single honest "Source" to report - the UI must
    /// show MIXED explicitly rather than picking one arbitrarily or blending them.</summary>
    public bool IsMixedProvenance
    {
        get
        {
            OutputOwner? common = null;
            bool any = false;
            foreach (var (_, state) in Channels)
            {
                if (state.Owner is null) continue;
                if (!any) { common = state.Owner; any = true; }
                else if (common?.Id != state.Owner.Id) return true;
            }
            return false;
        }
    }

    /// <summary>The single shared owner across every channel in this family that has one - null
    /// both when nothing is live yet (check IsLiveOnStage to tell the two apart) AND when
    /// provenance is mixed (check IsMixedProvenance).</summary>
    public OutputOwner? CommonOwner => IsMixedProvenance ? null : Channels.Select(c => c.State.Owner).FirstOrDefault(o => o is not null);

    public bool Matches(LiveFilter filter) => filter switch
    {
        LiveFilter.All => true,
        LiveFilter.EditorOnly => HasEditorValue,
        LiveFilter.Patched => true,
        LiveFilter.UsedInShow => IsUsedInShow,
        LiveFilter.LiveOnStage => IsLiveOnStage,
        LiveFilter.Selected => Channels.Count > 0 && Channels[0].State.IsSelected,
        _ => true,
    };

    public LiveChannelState? Find(ChannelType type)
    {
        foreach (var (t, state) in Channels)
            if (t == type) return state;
        return null;
    }
}

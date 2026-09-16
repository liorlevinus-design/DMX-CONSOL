using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Live;

/// <summary>
/// The unified per-fixture LIVE read model (docs/OPERATOR_UX_ROADMAP.md §4) - one
/// FixtureFamilyState per semantic parameter family (Intensity/Position/Color/Beam/Image/
/// Shape) present on this fixture, built entirely from <see cref="LiveChannelState"/> so
/// Fixtures LIVE can never disagree with Channels LIVE about what's pending/live/used. This is
/// a summary/selection surface only (docs/OPERATOR_UX_ROADMAP.md §4/§12: the Encoder Drawer
/// remains the primary editor) - nothing here mutates anything.
/// </summary>
public sealed class FixtureLiveState
{
    public PatchedFixture Fixture { get; }
    public bool IsSelected { get; }
    public IReadOnlyDictionary<EncoderCategory, FixtureFamilyState> Families { get; }

    private FixtureLiveState(PatchedFixture fixture, bool isSelected, IReadOnlyDictionary<EncoderCategory, FixtureFamilyState> families)
    {
        Fixture = fixture;
        IsSelected = isSelected;
        Families = families;
    }

    public FixtureFamilyState? Family(EncoderCategory category) => Families.GetValueOrDefault(category);

    public bool HasEditorValue => Families.Values.Any(f => f.HasEditorValue);
    public bool IsLiveOnStage => Families.Values.Any(f => f.IsLiveOnStage);
    public bool IsUsedInShow => Families.Values.Any(f => f.IsUsedInShow);

    public bool Matches(LiveFilter filter) => filter switch
    {
        LiveFilter.All => true,
        LiveFilter.EditorOnly => HasEditorValue,
        LiveFilter.Patched => true, // every FixtureLiveState is built for a patched fixture today
        LiveFilter.UsedInShow => IsUsedInShow,
        LiveFilter.LiveOnStage => IsLiveOnStage,
        LiveFilter.Selected => IsSelected,
        _ => true,
    };

    /// <summary>Builds the full per-family breakdown for one fixture - groups every channel of
    /// its Mode by ChannelTypeExtensions.ToEncoderCategory() (Core, the Encoder Drawer's own
    /// existing Vector-bank grouping - not a new classification invented for this view). A
    /// channel with no documented Encoder Category (Macro/ControlFunction/Generic) is simply not
    /// represented in any family, same as the Encoder Drawer's own handling.</summary>
    public static FixtureLiveState For(ConsoleContext context, PatchedFixture fixture, bool isSelected,
        IReadOnlySet<(int Universe, int Channel)> usedInShowAddresses)
    {
        var byCategory = new Dictionary<EncoderCategory, List<(ChannelType, LiveChannelState)>>();

        foreach (var channel in fixture.Mode.Channels)
        {
            var category = channel.Type.ToEncoderCategory();
            if (category is null) continue;

            int channelIndex = fixture.AbsoluteIndex(channel);
            var state = LiveChannelState.For(context, fixture.UniverseId, channelIndex, isSelected, usedInShowAddresses);

            if (!byCategory.TryGetValue(category.Value, out var list))
                byCategory[category.Value] = list = new List<(ChannelType, LiveChannelState)>();
            list.Add((channel.Type, state));
        }

        var families = byCategory.ToDictionary(kv => kv.Key, kv => new FixtureFamilyState(kv.Key, kv.Value));
        return new FixtureLiveState(fixture, isSelected, families);
    }
}

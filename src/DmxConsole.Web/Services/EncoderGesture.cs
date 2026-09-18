using DmxConsole.Application;
using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Web.Services;

/// <summary>
/// A single in-progress encoder interaction (knob drag, wheel step, value-strip drag, Position
/// pad, Color Picker) spanning possibly many fixtures and channels. Captures a full per-(fixture,
/// channel) Programmer snapshot once at <see cref="Begin"/> - not one shared before/after byte -
/// so a Mixed selection (different prior values, no stored value at all, a knocked-out value)
/// restores every target to its own exact prior state, never to a value borrowed from another
/// fixture. Mirrors the snapshot/restore shape <c>ProgrammerChannelCommandBase</c> already uses
/// for a single Command's Undo, extended to a whole gesture's lifetime and to multiple channels
/// at once (Position = Pan+Tilt, Color = R+G+B).
/// </summary>
public sealed class EncoderGesture
{
    private readonly record struct ChannelSnapshot(
        Guid FixtureId, ChannelType ChannelType, int Universe, int Channel,
        bool HadValue, byte Value, bool WasKnockedOut);

    private readonly IReadOnlyList<ChannelSnapshot> _snapshot;
    private readonly IReadOnlyList<Guid> _selectionAtStartIds;

    private EncoderGesture(IReadOnlyList<ChannelSnapshot> snapshot, IReadOnlyList<Guid> selectionAtStartIds)
    {
        _snapshot = snapshot;
        _selectionAtStartIds = selectionAtStartIds;
    }

    /// <summary>Captures one snapshot entry per (selected fixture, requested channel type) pair
    /// that the fixture actually supports - fixtures that don't have a given channel simply
    /// contribute no entry for it, same as every other "not all selected fixtures support this"
    /// case in this codebase.</summary>
    public static EncoderGesture Begin(ConsoleContext context, IEnumerable<ChannelType> types)
    {
        var typeList = types.ToList();
        var snapshots = new List<ChannelSnapshot>();

        foreach (var fixture in context.Selection.Items)
        {
            foreach (var type in typeList)
            {
                var channel = fixture.FindChannel(type);
                if (channel is null) continue;

                int idx = fixture.AbsoluteIndex(channel);
                bool hadValue = context.Programmer.HasStoredValue(fixture.UniverseId, idx, out var value);
                bool knockedOut = context.Programmer.IsKnockedOut(fixture.UniverseId, idx);

                snapshots.Add(new ChannelSnapshot(fixture.Id, type, fixture.UniverseId, idx,
                    hadValue, hadValue ? value : (byte)0, knockedOut));
            }
        }

        var selectionIds = context.Selection.Items.Select(f => f.Id).ToList();
        return new EncoderGesture(snapshots, selectionIds);
    }

    /// <summary>Order-sensitive: a reorder of the same fixtures (relevant to Next/Previous and
    /// any future order-dependent behavior) counts as a change too, not just an add/remove.</summary>
    public bool SelectionChanged(ConsoleContext context) =>
        !_selectionAtStartIds.SequenceEqual(context.Selection.Items.Select(f => f.Id));

    /// <summary>Re-resolves a snapshot entry against the CURRENT Patch - null if the fixture was
    /// removed, no longer has this channel, or the channel's own address moved (repatch). Never
    /// falls back to the snapshot's own stored address - a stale address is exactly what must
    /// never be written to.</summary>
    private static (PatchedFixture Fixture, FixtureChannel Channel)? Resolve(ConsoleContext context, ChannelSnapshot snap)
    {
        PatchedFixture? fixture = null;
        foreach (var candidate in context.Patch.Fixtures)
        {
            if (candidate.Id != snap.FixtureId) continue;
            fixture = candidate;
            break;
        }
        if (fixture is null) return null;

        var channel = fixture.FindChannel(snap.ChannelType);
        if (channel is null) return null;

        if (fixture.UniverseId != snap.Universe || fixture.AbsoluteIndex(channel) != snap.Channel) return null;

        return (fixture, channel);
    }

    /// <summary>Live preview - writes straight to the Programmer, bypassing Command/Undo
    /// entirely. Silently skips any target that no longer resolves (see <see cref="Resolve"/>).</summary>
    public void Preview(ConsoleContext context, ChannelType type, byte value)
    {
        foreach (var snap in _snapshot)
        {
            if (snap.ChannelType != type) continue;
            var resolved = Resolve(context, snap);
            if (resolved is not { } target) continue;

            context.Programmer.SetChannel(target.Fixture.UniverseId, target.Fixture.AbsoluteIndex(target.Channel), value);
        }
    }

    /// <summary>Restores every snapshotted channel to EXACTLY what it was before the gesture
    /// began - HadValue=false clears the channel entirely rather than writing a value that was
    /// never really there. Used both for Cancel (no Command at all) and as the first step of
    /// Commit (so the real Command's own before/after snapshot captures the true pre-gesture
    /// state, not the gesture's last live-preview value).</summary>
    public void RestoreSnapshot(ConsoleContext context)
    {
        foreach (var snap in _snapshot)
        {
            var resolved = Resolve(context, snap);
            if (resolved is not { } target) continue;

            int idx = target.Fixture.AbsoluteIndex(target.Channel);
            if (snap.HadValue) context.Programmer.SetChannel(target.Fixture.UniverseId, idx, snap.Value);
            else context.Programmer.ClearChannel(target.Fixture.UniverseId, idx);

            if (snap.WasKnockedOut) context.Programmer.Knockout(target.Fixture.UniverseId, idx);
            else context.Programmer.Restore(target.Fixture.UniverseId, idx);
        }
    }

    /// <summary>Currently-valid fixtures that had this channel type at gesture start - re-resolved
    /// against the live Patch, so a fixture removed mid-gesture is correctly excluded.</summary>
    public IReadOnlyList<PatchedFixture> TargetFixtures(ConsoleContext context, ChannelType type) =>
        _snapshot.Where(s => s.ChannelType == type)
            .Select(s => Resolve(context, s)?.Fixture)
            .Where(f => f is not null)
            .Select(f => f!)
            .Distinct()
            .ToList();
}

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// Base for DMX DIRECT ADDRESSING's two commands (Set/Release) - the same "snapshot every
/// touched address before, restore verbatim on Undo" technique <see cref="ProgrammerChannelCommandBase"/>
/// already uses, adapted to raw (Universe, Address) pairs instead of FixtureChannel.
///
/// This is deliberately NOT a subclass of ProgrammerChannelCommandBase: that type's iteration
/// unit is FixtureChannel (SelectChannels/ApplyToChannel), which requires a PatchedFixture - an
/// unpatched DMX address has no FixtureChannel at all, by definition. Rather than forcing a fake
/// FixtureChannel into existence (or teaching the base class about "channels that aren't really
/// channels"), this is a small, separate base sharing the same proven snapshot/undo shape against
/// the SAME Programmer - not a second merge engine, not a parallel "raw DMX override" path, just
/// the existing Programmer addressed directly (Programmer.SetChannel/ClearChannel/HasStoredValue
/// already take a raw (universeId, channelIndex) pair and know nothing about fixtures - see
/// Programmer.cs's own shape).
/// </summary>
public abstract class DmxAddressCommandBase : IConsoleCommand, IReplayableCommand
{
    private readonly record struct AddressSnapshot(int Universe, int Channel, bool HadValue, byte Value, bool WasKnockedOut);

    private readonly IReadOnlyList<(int Universe, int Channel)> _addresses;
    private List<AddressSnapshot>? _previous;

    protected abstract ConsoleActionType ActionType { get; }

    /// <summary>The raw addresses this command was constructed with - exposed so a subclass's own
    /// CreateFreshInstance() can pass the same construction-time addresses to a brand-new
    /// instance, never this instance's own accumulated _previous snapshot.</summary>
    protected IReadOnlyList<(int Universe, int Channel)> Addresses => _addresses;

    /// <summary>addresses are raw 0-based (universe, channelIndex) pairs - already converted from
    /// the operator-facing 1-512 DMX address by the caller (CommandComposer), never off-by-one
    /// ambiguity living in two places.</summary>
    protected DmxAddressCommandBase(IReadOnlyList<(int Universe, int Channel)> addresses) => _addresses = addresses;

    /// <summary>Builds a brand-new instance with this command's own construction-time addresses,
    /// never sharing this instance's own _previous snapshot (docs/COMMAND_SURFACE_KEY_SPEC.md
    /// MACROS "REPLAY INSTANCE SAFETY").</summary>
    public abstract IConsoleCommand CreateFreshInstance();

    /// <summary>Applies this command's effect to one address. Return true only if something
    /// actually changed - same "no-op returns false, never captured" rule as
    /// ProgrammerChannelCommandBase.ApplyToChannel.</summary>
    protected abstract bool Apply(ConsoleContext context, int universe, int channel);

    public CommandResult Execute(ConsoleContext context)
    {
        // A configured output Universe must exist even with zero patched fixtures (DMX DIRECT
        // ADDRESSING follow-up §2) - DMX direct addressing IS the explicit allocation mechanism
        // for a Universe nothing has ever patched into. Idempotent, and never touches Patch/
        // PatchedFixture - no dummy fixture, no fake patch entry, ever.
        foreach (var universe in _addresses.Select(a => a.Universe).Distinct())
            context.UniverseAllocator?.EnsureUniverse(universe);

        var snapshots = new List<AddressSnapshot>();
        var touchedAddresses = new List<(int Universe, int Channel)>();

        foreach (var (universe, channel) in _addresses)
        {
            bool hadValue = context.Programmer.HasStoredValue(universe, channel, out var beforeValue);
            bool wasKnockedOut = context.Programmer.IsKnockedOut(universe, channel);

            if (!Apply(context, universe, channel)) continue;

            snapshots.Add(new AddressSnapshot(universe, channel, hadValue, hadValue ? beforeValue : (byte)0, wasKnockedOut));
            touchedAddresses.Add((universe, channel));
        }

        _previous = snapshots;

        return new CommandResult
        {
            ActionType = ActionType,
            Message = touchedAddresses.Count == 0 ? "No DMX addresses affected." : $"{touchedAddresses.Count} DMX address(es) affected.",
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_previous is null) return;

        foreach (var snap in _previous)
        {
            if (snap.HadValue) context.Programmer.SetChannel(snap.Universe, snap.Channel, snap.Value);
            else context.Programmer.ClearChannel(snap.Universe, snap.Channel);

            if (snap.WasKnockedOut) context.Programmer.Knockout(snap.Universe, snap.Channel);
            else context.Programmer.Restore(snap.Universe, snap.Channel);
        }
    }
}

using DmxConsole.Core.Engine;

namespace DmxConsole.Application.Live;

/// <summary>
/// "Used in Show" (docs/OPERATOR_UX_ROADMAP.md §2): a channel is used in show when it's
/// referenced by programmed show data - today that means any Cue List (every CueList that
/// exists in this app is assigned to some Executor, so walking Executors already reaches every
/// CueList; there is no other place a CueList lives). Deliberately does not consult
/// EffectiveOutput/Programmer - "used" is a structural fact about what's *programmed*, entirely
/// independent of what's currently winning the merge (that's LiveFilter.LiveOnStage's job).
/// Future playback/source types become part of this union the moment they're first-class show-
/// programming objects, per the roadmap's own note - no redesign needed, just another branch
/// in the loop below.
/// </summary>
public static class ShowUsageQuery
{
    public static IReadOnlySet<(int Universe, int Channel)> ReferencedAddresses(ConsoleContext context)
    {
        var addresses = new HashSet<(int, int)>();
        foreach (var executor in context.Executors.Executors)
            if (executor.Source is CueList cueList)
                foreach (var address in cueList.ReferencedAddresses())
                    addresses.Add(address);
        return addresses;
    }
}

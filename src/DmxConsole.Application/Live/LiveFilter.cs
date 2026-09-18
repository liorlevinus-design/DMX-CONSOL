namespace DmxConsole.Application.Live;

/// <summary>
/// The operator-facing LIVE filter vocabulary - docs/OPERATOR_UX_ROADMAP.md §2. These are
/// console concepts, not cosmetic table filters: every frontend that shows a LIVE-style
/// channel/fixture list (Channels, Fixtures, the Programmer/Editor inspector, a future
/// touch/NL surface) must offer exactly this vocabulary and answer it identically, via
/// <see cref="LiveRowClassifier"/> - never a per-view reinvention of "what counts as active".
/// </summary>
public enum LiveFilter
{
    All,

    /// <summary>Only rows that currently hold a temporary Programmer/Editor value - the
    /// replacement for opening a separate Programmer view during normal operation.</summary>
    EditorOnly,

    /// <summary>Only rows with a valid patch. Every row a Channels/Fixtures view enumerates is
    /// patched by construction today (they're built by walking Patch.Fixtures) - this filter is
    /// a genuine no-op there, kept in the vocabulary honestly rather than hidden, and becomes
    /// meaningful the moment a future view enumerates addresses that aren't all patched.</summary>
    Patched,

    /// <summary>Referenced by programmed show data - see <see cref="ShowUsageQuery"/> - regardless
    /// of whether it's currently outputting a non-zero level.</summary>
    UsedInShow,

    /// <summary>Currently affecting the effective merged output - based on the real merge
    /// result (DmxOutputEngine's owning layer), never just "has a Programmer value".</summary>
    LiveOnStage,

    /// <summary>Only the current operator selection - the same shared FixtureSelection every
    /// other control surface reads and writes.</summary>
    Selected,
}

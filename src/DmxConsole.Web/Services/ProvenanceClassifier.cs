using DmxConsole.Core.Engine;

namespace DmxConsole.Web.Services;

/// <summary>
/// The console's semantic source/provenance categories - the single place that decides "what
/// counts as Main Cue List vs a generic Executor vs Programmer vs Effect", shared by every View
/// that shows ownership (Channels/Fixtures/Programmer) so they stay visually consistent rather
/// than each inventing its own coloring. Deliberately does NOT include a "Multiple/Contested"
/// category: DmxOutputEngine's tier-fold merge always resolves to exactly one winning layer per
/// channel per tick (that's the whole point of Step F's Priority/HTP/LTP tie-break), so there is
/// no "contested" state this architecture can actually produce today - adding one here would be
/// fabricating a distinction the data doesn't have.
/// </summary>
public enum ProvenanceCategory
{
    None,
    Programmer,

    /// <summary>Executor 1 - the app's single, always-present Main Cue List, by the wiring
    /// convention MainViewModel establishes (Executor.Add(1) assigned the CueList). A future
    /// multi-cue-list console would need a real "which one is the main one" concept; today
    /// there is exactly one, so ExecutorNumber == 1 is an exact, not approximate, test.</summary>
    MainCueList,

    /// <summary>Any other Executor (2+).</summary>
    Executor,

    Effect,
}

public static class ProvenanceClassifier
{
    public static ProvenanceCategory Classify(OutputOwner? owner) => owner switch
    {
        null => ProvenanceCategory.None,
        { Kind: OwnerKind.Programmer } => ProvenanceCategory.Programmer,
        { Kind: OwnerKind.Effect } => ProvenanceCategory.Effect,
        { Kind: OwnerKind.Executor, ExecutorNumber: 1 } => ProvenanceCategory.MainCueList,
        { Kind: OwnerKind.Executor } => ProvenanceCategory.Executor,
        _ => ProvenanceCategory.Executor,
    };

    public static string Label(ProvenanceCategory category) => category switch
    {
        ProvenanceCategory.Programmer => "Programmer",
        ProvenanceCategory.MainCueList => "Main Cue List",
        ProvenanceCategory.Executor => "Executor",
        ProvenanceCategory.Effect => "Effect",
        _ => "—",
    };

    /// <summary>CSS class for semantic (not decorative) color-coding - same categories, same
    /// colors, in every View that shows provenance.</summary>
    public static string CssClass(ProvenanceCategory category) => category switch
    {
        ProvenanceCategory.Programmer => "src-programmer",
        ProvenanceCategory.MainCueList => "src-maincue",
        ProvenanceCategory.Executor => "src-executor",
        ProvenanceCategory.Effect => "src-effect",
        _ => "src-none",
    };

    /// <summary>Full "Exec N / Cue Name" style label for an Executor-backed owner (Main Cue List
    /// or a generic Executor alike) - looks up the actual Executor from the bank to read its
    /// current cue, if its Source happens to be a CueList.</summary>
    public static string DescribeExecutorOwner(OutputOwner owner, ExecutorBank executors)
    {
        var executor = executors.Executors.FirstOrDefault(e => e.Id == owner.ExecutorId);
        string label = $"Exec {owner.ExecutorNumber}";
        if (executor?.GetStatus() is CueListPlaybackStatus { CurrentCue: { } cue })
        {
            string cueLabel = string.IsNullOrWhiteSpace(cue.Name) ? $"Cue {cue.Number}" : cue.Name;
            label += $" / {cueLabel}";
        }
        return label;
    }
}

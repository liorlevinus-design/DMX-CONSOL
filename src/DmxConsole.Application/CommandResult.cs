using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;

namespace DmxConsole.Application;

/// <summary>What kind of console action produced a <see cref="CommandResult"/>.</summary>
public enum ConsoleActionType
{
    ToggleFixture,
    SelectRange,
    SelectOdd,
    SelectEven,
    Next,
    Previous,
    ClearSelection,
    AddGroupToSelection,
    CreateGroup,
    RemoveGroup,

    /// <summary>Several commands dispatched and undone together as one transaction.</summary>
    Batch,
}

/// <summary>
/// Structured outcome of an <see cref="IConsoleCommand"/>. This is the source of truth for
/// any caller - including a future Natural Language layer, which should read the typed
/// fields below rather than parse <see cref="Message"/>. Message exists purely as a
/// ready-made human-readable summary for UI status bars and logs/debugging.
/// </summary>
public class CommandResult
{
    public required ConsoleActionType ActionType { get; init; }
    public bool Success { get; init; } = true;
    public string? Error { get; init; }
    public string? Warning { get; init; }

    /// <summary>Fixtures the action left affected - for selection commands, the resulting selection.</summary>
    public IReadOnlyList<PatchedFixture> AffectedFixtures { get; init; } = Array.Empty<PatchedFixture>();

    /// <summary>Populated only by Create/RemoveGroup.</summary>
    public FixtureGroup? Group { get; init; }

    /// <summary>Convenience for UI/logs/debugging only - never the source of truth for a natural-language layer.</summary>
    public string? Message { get; init; }

    public static CommandResult Failed(ConsoleActionType actionType, string error) =>
        new() { ActionType = actionType, Success = false, Error = error };
}

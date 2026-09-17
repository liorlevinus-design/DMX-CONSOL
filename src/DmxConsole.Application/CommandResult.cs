using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Effects;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;

namespace DmxConsole.Application;

/// <summary>What kind of console action produced a <see cref="CommandResult"/>.</summary>
public enum ConsoleActionType
{
    ToggleFixture,
    RemoveFixtureFromSelection,
    SelectRange,
    SelectOdd,
    SelectEven,
    Next,
    Previous,
    ClearSelection,
    AddGroupToSelection,
    CreateGroup,
    StoreGroup,
    RenameGroup,
    RemoveGroup,

    /// <summary>Several commands dispatched and undone together as one transaction.</summary>
    Batch,

    /// <summary>Released every Programmer value for the targeted fixtures (all attributes).</summary>
    Release,

    /// <summary>Released Programmer values for one attribute class only.</summary>
    ClearAttribute,

    /// <summary>Released Programmer values for one semantic parameter only (docs/COMMAND_SURFACE_KEY_SPEC.md
    /// §7 - PARAMETER RELEASE, e.g. "PAN RELEASE" leaves Tilt untouched). Distinct from
    /// ClearAttribute (a whole family) - a third, finer granularity.</summary>
    ReleaseParameter,

    Knockout,
    Restore,
    AdjustIntensity,
    SetAttributeValue,

    /// <summary>CAPTURE ALL (docs/COMMAND_SURFACE_KEY_SPEC.md §8): reads the current effective
    /// live output for every actively-owned channel across the whole patch and writes it into
    /// the Programmer as a new Editor value - an undoable editing command (IConsoleCommand), not
    /// a runtime action, since it genuinely creates new Editor state.</summary>
    CaptureAll,

    StorePreset,
    ApplyPreset,
    RemovePreset,

    // Step F - Executors: Undoable editing commands
    AssignExecutor,
    SetExecutorLevel,
    CreateExecutor,
    RemoveExecutor,

    // Step F - Executors: Operational/runtime actions (IConsoleAction - never enter Undo history)
    Go,
    Back,
    Stop,
    Pause,
    Resume,
    FlashPress,
    FlashRelease,

    CreateEffect,
    DeleteEffect,
    StartEffect,
    StopEffect,
    SetEffectRate,

    /// <summary>SHIFT+RELEASE (docs/COMMAND_SURFACE_KEY_SPEC.md §7/§18) - stops every currently
    /// active playback source (Executors, playback-driven Effects). Operational/runtime
    /// (IConsoleAction - never enters Undo history), never touches Editor values or Selection,
    /// never deletes show data.</summary>
    ReleaseAllPlaybacks,
}

/// <summary>
/// Structured outcome of an <see cref="IConsoleCommand"/>. This is the source of truth for
/// any caller - including a future Natural Language layer, which should read the typed
/// fields below rather than parse <see cref="Message"/>. Message exists purely as a
/// ready-made human-readable summary for UI status bars and logs/debugging.
/// </summary>
public record CommandResult
{
    public required ConsoleActionType ActionType { get; init; }
    public bool Success { get; init; } = true;
    public string? Error { get; init; }
    public string? Warning { get; init; }

    /// <summary>Fixtures the action left affected - for selection commands, the resulting selection.</summary>
    public IReadOnlyList<PatchedFixture> AffectedFixtures { get; init; } = Array.Empty<PatchedFixture>();

    /// <summary>Populated only by Create/RemoveGroup.</summary>
    public FixtureGroup? Group { get; init; }

    /// <summary>Populated only by Store/RemovePreset.</summary>
    public Preset? Preset { get; init; }

    /// <summary>Populated by Executor Commands and Actions (Step F).</summary>
    public Executor? Executor { get; init; }

    /// <summary>Populated by Effect Commands and Actions (Step G).</summary>
    public EffectPhaser? Effect { get; init; }

    /// <summary>Attribute classes this action touched - e.g. [Intensity] for AdjustIntensity, [Color] for a "clear color" ClearAttribute.</summary>
    public IReadOnlyList<AttributeClass> AffectedAttributes { get; init; } = Array.Empty<AttributeClass>();

    /// <summary>
    /// Per-channel values before/after the action, keyed by fixture identity and channel
    /// role rather than raw universe/address - so a caller (a future Natural Language layer
    /// especially) can report "Fixture 12's Dimmer went from 180 to 230" without knowing
    /// anything about DMX addressing.
    /// </summary>
    public IReadOnlyDictionary<(Guid FixtureId, ChannelType Channel), byte> PreviousValues { get; init; } =
        new Dictionary<(Guid, ChannelType), byte>();

    public IReadOnlyDictionary<(Guid FixtureId, ChannelType Channel), byte> NewValues { get; init; } =
        new Dictionary<(Guid, ChannelType), byte>();

    /// <summary>
    /// Populated only when <see cref="ActionType"/> is <see cref="ConsoleActionType.Batch"/> -
    /// the individual result of every command in the transaction, in dispatch order, so a
    /// caller (a future Natural Language layer especially) can report on each sub-action
    /// instead of only seeing the last one. Includes the failing command's result too when
    /// the batch as a whole failed and rolled back.
    /// </summary>
    public IReadOnlyList<CommandResult> ChildResults { get; init; } = Array.Empty<CommandResult>();

    /// <summary>Convenience for UI/logs/debugging only - never the source of truth for a natural-language layer.</summary>
    public string? Message { get; init; }

    public static CommandResult Failed(ConsoleActionType actionType, string error) =>
        new() { ActionType = actionType, Success = false, Error = error };
}

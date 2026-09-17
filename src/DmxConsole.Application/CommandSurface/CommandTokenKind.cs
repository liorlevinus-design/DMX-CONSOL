namespace DmxConsole.Application.CommandSurface;

/// <summary>
/// Every kind of token the command language currently understands. Not exhaustive by design -
/// see CommandToken's own doc comment for why a new kind never requires rewriting CommandComposer's
/// existing parsing for kinds it already handles; ones not yet interpreted by the composer are
/// simply ignored (see CommandComposer.Push), never a crash.
/// </summary>
public enum CommandTokenKind
{
    // Values
    Number,
    Recall,

    // Object types
    Fixture,
    Group,
    Cue,
    Preset,
    Executor,

    // Selection operators
    Thru,
    Plus,
    Minus,
    Odd,
    Even,
    Next,
    Previous,

    // Command structure
    At,
    Enter,
    Clear,
    Backspace,

    /// <summary>Semantic 100% (docs/COMMAND_SURFACE_KEY_SPEC.md §12) - never raw DMX 255 in the
    /// grammar itself (the underlying command still ends up writing byte 255, same as any other
    /// Absolute 100% AdjustIntensityCommand would).</summary>
    Full,

    /// <summary>Bare, or family-qualified (e.g. COLOR HOME) - self-terminating, see §6.</summary>
    Home,

    /// <summary>Addresses one semantic parameter (e.g. PAN, ZOOM) rather than a whole family -
    /// carries the specific ChannelType via CommandToken.SemanticPayload (see CommandToken.Parameter).
    /// Only "&lt;Parameter&gt; RELEASE" is defined in v1 (docs/COMMAND_SURFACE_KEY_SPEC.md §7) -
    /// there is no PARAMETER HOME.</summary>
    Parameter,

    // Editing verbs (not yet interpreted by CommandComposer - reserved)
    Store,
    Update,
    Delete,
    Copy,
    Move,
    GoTo,
    Release,
    Knockout,

    // Attribute classes/families - the six real, selectable families (docs/COMMAND_SURFACE_KEY_SPEC.md
    // §23.1's unified AttributeClass model). Image/Shape added alongside Full/Home since HOME/RELEASE
    // now need to address all six families, not just the original four.
    Intensity,
    Position,
    Color,
    Beam,
    Image,
    Shape,

    // Cue/timing vocabulary (not yet interpreted by CommandComposer - reserved)
    Timing,
    Trigger,
    Follow,
    Wait,
}

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

    /// <summary>Raw DMX direct addressing (docs/COMMAND_SURFACE_KEY_SPEC.md, "DMX DIRECT
    /// ADDRESSING" slice) - a fixed domain key alongside Fixture/Group/Cue, establishing
    /// object/domain context for the current command only (never sticky). Diagnostic/emergency
    /// tool: writes/releases raw (Universe, Address) pairs directly through the Programmer,
    /// independent of any Fixture Profile.</summary>
    Dmx,

    /// <summary>A single resolved "Universe.Address" pair (docs above, §8's dot-ambiguity
    /// resolution) - carries (int Universe, int Address) via CommandToken.SemanticPayload,
    /// assembled by CommandSurfaceViewModel from digit+'.'+digit entry while a DmxAddress is
    /// expected next (CommandComposition.ExpectedNext), never a raw Number token reinterpreted
    /// by string-splitting in Razor.</summary>
    DmxAddress,

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

    /// <summary>CAPTURE ALL (docs/COMMAND_SURFACE_KEY_SPEC.md §8) - always instant/self-terminating,
    /// never composed with anything else. Not "Select All" and not scoped to the current
    /// Selection - reads the whole patch's current effective live output into the Editor.</summary>
    CaptureAll,

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

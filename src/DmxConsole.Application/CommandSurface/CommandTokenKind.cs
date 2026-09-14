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

    // Editing verbs (not yet interpreted by CommandComposer - reserved)
    Store,
    Update,
    Delete,
    Copy,
    Move,
    GoTo,
    Release,
    Knockout,

    // Attribute classes (not yet interpreted by CommandComposer - reserved)
    Intensity,
    Position,
    Color,
    Beam,

    // Cue/timing vocabulary (not yet interpreted by CommandComposer - reserved)
    Timing,
    Trigger,
    Follow,
    Wait,
}

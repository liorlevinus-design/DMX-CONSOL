using DmxConsole.Application.CommandSurface;

namespace DmxConsole.Web.CommandSurface;

/// <summary>One physical-key binding: either a digit character (0-9, consumed as digit entry)
/// or a CommandTokenKind (consumed as a whole token). Exactly one is set.</summary>
public readonly record struct KeyBinding(char? Digit, CommandTokenKind? TokenKind);

/// <summary>
/// The single source of truth mapping a physical keyboard key (KeyboardEventArgs.Key) to the
/// same CommandToken a touch button on CommandSurface would emit - so a physical keyboard, and
/// eventually MIDI/HID, drive the exact same CommandComposer path as touch, never a parallel one.
///
/// The digit/Enter/Backspace/+/- bindings mirror a numeric keypad directly and are stable. The
/// letter mnemonics (F=Fixture, G=Group, T=Thru, @=At) are a reasonable provisional choice, not
/// a finalized professional-console layout - remapping any of them later is a one-line change
/// here, not a rewrite, since every consumer only ever asks this map, never hardcodes a key.
/// </summary>
public static class KeyboardCommandMap
{
    private static readonly Dictionary<string, KeyBinding> Bindings = BuildBindings();

    public static bool TryResolve(string key, out KeyBinding binding) => Bindings.TryGetValue(key, out binding);

    private static Dictionary<string, KeyBinding> BuildBindings()
    {
        var map = new Dictionary<string, KeyBinding>();

        for (char d = '0'; d <= '9'; d++) map[d.ToString()] = new KeyBinding(Digit: d, null);
        map["."] = new KeyBinding(Digit: '.', null);

        void Token(string key, CommandTokenKind kind) => map[key] = new KeyBinding(null, kind);

        Token("Enter", CommandTokenKind.Enter);
        Token("Backspace", CommandTokenKind.Backspace);
        Token("Escape", CommandTokenKind.Clear);
        Token("Delete", CommandTokenKind.Clear);
        Token("+", CommandTokenKind.Plus);
        Token("-", CommandTokenKind.Minus);
        Token("@", CommandTokenKind.At);

        Token("f", CommandTokenKind.Fixture);
        Token("F", CommandTokenKind.Fixture);
        Token("g", CommandTokenKind.Group);
        Token("G", CommandTokenKind.Group);
        Token("t", CommandTokenKind.Thru);
        Token("T", CommandTokenKind.Thru);
        Token("c", CommandTokenKind.Cue);
        Token("C", CommandTokenKind.Cue);

        // RELEASE and HOME (docs/COMMAND_SURFACE_KEY_SPEC.md §6/§7) - Shift is deliberately NOT
        // bound to a physical key here: the browser's own Shift key fires on every keystroke
        // (including ordinary capital-letter typing elsewhere on the page), so treating it as our
        // toggle-arm modifier would be surprising rather than useful. Shift stays touch-only for
        // v1, same as the six family keys/Preset/Full (no safe, non-colliding mnemonic chosen yet
        // - a provisional choice deferred rather than guessed, per this project's own rule).
        Token("r", CommandTokenKind.Release);
        Token("R", CommandTokenKind.Release);
        Token("h", CommandTokenKind.Home);
        Token("H", CommandTokenKind.Home);

        // CAPTURE ALL (docs/COMMAND_SURFACE_KEY_SPEC.md §8) - "a" for capture-All.
        Token("a", CommandTokenKind.CaptureAll);
        Token("A", CommandTokenKind.CaptureAll);

        return map;
    }
}

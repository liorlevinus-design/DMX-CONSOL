using DmxConsole.Core;

namespace DmxConsole.Application.CommandSurface;

/// <summary>
/// One unit of console command-line syntax, emitted by a CommandSurface (touch keypad, physical
/// keyboard, future MIDI/HID) and consumed by CommandComposer. A single closed enum of Kind
/// values is not enough on its own - Number needs a value, Fixture/Group/Cue/etc. eventually
/// need to carry a resolved object reference once the composer looks one up, and some future
/// token (an Effect parameter key, say) may need arbitrary structured data. Rather than a
/// class hierarchy per kind (which would force every consumer to pattern-match a growing type
/// tree) this is one flat, optional-field record: consumers switch on Kind and read whichever
/// field that Kind actually populates. Adding a new CommandTokenKind never requires changing
/// this shape.
/// </summary>
public sealed record CommandToken
{
    public required CommandTokenKind Kind { get; init; }

    /// <summary>Populated for Kind == Number.</summary>
    public double? NumericValue { get; init; }

    /// <summary>Reserved for a token that already carries a resolved object identity (e.g. a
    /// soft-key for a specific saved Group, rather than the operator typing its number) - not
    /// used by CommandComposer's current Number-based resolution, but the shape supports it
    /// without a rewrite.</summary>
    public Guid? ObjectId { get; init; }

    /// <summary>How this token renders on the CommandLine/CommandSurface. Always populated -
    /// see the factory methods below.</summary>
    public string DisplayText { get; init; } = string.Empty;

    /// <summary>Escape hatch for a future token kind whose data doesn't fit Number/ObjectId -
    /// e.g. a structured Effect parameter or a multi-field timing payload. Untyped deliberately:
    /// this is a documented extension point, not something CommandComposer's current grammar
    /// reads.</summary>
    public object? SemanticPayload { get; init; }

    public static CommandToken Number(double value) =>
        new() { Kind = CommandTokenKind.Number, NumericValue = value, DisplayText = FormatNumber(value) };

    /// <summary>PARAMETER RELEASE's addressed channel (docs/COMMAND_SURFACE_KEY_SPEC.md §7) -
    /// carries the specific DmxConsole.Core.ChannelType via SemanticPayload since ChannelType
    /// isn't itself a CommandTokenKind (there are far too many for a dedicated enum member each,
    /// unlike the six fixed family keys).</summary>
    public static CommandToken Parameter(ChannelType channelType) =>
        new() { Kind = CommandTokenKind.Parameter, SemanticPayload = channelType, DisplayText = channelType.ToString().ToUpperInvariant() };

    public static CommandToken Simple(CommandTokenKind kind, string? displayText = null) =>
        new() { Kind = kind, DisplayText = displayText ?? DefaultDisplay(kind) };

    private static string FormatNumber(double value) =>
        value == Math.Floor(value) ? ((long)value).ToString() : value.ToString("0.##");

    private static string DefaultDisplay(CommandTokenKind kind) => kind switch
    {
        CommandTokenKind.Thru => "Thru",
        CommandTokenKind.Plus => "+",
        CommandTokenKind.Minus => "-",
        CommandTokenKind.At => "At",
        CommandTokenKind.Recall => ".",
        CommandTokenKind.Enter => "Enter",
        CommandTokenKind.Odd => "Odd",
        CommandTokenKind.Even => "Even",
        CommandTokenKind.Next => "Next",
        CommandTokenKind.Previous => "Last",
        CommandTokenKind.GoTo => "GoTo",
        CommandTokenKind.Full => "Full",
        CommandTokenKind.Home => "Home",
        _ => kind.ToString(),
    };
}

using DmxConsole.Application.Commands;
using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;

namespace DmxConsole.Application.CommandSurface;

/// <summary>
/// Owns the console command line's partial-composition state - deterministic console command
/// syntax, NOT a natural-language parser (a future NL layer is a separate, independent path to
/// the same Application commands - see the type's own architecture note in the GUI milestone
/// plan). Pure and UI-independent: a CommandSurface (touch keypad, physical keyboard, future
/// MIDI/HID) only ever pushes CommandTokens here and renders whatever CommandComposition comes
/// back - it never mutates ConsoleContext directly, and this class never dispatches what it
/// builds. That boundary is what "one business-logic path for the command keypad" means in
/// practice: CommandComposer only ever *constructs* an IConsoleCommand out of tokens using the
/// exact same Commands (SelectRangeCommand, AdjustIntensityCommand, ...) any other frontend
/// already dispatches through CommandDispatcher - the caller still does the actual Dispatch.
///
/// Grammar supported by this first milestone slice (see CommandTokenKind for the reserved-but-
/// not-yet-interpreted vocabulary - adding a new token kind never requires rewriting this):
///
///   command    := objectType clause+ (AT number)?
///   objectType := FIXTURE | GROUP
///   clause     := number | (PLUS number) | (MINUS number) | (THRU number)
///
/// THRU always pairs with the number immediately before it (the running "last number"), not
/// necessarily the very first one - "1 Thru 5 + 8 Thru 10" is a valid, if advanced, composition.
/// </summary>
public sealed class CommandComposer
{
    private readonly ConsoleContext _context;
    private readonly List<CommandToken> _tokens = new();

    public CommandComposer(ConsoleContext context) => _context = context;

    /// <summary>Current composition without pushing anything - what a freshly-opened CommandLine renders.</summary>
    public CommandComposition Current => Build(finalize: false);

    public CommandComposition Push(CommandToken token)
    {
        switch (token.Kind)
        {
            case CommandTokenKind.Clear:
                if (_tokens.Count == 0) return Build(finalize: false) with { EmptyClearRequested = true };
                _tokens.Clear();
                return Build(finalize: false);

            case CommandTokenKind.Backspace:
                if (_tokens.Count > 0) _tokens.RemoveAt(_tokens.Count - 1);
                return Build(finalize: false);

            case CommandTokenKind.Enter:
                return Build(finalize: true);

            default:
                _tokens.Add(token);
                return Build(finalize: false);
        }
    }

    /// <summary>Resets composition state without treating it as an "empty Clear" signal -
    /// for a caller that just successfully dispatched a ReadyOperation and wants a fresh line.</summary>
    public void Reset() => _tokens.Clear();

    // ---- Parsing --------------------------------------------------------------------------

    private enum ObjectType { Fixture, Group }
    private enum ClauseOp { Anchor, Plus, Minus, Thru }
    private readonly record struct Clause(ClauseOp Op, double Number);

    private CommandComposition Build(bool finalize)
    {
        string preview = string.Join(" ", _tokens.Select(t => t.DisplayText));

        if (_tokens.Count == 0)
        {
            return new CommandComposition
            {
                Tokens = _tokens.ToList(),
                PreviewText = preview,
                ExpectedNext = new[] { CommandTokenKind.Fixture, CommandTokenKind.Group },
            };
        }

        var head = _tokens[0];
        if (head.Kind is not (CommandTokenKind.Fixture or CommandTokenKind.Group))
        {
            return Incomplete(preview, "Expected Fixture or Group.", CommandTokenKind.Fixture, CommandTokenKind.Group);
        }

        var objectType = head.Kind == CommandTokenKind.Fixture ? ObjectType.Fixture : ObjectType.Group;

        var clauses = new List<Clause>();
        double? atValue = null;
        double? lastNumber = null;

        // Tiny explicit state machine over the remaining tokens - see the class doc comment for the grammar.
        int i = 1;
        while (i < _tokens.Count)
        {
            var token = _tokens[i];

            if (token.Kind == CommandTokenKind.Number)
            {
                // A bare number right after the object type (or after another bare number) is
                // an Anchor/additional target; a number immediately after +/-/Thru is that
                // operator's operand - both cases already consumed the operator token below,
                // so a Number reaching here is always a fresh Anchor.
                clauses.Add(new Clause(ClauseOp.Anchor, token.NumericValue!.Value));
                lastNumber = token.NumericValue;
                i++;
                continue;
            }

            if (token.Kind is CommandTokenKind.Plus or CommandTokenKind.Minus or CommandTokenKind.Thru)
            {
                if (i + 1 >= _tokens.Count || _tokens[i + 1].Kind != CommandTokenKind.Number)
                    return Incomplete(preview, $"Expected a number after {token.DisplayText}.", CommandTokenKind.Number);

                double operand = _tokens[i + 1].NumericValue!.Value;
                var op = token.Kind switch
                {
                    CommandTokenKind.Plus => ClauseOp.Plus,
                    CommandTokenKind.Minus => ClauseOp.Minus,
                    _ => ClauseOp.Thru,
                };

                if (op == ClauseOp.Thru && lastNumber is null)
                    return Incomplete(preview, "Thru needs a preceding number.", CommandTokenKind.Number);

                clauses.Add(new Clause(op, operand));
                lastNumber = operand;
                i += 2;
                continue;
            }

            if (token.Kind == CommandTokenKind.At)
            {
                if (i + 1 >= _tokens.Count || _tokens[i + 1].Kind != CommandTokenKind.Number)
                    return Incomplete(preview, "Expected a number after At.", CommandTokenKind.Number);

                atValue = _tokens[i + 1].NumericValue!.Value;
                i += 2;

                if (i < _tokens.Count)
                    return Incomplete(preview, "Nothing may follow the At value.", CommandTokenKind.Enter);

                break;
            }

            return Incomplete(preview, $"Unexpected token '{token.DisplayText}'.");
        }

        if (clauses.Count == 0)
            return Incomplete(preview, null, CommandTokenKind.Number);

        if (!finalize)
        {
            var expected = new List<CommandTokenKind> { CommandTokenKind.Plus, CommandTokenKind.Minus, CommandTokenKind.Thru, CommandTokenKind.At, CommandTokenKind.Enter };
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = expected };
        }

        return Resolve(objectType, clauses, atValue, preview);
    }

    private CommandComposition Incomplete(string preview, string? error, params CommandTokenKind[] expected) => new()
    {
        Tokens = _tokens.ToList(),
        PreviewText = preview,
        Error = error,
        ExpectedNext = expected,
    };

    private CommandComposition Resolve(ObjectType objectType, List<Clause> clauses, double? atValue, string preview)
    {
        var commands = new List<IConsoleCommand> { new ClearSelectionCommand() };
        var targets = new List<PatchedFixture>();

        foreach (var clause in clauses)
        {
            switch (clause.Op)
            {
                case ClauseOp.Anchor:
                case ClauseOp.Plus:
                {
                    if (!TryResolveSingle(objectType, clause.Number, out var fixtures, out var error))
                        return Incomplete(preview, error);

                    foreach (var fixture in fixtures)
                    {
                        commands.Add(new ToggleFixtureCommand(fixture));
                        if (!targets.Contains(fixture)) targets.Add(fixture);
                    }
                    break;
                }

                case ClauseOp.Minus:
                {
                    if (!TryResolveSingle(objectType, clause.Number, out var fixtures, out var error))
                        return Incomplete(preview, error);

                    foreach (var fixture in fixtures)
                    {
                        commands.Add(new RemoveFixtureFromSelectionCommand(fixture));
                        targets.Remove(fixture);
                    }
                    break;
                }

                case ClauseOp.Thru:
                {
                    // Range endpoints are looked up from the clause list itself (the Anchor/Plus/
                    // Minus immediately before this Thru), never guessed - "1 Thru 5" always means
                    // the 1 that was just typed.
                    int index = clauses.IndexOf(clause);
                    double from = clauses[index - 1].Number;
                    double to = clause.Number;

                    if (objectType == ObjectType.Fixture)
                    {
                        commands.Add(new SelectRangeCommand((int)from, (int)to));
                        int lo = (int)Math.Min(from, to), hi = (int)Math.Max(from, to);
                        foreach (var fixture in _context.Patch.Fixtures.Where(f => f.Number >= lo && f.Number <= hi))
                            if (!targets.Contains(fixture)) targets.Add(fixture);
                    }
                    else
                    {
                        int lo = (int)Math.Min(from, to), hi = (int)Math.Max(from, to);
                        for (int n = lo; n <= hi; n++)
                        {
                            var group = _context.Groups.FindByNumber(n);
                            if (group is null) continue; // gaps in a range are expected, same as Fixture Thru
                            commands.Add(new AddGroupToSelectionCommand(group));
                            foreach (var fixture in group.Fixtures)
                                if (!targets.Contains(fixture)) targets.Add(fixture);
                        }
                    }
                    break;
                }
            }
        }

        if (atValue is not null)
        {
            if (targets.Count == 0)
                return Incomplete(preview, "No fixtures resolved to apply At to.");

            commands.Add(new AdjustIntensityCommand(targets, AdjustOperation.Absolute, atValue.Value));
        }

        IConsoleCommand operation = commands.Count == 1 ? commands[0] : new CompositeCommand(commands);

        return new CommandComposition
        {
            Tokens = _tokens.ToList(),
            PreviewText = preview,
            IsComplete = true,
            ReadyOperation = operation,
        };
    }

    private bool TryResolveSingle(ObjectType objectType, double number, out IReadOnlyList<PatchedFixture> fixtures, out string? error)
    {
        if (objectType == ObjectType.Fixture)
        {
            var fixture = _context.Patch.Fixtures.FirstOrDefault(f => f.Number == (int)number);
            if (fixture is null) { fixtures = Array.Empty<PatchedFixture>(); error = $"Fixture {FormatNumber(number)} not found."; return false; }
            fixtures = new[] { fixture };
            error = null;
            return true;
        }

        var group = _context.Groups.FindByNumber((int)number);
        if (group is null) { fixtures = Array.Empty<PatchedFixture>(); error = $"Group {FormatNumber(number)} not found."; return false; }
        fixtures = group.Fixtures;
        error = null;
        return true;
    }

    private static string FormatNumber(double value) => value == Math.Floor(value) ? ((long)value).ToString() : value.ToString("0.##");
}

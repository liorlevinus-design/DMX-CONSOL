using DmxConsole.Application.Commands;
using DmxConsole.Application.Commands.Presets;
using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Core;
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
/// Grammar supported by this slice:
///
///   command    := [objectType] clause+ (AT number)?
///   objectType := FIXTURE | GROUP
///   clause     := number | (PLUS number) | (MINUS number) | (THRU number)
///
/// Fixture is the default object context, so "1 THRU 5" is exactly equivalent to
/// "FIXTURE 1 THRU 5". GROUP remains explicit. THRU always pairs with the number immediately
/// before it (the running "last number"), not necessarily the very first one.
/// </summary>
public sealed class CommandComposer
{
    private readonly ConsoleContext _context;
    private readonly List<CommandToken> _tokens = new();

    public CommandComposer(ConsoleContext context) => _context = context;

    /// <summary>
    /// When true, a finalized selection command begins by clearing the previous Selection.
    /// The CommandSurfaceViewModel flips this to false while an operator is still building the
    /// same selection cycle so consecutive Fixture/Group selections accumulate without a + key.
    /// It becomes true again only after an execution such as AT closes that cycle.
    /// </summary>
    public bool ReplaceSelectionOnResolve { get; set; } = true;

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
                ExpectedNext = new[] { CommandTokenKind.Number, CommandTokenKind.Fixture, CommandTokenKind.Group },
            };
        }

        // CUE establishes object context for the current command (§2/§3), symmetric with FIXTURE/
        // GROUP as a head token. But there is no existing Application-layer command for targeting
        // a Cue by number from the Command Surface yet - Cues aren't a FixtureSelection-like
        // multi-select concept in this console's architecture (which CueList would "Cue 5" even
        // mean, with multiple Executors potentially each running their own?). Recognized honestly
        // as incomplete rather than silently falling through to Fixture-selection grammar or
        // fabricating a resolution - a real gap, tracked in docs/COMMAND_SURFACE_KEY_SPEC.md, not
        // guessed at here.
        if (_tokens[0].Kind == CommandTokenKind.Cue)
        {
            return Incomplete(preview, "Cue numeric commands are not implemented yet - no Application-layer command exists for targeting a Cue by number from the Command Surface.");
        }

        // Next/Last (docs/COMMAND_SURFACE_KEY_SPEC.md §9) move a single-fixture cursor through the
        // CURRENT ordered selection - they never build a new selection via numeric clauses, and
        // (like every other unambiguous terminal action in this composer) execute the moment the
        // single token is pushed, regardless of the `finalize` flag - Enter is never required.
        if (_tokens.Count == 1 && _tokens[0].Kind is CommandTokenKind.Next or CommandTokenKind.Previous)
        {
            IConsoleCommand cursorCommand = _tokens[0].Kind == CommandTokenKind.Next
                ? new NextFixtureCommand()
                : new PreviousFixtureCommand();
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, ReadyOperation = cursorCommand };
        }

        // A lone family token (COLOR, POSITION, ...) is waiting for HOME/RELEASE/PRESET - not an
        // error, just incomplete (§4/§6/§7).
        if (_tokens.Count == 1 && FamilyFor(_tokens[0].Kind) is not null)
        {
            return new CommandComposition
            {
                Tokens = _tokens.ToList(), PreviewText = preview,
                ExpectedNext = new[] { CommandTokenKind.Home, CommandTokenKind.Release, CommandTokenKind.Preset },
            };
        }

        // HOME (docs/COMMAND_SURFACE_KEY_SPEC.md §6) - bare or family-qualified, always resolved
        // against the CURRENT persistent Selection (never a numeric selection clause), and always
        // self-terminating the instant the shape is complete, exactly like Next/Last above.
        if (_tokens.Count == 1 && _tokens[0].Kind == CommandTokenKind.Home)
            return ResolveHome(family: null, preview);

        if (_tokens.Count == 2 && FamilyFor(_tokens[0].Kind) is { } homeFamily && _tokens[1].Kind == CommandTokenKind.Home)
            return ResolveHome(homeFamily, preview);

        // Family-qualified RELEASE (§7) - e.g. "POSITION RELEASE" - self-terminating, resolved
        // against the current Selection. Bare RELEASE is deliberately NOT handled here: per
        // explicit operator direction, bare RELEASE is a two-press gesture the CommandSurface
        // itself owns (release-on-first-press, escalate-to-Clear-Entire-Editor if ENTER follows
        // immediately) - see CommandSurfaceViewModel.PressRelease, not this grammar.
        if (_tokens.Count == 2 && FamilyFor(_tokens[0].Kind) is { } releaseFamily && _tokens[1].Kind == CommandTokenKind.Release)
        {
            var targets = _context.Selection.Items.ToList();
            if (targets.Count == 0) return Incomplete(preview, "Select at least one fixture first.");
            return new CommandComposition
            {
                Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true,
                ReadyOperation = new ReleaseCommand(targets, releaseFamily),
            };
        }

        // COLOR PRESET 5 [ENTER] (§4) - family-qualified Preset recall against the current
        // Selection. Ends in a numeric/reference token, so per §14 it needs ENTER to commit,
        // same as any other numeric-terminated command.
        if (_tokens.Count >= 2 && FamilyFor(_tokens[0].Kind) is { } presetFamily && _tokens[1].Kind == CommandTokenKind.Preset)
            return ResolvePresetRecall(presetFamily, preview, finalize);

        if (_tokens.Count == 2 && _tokens[1].Kind == CommandTokenKind.Recall &&
            _tokens[0].Kind is CommandTokenKind.Fixture or CommandTokenKind.Group)
        {
            if (!finalize) return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Enter } };
            return ResolveRecall(_tokens[0].Kind, preview);
        }

        if (_tokens.Count == 2 && _tokens[0].Kind == CommandTokenKind.At && _tokens[1].Kind == CommandTokenKind.Recall)
        {
            if (!finalize) return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Enter } };
            if (_context.SelectionCycle.LastAtPercent is not double lastAt)
                return Incomplete(preview, "No previous At value to recall.");
            if (_context.Selection.Items.Count == 0)
                return Incomplete(preview, "Select at least one fixture before At recall.");
            return new CommandComposition
            {
                Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, EndsSelectionCycle = true,
                AppliedAtPercent = lastAt,
                ReadyOperation = new AdjustIntensityCommand(_context.Selection.Items.ToList(), AdjustOperation.Absolute, lastAt),
            };
        }

        var head = _tokens[0];
        ObjectType objectType;
        int i;

        if (head.Kind == CommandTokenKind.Number)
        {
            // Fixture is the console's default object family. A bare number/range therefore
            // means Fixture without injecting a fake token into the visible command line.
            objectType = ObjectType.Fixture;
            i = 0;
        }
        else if (head.Kind is CommandTokenKind.Fixture or CommandTokenKind.Group)
        {
            objectType = head.Kind == CommandTokenKind.Fixture ? ObjectType.Fixture : ObjectType.Group;
            i = 1;
        }
        else
        {
            return Incomplete(preview, "Expected a fixture number, Fixture, or Group.",
                CommandTokenKind.Number, CommandTokenKind.Fixture, CommandTokenKind.Group);
        }

        var clauses = new List<Clause>();
        double? atValue = null;
        double? lastNumber = null;
        bool fullSelfTerminates = false;

        // Tiny explicit state machine over the remaining tokens - see the class doc comment for the grammar.
        while (i < _tokens.Count)
        {
            var token = _tokens[i];

            if (token.Kind == CommandTokenKind.Number)
            {
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

            // FULL (docs/COMMAND_SURFACE_KEY_SPEC.md §12) - semantic 100%, an unambiguous terminal
            // Action key: "1 THRU 10 FULL" self-terminates immediately, never waiting for Enter,
            // exactly like the family-qualified HOME/RELEASE self-termination above.
            if (token.Kind == CommandTokenKind.Full)
            {
                if (i + 1 < _tokens.Count)
                    return Incomplete(preview, "Nothing may follow Full.", CommandTokenKind.Enter);

                atValue = 100;
                fullSelfTerminates = true;
                i++;
                break;
            }

            return Incomplete(preview, $"Unexpected token '{token.DisplayText}'.");
        }

        if (clauses.Count == 0)
            return Incomplete(preview, null, CommandTokenKind.Number);

        if (!finalize && !fullSelfTerminates)
        {
            var expected = new List<CommandTokenKind> { CommandTokenKind.Plus, CommandTokenKind.Minus, CommandTokenKind.Thru, CommandTokenKind.At, CommandTokenKind.Full, CommandTokenKind.Enter };
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = expected };
        }

        return Resolve(objectType, clauses, atValue, preview);
    }

    private CommandComposition ResolveRecall(CommandTokenKind kind, string preview)
    {
        IReadOnlyList<PatchedFixture> fixtures;
        int? groupNumber = null;
        if (kind == CommandTokenKind.Fixture)
        {
            fixtures = _context.SelectionCycle.LastSelection;
            if (fixtures.Count == 0) return Incomplete(preview, "No previous fixture selection to recall.");
        }
        else
        {
            groupNumber = _context.SelectionCycle.LastGroupNumber;
            var group = groupNumber is int number ? _context.Groups.FindByNumber(number) : null;
            if (group is null) return Incomplete(preview, "No previous group to recall.");
            fixtures = group.Fixtures;
        }

        return new CommandComposition
        {
            Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true,
            ResolvedGroupNumber = groupNumber,
            ReadyOperation = new ReplaceSelectionCommand(fixtures),
        };
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
        var commands = new List<IConsoleCommand>();
        if (ReplaceSelectionOnResolve) commands.Add(new ClearSelectionCommand());

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
                        // Add is intentionally idempotent. A GUI selection mirrored into this
                        // composer must survive an immediate AT instead of toggling itself off.
                        commands.Add(new AddFixtureToSelectionCommand(fixture));
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
                            if (group is null) continue;
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
            EndsSelectionCycle = atValue is not null,
            ResolvedGroupNumber = objectType == ObjectType.Group ? (int?)clauses.Last().Number : null,
            AppliedAtPercent = atValue,
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

    /// <summary>Maps a family-keyword token to its AttributeClass, or null if this token isn't
    /// one of the six family keywords (docs/COMMAND_SURFACE_KEY_SPEC.md §4/§23.1's unified model).</summary>
    private static AttributeClass? FamilyFor(CommandTokenKind kind) => kind switch
    {
        CommandTokenKind.Intensity => AttributeClass.Intensity,
        CommandTokenKind.Position => AttributeClass.Position,
        CommandTokenKind.Color => AttributeClass.Color,
        CommandTokenKind.Beam => AttributeClass.Beam,
        CommandTokenKind.Image => AttributeClass.Image,
        CommandTokenKind.Shape => AttributeClass.Shape,
        _ => null,
    };

    /// <summary>Builds HOME's batched command (§6): each target fixture's own channel(s) go back
    /// to their own FixtureChannel.DefaultValue - never one shared value, since different
    /// fixtures/profiles can have different defaults for the "same" channel type. `family: null`
    /// means every channel of the fixture (bare HOME); a family restricts to that family's
    /// channels only (e.g. COLOR HOME). One CompositeCommand - one Undo step for the whole
    /// operation, same pattern as EncoderDrawerViewModel.Home for a single channel type.</summary>
    private CommandComposition ResolveHome(AttributeClass? family, string preview)
    {
        var targets = _context.Selection.Items;
        if (targets.Count == 0) return Incomplete(preview, "Select at least one fixture first.");

        var commands = new List<IConsoleCommand>();
        foreach (var fixture in targets)
        {
            var channels = family is { } cls ? fixture.ChannelsForAttribute(cls) : fixture.Mode.Channels;
            foreach (var channel in channels)
                commands.Add(new SetAttributeValueCommand(new List<PatchedFixture> { fixture }, channel.Type, channel.DefaultValue));
        }

        if (commands.Count == 0)
            return Incomplete(preview, family is { } f ? $"No {f} channels on the current selection." : "No channels to Home on the current selection.");

        IConsoleCommand operation = commands.Count == 1 ? commands[0] : new CompositeCommand(commands);
        return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, ReadyOperation = operation };
    }

    /// <summary>"&lt;Family&gt; PRESET &lt;number&gt; [ENTER]" (§4) - resolved against the current
    /// Selection. Ends in a numeric token, so per §14 it needs ENTER to commit, same as any other
    /// numeric-terminated command (mirrors the AT grammar's own finalize gating).</summary>
    private CommandComposition ResolvePresetRecall(AttributeClass family, string preview, bool finalize)
    {
        if (_tokens.Count == 2)
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Number } };

        if (_tokens.Count != 3 || _tokens[2].Kind != CommandTokenKind.Number)
            return Incomplete(preview, "Expected a number after Preset.", CommandTokenKind.Number);

        if (!finalize)
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Enter } };

        int number = (int)_tokens[2].NumericValue!.Value;
        var preset = _context.Presets.FindByNumber(family, number);
        if (preset is null) return Incomplete(preview, $"{family} Preset {number} not found.");

        var targets = _context.Selection.Items.ToList();
        if (targets.Count == 0) return Incomplete(preview, "Select at least one fixture first.");

        return new CommandComposition
        {
            Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, EndsSelectionCycle = true,
            ReadyOperation = new ApplyPresetCommand(targets, preset),
        };
    }
}

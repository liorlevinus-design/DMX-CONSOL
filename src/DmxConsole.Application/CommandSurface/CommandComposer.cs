using DmxConsole.Application.Commands;
using DmxConsole.Application.Commands.Cues;
using DmxConsole.Application.Commands.Groups;
using DmxConsole.Application.Commands.Patch;
using DmxConsole.Application.Commands.Presets;
using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
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
                // Implicit Fixture context (RELEASE-panel/Quick-Patch stabilization slice, §4): a
                // bare Number as the very first token defaults to FIXTURE - injected here as a
                // REAL token, not merely inferred later in Build(), so "1 THRU 8" and
                // "FIXTURE 1 THRU 8" produce byte-identical CommandCompositions from this point
                // on (same Tokens, same PreviewText, same Resolve() path, same SelectionCycle
                // behavior) - never a second, parallel "numbers without object" engine.
                if (_tokens.Count == 0 && token.Kind == CommandTokenKind.Number)
                    _tokens.Add(CommandToken.Simple(CommandTokenKind.Fixture));
                _tokens.Add(token);
                return Build(finalize: false);
        }
    }

    /// <summary>Resets composition state without treating it as an "empty Clear" signal -
    /// for a caller that just successfully dispatched a ReadyOperation and wants a fresh line.</summary>
    public void Reset() => _tokens.Clear();

    // ---- Parsing --------------------------------------------------------------------------

    private enum ObjectType { Fixture, Group }
    private enum ClauseOp { Anchor, Plus, Minus, Thru, Odd, Even }
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

        // CAPTURE ALL (§8) - always instant/self-terminating, resolved against the whole patch
        // (never the current Selection, and never gated on any family). The caller
        // (CommandSurfaceViewModel.PressCaptureAll) resets the composer before pushing this so it
        // is always the sole token here in practice, but the check itself doesn't depend on that.
        if (_tokens.Count == 1 && _tokens[0].Kind == CommandTokenKind.CaptureAll)
        {
            return new CommandComposition
            {
                Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true,
                ReadyOperation = new CaptureAllCommand(_context.Patch.Fixtures.ToList()),
            };
        }

        // CUE establishes object context for the current command (§2/§3), symmetric with FIXTURE/
        // GROUP as a head token. Cues aren't a FixtureSelection-like multi-select concept in this
        // console's architecture, so most CUE-numeric grammar remains a real, honestly-reported
        // gap (tracked in docs/COMMAND_SURFACE_KEY_SPEC.md) rather than something silently falling
        // through to Fixture-selection grammar. The ONE exception (Cue-timing slice): "CUE <n>
        // [THRU <n>] TIME <value>[/<value>] ENTER" - sets Cue(s) In/Out fade timing, resolved by
        // ResolveCueCommand below. Nothing else after a Cue number/range is implemented yet.
        if (_tokens[0].Kind == CommandTokenKind.Cue)
        {
            return ResolveCueCommand(preview, finalize);
        }

        // DMX DIRECT ADDRESSING - establishes object/domain context for THIS command only
        // (never sticky; the next bare-numeric command defaults back to FIXTURE exactly like any
        // other completed command, since ObjectType/DMX-mode is never persisted anywhere).
        if (_tokens[0].Kind == CommandTokenKind.Dmx)
        {
            return ResolveDmx(finalize, preview);
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

        // Selection History rule slice: bare ODD/EVEN/REVERSE - no FIXTURE/GROUP prefix - filter/
        // transform the CURRENT Selection directly, exactly like bare Next/Last above (self-
        // terminating, Enter never required, never a numeric clause). This is distinct from
        // Odd/Even appearing INSIDE a Fixture/Group clause sequence (e.g. "FIXTURE 1 THRU 10
        // ODD") - that path is handled later, in the object-clause while loop, and is unaffected;
        // this one only ever fires when Odd/Even/Reverse is the ENTIRE composition. A pure
        // Selection transform, never a programming execution - EndsSelectionCycle stays at its
        // default false, same as Next/Last.
        if (_tokens.Count == 1 && _tokens[0].Kind is CommandTokenKind.Odd or CommandTokenKind.Even or CommandTokenKind.Reverse)
        {
            if (_context.Selection.Items.Count == 0)
                return Incomplete(preview, "Select at least one fixture first.");

            IConsoleCommand filterCommand = _tokens[0].Kind switch
            {
                CommandTokenKind.Odd => new SelectOddCommand(),
                CommandTokenKind.Even => new SelectEvenCommand(),
                _ => new ReverseSelectionCommand(),
            };
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, ReadyOperation = filterCommand };
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
                Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, EndsSelectionCycle = true,
                ReadyOperation = new ReleaseCommand(targets, releaseFamily),
            };
        }

        // PAN RELEASE / ZOOM RELEASE (§7 PARAMETER RELEASE) - a lone Parameter token is waiting
        // for RELEASE (there is no PARAMETER HOME in v1). Self-terminating like family RELEASE,
        // resolved against the current Selection via ReleaseParameterCommand - a third, finer
        // granularity than family RELEASE, never conflated with it.
        if (_tokens.Count == 1 && _tokens[0].Kind == CommandTokenKind.Parameter)
        {
            return new CommandComposition
            {
                Tokens = _tokens.ToList(), PreviewText = preview,
                ExpectedNext = new[] { CommandTokenKind.Release },
            };
        }

        if (_tokens.Count == 2 && _tokens[0].Kind == CommandTokenKind.Parameter && _tokens[1].Kind == CommandTokenKind.Release)
        {
            var channelType = (ChannelType)_tokens[0].SemanticPayload!;
            var targets = _context.Selection.Items.ToList();
            if (targets.Count == 0) return Incomplete(preview, "Select at least one fixture first.");
            return new CommandComposition
            {
                Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, EndsSelectionCycle = true,
                ReadyOperation = new ReleaseParameterCommand(targets, channelType),
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

        // Bare STORE (Store-grammar slice) - the operator pressed STORE with no selection clause
        // typed on THIS line at all (a Selection may already exist from an earlier, separate
        // command, or from a GUI click - exactly the scenario this slice replaces the old
        // "STORE immediately shortcuts to whatever EditorContext happens to be Group" bug with):
        // no selection-building commands are needed, targets = whatever is currently selected.
        if (_tokens[0].Kind == CommandTokenKind.Store)
            return ResolveStore(preview, finalize, storeIndex: 0, precedingCommands: new List<IConsoleCommand>(), targets: _context.Selection.Items.ToList());

        var head = _tokens[0];
        ObjectType objectType;
        int i;

        if (head.Kind == CommandTokenKind.Number)
        {
            // Unreachable in normal operation since Push() now injects an explicit Fixture token
            // ahead of a leading bare Number (§4) - kept as a defensive fallback for a caller that
            // builds _tokens some other way, so a stray bare-Number head still resolves to Fixture
            // rather than falling into the generic "Expected..." error below.
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
        List<double>? atControlPoints = null;
        double? lastNumber = null;
        bool fullSelfTerminates = false;
        int? storeIndex = null;

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

            // ODD/EVEN (Store-grammar slice, §D) - operate on Selection ORDER, not fixture
            // numbers/IDs: filters whatever has been resolved so far (in order) down to the
            // odd/even 1-based positions, exactly like FixtureSelection.FilterOdd/FilterEven
            // itself does at dispatch time. No operand, so it advances i by 1 like Full/Store.
            if (token.Kind is CommandTokenKind.Odd or CommandTokenKind.Even)
            {
                clauses.Add(new Clause(token.Kind == CommandTokenKind.Odd ? ClauseOp.Odd : ClauseOp.Even, 0));
                i++;
                continue;
            }

            // STORE (Store-grammar slice, §A/§B) - pivots out of Fixture/Group selection grammar
            // entirely into the Store sub-grammar (Group/Cue/Preset target). Never valid together
            // with AT/FULL on the same line (those already `break` this loop themselves) - STORE
            // simply stops clause parsing here, same shape as At/Full below.
            if (token.Kind == CommandTokenKind.Store)
            {
                storeIndex = i;
                break;
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
                // Quick Patch form A (Quick Patch stabilization slice): "FIXTURE <clauses> AT DMX
                // <address> [ENTER]" - AT followed by DMX (not a plain Number) means "patch these
                // fixture numbers starting at this DMX address", never an intensity percentage.
                // Never valid for Group - Quick Patch only ever creates Fixtures.
                if (i + 1 < _tokens.Count && _tokens[i + 1].Kind == CommandTokenKind.Dmx)
                {
                    if (objectType != ObjectType.Fixture)
                        return Incomplete(preview, "Quick Patch applies to Fixture numbers only, not Group.");

                    int addressIndex = i + 2;
                    if (addressIndex >= _tokens.Count || _tokens[addressIndex].Kind != CommandTokenKind.Number)
                        return Incomplete(preview, "Expected a DMX address after AT DMX.", CommandTokenKind.Number);
                    if (addressIndex + 1 < _tokens.Count)
                        return Incomplete(preview, "Nothing may follow the DMX address.", CommandTokenKind.Enter);

                    if (!finalize)
                        return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Enter } };

                    return BuildQuickPatchComposition(preview, ClausesToNumbers(clauses), (int)_tokens[addressIndex].NumericValue!.Value);
                }

                if (i + 1 >= _tokens.Count || _tokens[i + 1].Kind != CommandTokenKind.Number)
                    return Incomplete(preview, "Expected a number after At.", CommandTokenKind.Number);

                var controlPoints = new List<double> { _tokens[i + 1].NumericValue!.Value };
                i += 2;

                // Value-distribution slice: THRU AFTER AT is a DIFFERENT grammatical meaning than
                // THRU before AT (object-range, handled earlier in this same loop) - here it adds
                // another control point to a value path ("AT 20 THRU 60" = distribute 20->60
                // across the ordered targets; "AT 20 THRU 60 THRU 20" = 20->60->20). Resolved
                // purely by POSITION relative to At, never a second token kind - the loop only
                // ever reaches this while-loop once it has already broken into the At branch.
                while (i < _tokens.Count && _tokens[i].Kind == CommandTokenKind.Thru)
                {
                    if (i + 1 >= _tokens.Count || _tokens[i + 1].Kind != CommandTokenKind.Number)
                        return Incomplete(preview, "Expected a number after Thru.", CommandTokenKind.Number);

                    controlPoints.Add(_tokens[i + 1].NumericValue!.Value);
                    i += 2;
                }

                if (i < _tokens.Count)
                    return Incomplete(preview, "Nothing may follow the At value.", CommandTokenKind.Enter);

                atControlPoints = controlPoints;
                break;
            }

            // FULL (docs/COMMAND_SURFACE_KEY_SPEC.md §12) - semantic 100%, an unambiguous terminal
            // Action key: "1 THRU 10 FULL" self-terminates immediately, never waiting for Enter,
            // exactly like the family-qualified HOME/RELEASE self-termination above.
            if (token.Kind == CommandTokenKind.Full)
            {
                if (i + 1 < _tokens.Count)
                    return Incomplete(preview, "Nothing may follow Full.", CommandTokenKind.Enter);

                atControlPoints = new List<double> { 100 };
                fullSelfTerminates = true;
                i++;
                break;
            }

            return Incomplete(preview, $"Unexpected token '{token.DisplayText}'.");
        }

        if (storeIndex is int idx)
        {
            var (selectionCommands, storeTargets, buildError) = BuildSelectionCommands(objectType, clauses);
            if (buildError is not null) return Incomplete(preview, buildError);
            return ResolveStore(preview, finalize, idx, selectionCommands, storeTargets);
        }

        if (clauses.Count == 0)
            return Incomplete(preview, null, CommandTokenKind.Number);

        if (!finalize && !fullSelfTerminates)
        {
            var expected = new List<CommandTokenKind> { CommandTokenKind.Plus, CommandTokenKind.Minus, CommandTokenKind.Thru, CommandTokenKind.Odd, CommandTokenKind.Even, CommandTokenKind.At, CommandTokenKind.Full, CommandTokenKind.Store, CommandTokenKind.Enter };
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = expected };
        }

        return Resolve(objectType, clauses, atControlPoints, preview);
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

    /// <summary>Builds the selection-mutating commands (Clear/Add/Remove/Range/Group/Odd/Even) for
    /// a clause list, plus the resulting ordered fixture list those commands would leave selected
    /// - the same shadow bookkeeping <see cref="Resolve"/> already needs for AT, now also reused by
    /// the Store-grammar pivot (Store-grammar slice, §D) so "FIXTURE 1 THRU 10 ODD STORE GROUP 1"
    /// knows exactly which (odd-position) fixtures to store, in order, before any command runs.
    /// Returns a non-null Error instead of throwing/Incomplete-ing directly, so both callers can
    /// decide how to wrap it (Resolve already has its own preview string in scope; ResolveStore's
    /// callers pass a possibly-different one).</summary>
    private (List<IConsoleCommand> Commands, List<PatchedFixture> Targets, string? Error) BuildSelectionCommands(ObjectType objectType, List<Clause> clauses)
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
                        return (commands, targets, error);

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
                        return (commands, targets, error);

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

                case ClauseOp.Odd:
                {
                    commands.Add(new SelectOddCommand());
                    var kept = targets.Where((_, idx) => idx % 2 == 0).ToList();
                    targets.Clear();
                    targets.AddRange(kept);
                    break;
                }

                case ClauseOp.Even:
                {
                    commands.Add(new SelectEvenCommand());
                    var kept = targets.Where((_, idx) => idx % 2 == 1).ToList();
                    targets.Clear();
                    targets.AddRange(kept);
                    break;
                }
            }
        }

        return (commands, targets, null);
    }

    private CommandComposition Resolve(ObjectType objectType, List<Clause> clauses, List<double>? atControlPoints, string preview)
    {
        var (commands, targets, buildError) = BuildSelectionCommands(objectType, clauses);
        if (buildError is not null) return Incomplete(preview, buildError);

        if (atControlPoints is not null)
        {
            if (targets.Count == 0)
                return Incomplete(preview, "No fixtures resolved to apply At to.");

            if (atControlPoints.Count == 1)
            {
                // Single-value AT - unchanged from before the value-distribution slice: one
                // AdjustIntensityCommand for every target, same value.
                commands.Add(new AdjustIntensityCommand(targets, AdjustOperation.Absolute, atControlPoints[0]));
            }
            else
            {
                // Value distribution (§1/§2/§4/§7): interpolate along the control-point path,
                // walking TARGETS IN THEIR EXISTING SELECTION ORDER (never re-sorted by fixture
                // number/ID - `targets` here is exactly the ordered list BuildSelectionCommands
                // already produced, the SAME shadow list Odd/Even/Store already rely on being
                // selection-order-correct). One AdjustIntensityCommand PER fixture with its own
                // interpolated value - the exact same command/Programmer-write path single-value
                // AT already uses, never a second write mechanism - all batched into the SAME
                // CompositeCommand as any selection-building commands, so a failure anywhere
                // rolls back the whole transaction (CompositeCommand's existing atomicity),
                // never a partially-applied distribution.
                for (int index = 0; index < targets.Count; index++)
                {
                    double t = targets.Count == 1 ? 0.0 : (double)index / (targets.Count - 1);
                    double value = InterpolateAlongPath(atControlPoints, t);
                    commands.Add(new AdjustIntensityCommand(new List<PatchedFixture> { targets[index] }, AdjustOperation.Absolute, value));
                }
            }
        }

        IConsoleCommand operation = commands.Count == 1 ? commands[0] : new CompositeCommand(commands);

        // ResolvedGroupNumber (SelectionCycle's "last Group recalled") should reflect the last
        // NUMBERED clause (Anchor/Plus/Thru), never an Odd/Even modifier (which carries Number=0
        // and would otherwise misreport recall as "Group 0").
        var lastNumberedClause = clauses.LastOrDefault(c => c.Op is ClauseOp.Anchor or ClauseOp.Plus or ClauseOp.Thru);

        return new CommandComposition
        {
            Tokens = _tokens.ToList(),
            PreviewText = preview,
            IsComplete = true,
            EndsSelectionCycle = atControlPoints is not null,
            ResolvedGroupNumber = objectType == ObjectType.Group && clauses.Any(c => c.Op is ClauseOp.Anchor or ClauseOp.Plus or ClauseOp.Thru)
                ? (int?)lastNumberedClause.Number : null,
            // Only a single scalar AT value is recallable (AT RECALL replays one percentage) - a
            // multi-point distribution has no single "the value" to remember, so it's left null
            // rather than misrepresenting the path as one number.
            AppliedAtPercent = atControlPoints is { Count: 1 } single ? single[0] : null,
            ReadyOperation = operation,
        };
    }

    /// <summary>Value-distribution slice (§7): linear interpolation across one or more control
    /// points, treating them as evenly-spaced waypoints along a single continuous 0..1 path -
    /// never split into crude integer-sized blocks. t=0 always yields the first control point
    /// exactly (so a single-fixture target, which always computes t=0, gets exactly the FIRST
    /// value, never an average - §6) and t=1 always yields the last one exactly.</summary>
    private static double InterpolateAlongPath(IReadOnlyList<double> controlPoints, double t)
    {
        if (controlPoints.Count == 1) return controlPoints[0];

        int segments = controlPoints.Count - 1;
        double segmentPosition = Math.Clamp(t, 0.0, 1.0) * segments;
        int segmentIndex = Math.Min((int)Math.Floor(segmentPosition), segments - 1);
        double localT = segmentPosition - segmentIndex;
        return controlPoints[segmentIndex] + (controlPoints[segmentIndex + 1] - controlPoints[segmentIndex]) * localT;
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
    /// one of the six family keywords (docs/COMMAND_SURFACE_KEY_SPEC.md §4/§23.1's unified model).
    /// Public so other UI-independent state readers (e.g. the PARAMETER PICKER's "which family is
    /// currently armed" query) can reuse this exact mapping instead of re-declaring a second one -
    /// this composer's own grammar remains the sole place the mapping is DEFINED.</summary>
    public static AttributeClass? FamilyFor(CommandTokenKind kind) => kind switch
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
        return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, EndsSelectionCycle = true, ReadyOperation = operation };
    }

    /// <summary>
    /// CUE &lt;n&gt; [THRU &lt;n&gt;] TIME &lt;value&gt;[/&lt;value&gt;] ENTER (Cue-timing slice) -
    /// the one implemented CUE-numeric grammar branch. Cue-number range parsing deliberately
    /// mirrors the FIXTURE/GROUP THRU pattern (a bare number anchors, THRU extends to a second
    /// number) rather than reusing the Clause/BuildSelectionCommands machinery wholesale - Cues
    /// have no Selection concept to build (CLAUDE.md §1: Selection != Playback), so this is a
    /// small, self-contained clause reader over Cue.Number instead. Anything after the Cue
    /// number(s) other than TIME is an honest "not implemented" gap, same spirit as the doc
    /// comment on the CUE branch in Build() above - never guessed at.
    /// </summary>
    private CommandComposition ResolveCueCommand(string preview, bool finalize)
    {
        int i = 1;
        if (i >= _tokens.Count)
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Number } };

        if (_tokens[i].Kind != CommandTokenKind.Number)
            return Incomplete(preview, "Expected a Cue number.", CommandTokenKind.Number);

        double fromNumber = _tokens[i].NumericValue!.Value;
        double toNumber = fromNumber;
        i++;

        if (i < _tokens.Count && _tokens[i].Kind == CommandTokenKind.Thru)
        {
            i++;
            if (i >= _tokens.Count)
                return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Number } };
            if (_tokens[i].Kind != CommandTokenKind.Number)
                return Incomplete(preview, "Expected a Cue number after Thru.", CommandTokenKind.Number);

            toNumber = _tokens[i].NumericValue!.Value;
            i++;
        }

        if (i >= _tokens.Count)
        {
            // Genuinely ambiguous only while still typing (ENTER not yet pressed) - THRU could
            // still extend the range, or TIME could still follow. On finalize with nothing else
            // typed, this is the same honest "not implemented" gap as any other post-number token
            // (Trigger/Wait/Follow/...) - never silently treated as complete.
            if (!finalize)
                return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Thru, CommandTokenKind.Timing } };
            return Incomplete(preview, "Cue numeric commands are not implemented yet - only CUE ... TIME ... is supported by the Command Surface.");
        }

        if (_tokens[i].Kind == CommandTokenKind.Timing)
            return ResolveCueTiming(preview, finalize, fromNumber, toNumber, i + 1);

        return Incomplete(preview, "Cue numeric commands are not implemented yet - only CUE ... TIME ... is supported by the Command Surface.");
    }

    /// <summary>
    /// The TIME half of CUE &lt;n&gt; [THRU &lt;n&gt;] TIME &lt;value&gt;[/&lt;value&gt;] ENTER -
    /// `i` points just past the TIME token. A single numeric value sets both In and Out to the
    /// same value; an optional "/" splits it into In/Out explicitly (§ In=first, Out=second, never
    /// the reverse). Resolved into one <see cref="SetCueTimingCommand"/> per matched Cue, batched
    /// into one CompositeCommand for a THRU range so the whole range update is one atomic Undo
    /// step (CLAUDE.md §14) - never N separate Undo entries. Only ever touches CueTiming.TimeIn/
    /// TimeOut (DelayIn/DelayOut carried through unchanged by SetCueTimingCommand itself) and never
    /// TriggerMode/WaitTime - Cue Trigger Semantics (CLAUDE.md §9) are completely untouched by this
    /// grammar.
    /// </summary>
    private CommandComposition ResolveCueTiming(string preview, bool finalize, double fromNumber, double toNumber, int i)
    {
        if (i >= _tokens.Count)
        {
            if (!finalize) return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Number } };
            return Incomplete(preview, "TIME VALUE IS MISSING");
        }
        if (_tokens[i].Kind != CommandTokenKind.Number)
            return Incomplete(preview, "Expected a numeric Time value.", CommandTokenKind.Number);

        double timeIn = _tokens[i].NumericValue!.Value;
        double timeOut = timeIn;
        i++;

        // Whether an In/Out split ("/") was actually consumed on THIS composition - governs
        // whether Slash still belongs in ExpectedNext below (Command Surface UI gating, e.g. the
        // contextual "/" softkey AND the fixed TIME key's own enable state, both of which read
        // ExpectedNext/CanPressToken - never a separate UI-owned check). A dangling second Slash
        // is still rejected explicitly a few lines down, same as before.
        bool sawSlash = false;

        if (i < _tokens.Count && _tokens[i].Kind == CommandTokenKind.Slash)
        {
            sawSlash = true;
            i++;
            if (i >= _tokens.Count)
            {
                if (!finalize) return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Number } };
                return Incomplete(preview, "Expected an Out time after '/'.", CommandTokenKind.Number);
            }
            if (_tokens[i].Kind != CommandTokenKind.Number)
                return Incomplete(preview, "Expected an Out time after '/'.", CommandTokenKind.Number);

            timeOut = _tokens[i].NumericValue!.Value;
            i++;

            if (i < _tokens.Count && _tokens[i].Kind == CommandTokenKind.Slash)
                return Incomplete(preview, "Time accepts at most one '/' split (In/Out).");
        }

        if (i < _tokens.Count)
            return Incomplete(preview, "Nothing may follow the Time value.", CommandTokenKind.Enter);

        if (timeIn < 0 || timeOut < 0)
            return Incomplete(preview, "Time values must be zero or greater.");

        const double MaxCueTimeSeconds = 86400; // 24 hours - a sane ceiling for a cue fade, well under TimeSpan's own limit.
        if (timeIn > MaxCueTimeSeconds || timeOut > MaxCueTimeSeconds)
            return Incomplete(preview, "Time values must be 24 hours or less.");

        if (!finalize)
        {
            // Right after a bare In value ("CUE 1 TIME 8"), both a commit (Enter, scalar
            // In=Out=8) and a still-open In/Out split (Slash) are valid next tokens - once a
            // split has already been consumed ("CUE 1 TIME 8/10"), only Enter remains valid
            // (a second Slash is a hard error, handled above).
            var expected = sawSlash
                ? new[] { CommandTokenKind.Enter }
                : new[] { CommandTokenKind.Enter, CommandTokenKind.Slash };
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = expected };
        }

        if (_context.PrimaryCueList is not { } cueList)
            return Incomplete(preview, "No Cue List available to edit.");

        double lo = Math.Min(fromNumber, toNumber), hi = Math.Max(fromNumber, toNumber);
        var cues = cueList.Cues.Where(c => c.Number >= lo && c.Number <= hi).OrderBy(c => c.Number).ToList();
        if (cues.Count == 0)
        {
            return Incomplete(preview, fromNumber == toNumber
                ? $"Cue {FormatNumber(fromNumber)} not found."
                : $"No cues found in range {FormatNumber(fromNumber)} THRU {FormatNumber(toNumber)}.");
        }

        var commands = cues
            .Select(c => (IConsoleCommand)new SetCueTimingCommand(cueList, c, TimeSpan.FromSeconds(timeIn), TimeSpan.FromSeconds(timeOut)))
            .ToList();
        IConsoleCommand operation = commands.Count == 1 ? commands[0] : new CompositeCommand(commands);

        return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, ReadyOperation = operation };
    }

    /// <summary>
    /// STORE (Store-grammar slice, §A/§B) - "&lt;selection/context&gt; STORE &lt;target&gt;
    /// &lt;number&gt; ENTER". STORE alone is never enough information and never silently defaults
    /// to any target - the grammar only completes once an explicit target object AND (for
    /// Group/Cue, always; for Preset, unless the operator opens the family/number panel) an
    /// explicit number are present. `targets`/`precedingCommands` were already built by
    /// BuildSelectionCommands (or are the current Selection verbatim, for a bare "STORE ...") -
    /// this method only ever appends to them, never re-derives them.
    /// </summary>
    private CommandComposition ResolveStore(string preview, bool finalize, int storeIndex, List<IConsoleCommand> precedingCommands, List<PatchedFixture> targets)
    {
        int i = storeIndex + 1;

        if (i >= _tokens.Count)
        {
            if (!finalize)
            {
                return new CommandComposition
                {
                    Tokens = _tokens.ToList(), PreviewText = preview,
                    ExpectedNext = new[]
                    {
                        CommandTokenKind.Group, CommandTokenKind.Cue, CommandTokenKind.Preset,
                        CommandTokenKind.Intensity, CommandTokenKind.Position, CommandTokenKind.Color,
                        CommandTokenKind.Beam, CommandTokenKind.Image, CommandTokenKind.Shape,
                    },
                };
            }
            return Incomplete(preview, "STORE TARGET IS MISSING");
        }

        var targetToken = _tokens[i];

        if (targetToken.Kind == CommandTokenKind.Group)
            return ResolveStoreGroup(preview, finalize, i + 1, precedingCommands, targets);

        if (targetToken.Kind == CommandTokenKind.Cue)
            return ResolveStoreCue(preview, finalize, i + 1, precedingCommands, targets);

        if (targetToken.Kind == CommandTokenKind.Preset)
            return ResolveStorePreset(preview, finalize, i + 1, precedingCommands, targets, bareForm: true, explicitFamily: null);

        if (FamilyFor(targetToken.Kind) is { } family)
            return ResolveStorePreset(preview, finalize, i + 1, precedingCommands, targets, bareForm: false, explicitFamily: family);

        return Incomplete(preview, $"Unexpected token '{targetToken.DisplayText}' after Store.",
            CommandTokenKind.Group, CommandTokenKind.Cue, CommandTokenKind.Preset);
    }

    /// <summary>"STORE GROUP &lt;n&gt; ENTER" - create-only, mirrors GroupsViewModel.Store's own
    /// "an explicit, operator-chosen number that's already taken is a hard conflict" behavior
    /// (StoreGroupCommand.Execute itself fails if the number is in use) - never invented here,
    /// never a silent update.</summary>
    private CommandComposition ResolveStoreGroup(string preview, bool finalize, int i, List<IConsoleCommand> precedingCommands, List<PatchedFixture> targets)
    {
        if (i >= _tokens.Count)
        {
            if (!finalize) return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Number } };
            return Incomplete(preview, "GROUP NUMBER IS MISSING");
        }
        if (_tokens[i].Kind != CommandTokenKind.Number)
            return Incomplete(preview, "Expected a Group number.", CommandTokenKind.Number);

        int number = (int)_tokens[i].NumericValue!.Value;
        if (i + 1 < _tokens.Count)
            return Incomplete(preview, "Nothing may follow the Group number.", CommandTokenKind.Enter);
        if (!finalize)
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Enter } };

        if (targets.Count == 0)
            return Incomplete(preview, "No fixtures resolved to store as a Group.");

        var commands = new List<IConsoleCommand>(precedingCommands) { new StoreGroupCommand($"Group {number}", number) };
        IConsoleCommand operation = commands.Count == 1 ? commands[0] : new CompositeCommand(commands);

        return new CommandComposition
        {
            Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, EndsSelectionCycle = true,
            ResolvedGroupNumber = number,
            ReadyOperation = operation,
        };
    }

    /// <summary>"STORE CUE &lt;n&gt; ENTER" - create-only (mirrors CueListViewModel.StoreWithFilter's
    /// own "already exists - Update it instead" behavior, via StoreCueCommand). Timing/Trigger/
    /// StoreFilter are not yet part of this grammar (Store-grammar slice, §2's explicit scope) -
    /// CueStoreOptions.Default (the same shared default the Core layer itself defines) is used;
    /// inventing per-attribute timing/trigger/filter grammar here was explicitly out of scope.</summary>
    private CommandComposition ResolveStoreCue(string preview, bool finalize, int i, List<IConsoleCommand> precedingCommands, List<PatchedFixture> targets)
    {
        if (i >= _tokens.Count)
        {
            if (!finalize) return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Number } };
            return Incomplete(preview, "CUE NUMBER IS MISSING");
        }
        if (_tokens[i].Kind != CommandTokenKind.Number)
            return Incomplete(preview, "Expected a Cue number.", CommandTokenKind.Number);

        double number = _tokens[i].NumericValue!.Value;
        if (i + 1 < _tokens.Count)
            return Incomplete(preview, "Nothing may follow the Cue number.", CommandTokenKind.Enter);
        if (!finalize)
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Enter } };

        if (_context.PrimaryCueList is not { } cueList)
            return Incomplete(preview, "No Cue List available to store into.");

        var storeCommand = new StoreCueCommand(cueList, $"Cue {FormatNumber(number)}", number, CueStoreOptions.Default);
        var commands = new List<IConsoleCommand>(precedingCommands) { storeCommand };
        IConsoleCommand operation = commands.Count == 1 ? commands[0] : new CompositeCommand(commands);

        return new CommandComposition
        {
            Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, EndsSelectionCycle = true,
            ReadyOperation = operation,
        };
    }

    /// <summary>
    /// "STORE PRESET [&lt;Family&gt;] [&lt;n&gt;] ENTER" / "STORE &lt;Family&gt; [&lt;n&gt;] ENTER"
    /// (Store-grammar slice, §A/§C). Three distinct outcomes on a missing piece, never guessed:
    ///   - neither family nor number given ("STORE PRESET ENTER") - opens the family+number panel
    ///     (PendingStoreChoice with Number=null), regardless of how many families are touched.
    ///   - family given (explicit or the family token itself), number missing - a hard grammar
    ///     error ("PRESET NUMBER IS MISSING"), never a dialog - an explicit family already removes
    ///     the one thing a dialog would otherwise need to ask.
    ///   - number given, family not given - resolved from touched PROGRAMMER families: 0 touched is
    ///     an error, exactly 1 proceeds directly (no dialog - as unambiguous as an explicit family),
    ///     more than 1 opens the family panel with Number already known.
    /// Whatever family list is ultimately resolved (explicit, single-touched, or operator-chosen)
    /// funnels through the SAME BuildPresetStoreResolution conflict-check, so "explicit family
    /// already existing" and "chosen-via-panel family already existing" both get the identical
    /// OVERWRITE/UPDATE/CANCEL treatment - never two divergent conflict paths.
    /// </summary>
    private CommandComposition ResolveStorePreset(string preview, bool finalize, int i, List<IConsoleCommand> precedingCommands,
        List<PatchedFixture> targets, bool bareForm, AttributeClass? explicitFamily)
    {
        AttributeClass? family = explicitFamily;
        if (bareForm && i < _tokens.Count && FamilyFor(_tokens[i].Kind) is { } inlineFamily)
        {
            family = inlineFamily;
            i++;
        }

        int? number = null;
        if (i < _tokens.Count && _tokens[i].Kind == CommandTokenKind.Number)
        {
            number = (int)_tokens[i].NumericValue!.Value;
            i++;
        }

        if (i < _tokens.Count)
            return Incomplete(preview, "Nothing may follow the Preset number.", CommandTokenKind.Enter);

        if (!finalize)
        {
            var expected = new List<CommandTokenKind>();
            if (family is null)
                expected.AddRange(new[] { CommandTokenKind.Intensity, CommandTokenKind.Position, CommandTokenKind.Color, CommandTokenKind.Beam, CommandTokenKind.Image, CommandTokenKind.Shape });
            if (number is null) expected.Add(CommandTokenKind.Number);
            expected.Add(CommandTokenKind.Enter);
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = expected };
        }

        if (targets.Count == 0)
            return Incomplete(preview, "No fixtures resolved to store as a Preset.");

        if (family is null && number is null)
        {
            var touchedForDialog = StorePresetFamilyDetector.TouchedFamilies(targets, _context.Programmer);
            if (touchedForDialog.Count == 0) return Incomplete(preview, "No touched PROGRAMMER families to store.");
            return new CommandComposition
            {
                Tokens = _tokens.ToList(), PreviewText = preview,
                PendingStoreChoice = new PendingStoreChoice(null, touchedForDialog, targets, precedingCommands),
            };
        }

        if (number is null)
            return Incomplete(preview, "PRESET NUMBER IS MISSING");

        IReadOnlyList<AttributeClass> families;
        if (family is { } explicitOne)
        {
            families = new[] { explicitOne };
        }
        else
        {
            var touched = StorePresetFamilyDetector.TouchedFamilies(targets, _context.Programmer);
            if (touched.Count == 0) return Incomplete(preview, "No touched PROGRAMMER families to store.");
            if (touched.Count > 1)
            {
                return new CommandComposition
                {
                    Tokens = _tokens.ToList(), PreviewText = preview,
                    PendingStoreChoice = new PendingStoreChoice(number, touched, targets, precedingCommands),
                };
            }
            families = touched;
        }

        return BuildPresetStoreResolution(preview, number.Value, families, targets, precedingCommands);
    }

    /// <summary>The one place a resolved (family list, number) pair becomes either a ready
    /// operation (no conflicts) or a PendingStoreConflict (one or more families already have a
    /// Preset at this number) - shared with CommandSurfaceViewModel's post-family-choice
    /// confirmation via StorePresetTransactionBuilder, so grammar-resolved and panel-resolved
    /// Preset stores never diverge in how conflicts are found or transactions are built.</summary>
    private CommandComposition BuildPresetStoreResolution(string preview, int number, IReadOnlyList<AttributeClass> families,
        IReadOnlyList<PatchedFixture> targets, IReadOnlyList<IConsoleCommand> precedingCommands)
    {
        var conflicts = StorePresetTransactionBuilder.FindConflicts(_context.Presets, families, number);
        if (conflicts.Count > 0)
        {
            return new CommandComposition
            {
                Tokens = _tokens.ToList(), PreviewText = preview,
                PendingStoreConflict = new PendingStoreConflict(number, families, conflicts, targets, precedingCommands),
            };
        }

        var operation = StorePresetTransactionBuilder.BuildTransaction(_context.Presets, families, number, targets, precedingCommands, overwrite: false);
        return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, EndsSelectionCycle = true, ReadyOperation = operation };
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

    /// <summary>
    /// DMX DIRECT ADDRESSING grammar:
    ///
    ///   DMX &lt;Universe.Address&gt; (THRU &lt;Universe.Address&gt;)? (AT number | FULL | RELEASE)?
    ///
    /// A low-level/diagnostic tool, independent of any Fixture Profile - operates on raw
    /// (Universe, Address) pairs directly against the Programmer (see DmxAddressCommandBase's own
    /// doc comment for why that's a small adaptation of the existing snapshot/undo technique, not
    /// a second merge engine). _tokens[0] is always Dmx when this is called.
    /// </summary>
    private CommandComposition ResolveDmx(bool finalize, string preview)
    {
        if (_tokens.Count == 1)
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.DmxAddress, CommandTokenKind.Number } };

        // Quick Patch form B (Quick Patch stabilization slice): "DMX <addr> [THRU <addr2>] FIXTURE
        // <n> [ENTER]" - a plain Number after DMX (not the dotted Universe.Address DmxAddress
        // token DMX DIRECT ADDRESSING always uses) signals the reverse-form Quick Patch syntax,
        // resolving through the exact same PatchFixturesCommand as form A - never a second patch
        // engine, never duplicated numbering/address logic.
        if (_tokens[1].Kind == CommandTokenKind.Number)
            return ResolveQuickPatchFromDmx(finalize, preview);

        if (_tokens[1].Kind != CommandTokenKind.DmxAddress)
            return Incomplete(preview, "Expected a Universe.Address after DMX.", CommandTokenKind.DmxAddress);

        var (fromUniverse, fromAddress) = ((int Universe, int Address))_tokens[1].SemanticPayload!;
        if (!ValidateDmxAddress(fromUniverse, fromAddress, out var fromError))
            return Incomplete(preview, fromError);

        // fromUniverse/toUniverse stay OPERATOR-facing (1-based, DMX DIRECT ADDRESSING follow-up
        // §1) everywhere in this method's own logic/error messages - translated to the engine's
        // internal 0-based universeId only once, right here, at the single point addresses are
        // actually built for the command. Never two representations drifting independently.
        var addresses = new List<(int Universe, int Channel)> { (ToInternalUniverseId(fromUniverse), fromAddress - 1) }; // 1-512 -> 0-based channelIndex
        int i = 2;

        if (i >= _tokens.Count)
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Thru, CommandTokenKind.At, CommandTokenKind.Full, CommandTokenKind.Release } };

        if (_tokens[i].Kind == CommandTokenKind.Thru)
        {
            i++;
            if (i >= _tokens.Count) return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.DmxAddress } };
            if (_tokens[i].Kind != CommandTokenKind.DmxAddress)
                return Incomplete(preview, "Expected a Universe.Address after Thru.", CommandTokenKind.DmxAddress);

            var (toUniverse, toAddress) = ((int Universe, int Address))_tokens[i].SemanticPayload!;
            if (!ValidateDmxAddress(toUniverse, toAddress, out var toError))
                return Incomplete(preview, toError);

            // Same-universe range only for v1 (§2) - an honest, structured rejection rather than
            // silently crossing a Universe boundary or guessing operator intent.
            if (toUniverse != fromUniverse)
                return Incomplete(preview, $"DMX range cannot cross Universe boundary ({fromUniverse} -> {toUniverse}) - not supported in this slice.");

            addresses.Clear();
            int lo = Math.Min(fromAddress, toAddress), hi = Math.Max(fromAddress, toAddress); // reverse ranges normalize, same as FIXTURE/GROUP THRU
            int internalUniverse = ToInternalUniverseId(fromUniverse);
            for (int a = lo; a <= hi; a++) addresses.Add((internalUniverse, a - 1));
            i++;
        }

        if (i >= _tokens.Count)
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.At, CommandTokenKind.Full, CommandTokenKind.Release } };

        bool selfTerminates;
        IConsoleCommand operation;

        if (_tokens[i].Kind == CommandTokenKind.Release)
        {
            i++;
            if (i < _tokens.Count) return Incomplete(preview, "Nothing may follow Release.", CommandTokenKind.Enter);
            selfTerminates = true;
            operation = new ReleaseDmxAddressCommand(addresses);
        }
        else if (_tokens[i].Kind == CommandTokenKind.Full)
        {
            i++;
            if (i < _tokens.Count) return Incomplete(preview, "Nothing may follow Full.", CommandTokenKind.Enter);
            selfTerminates = true;
            operation = new SetDmxAddressCommand(addresses, PercentToByte(100));
        }
        else if (_tokens[i].Kind == CommandTokenKind.At)
        {
            i++;
            if (i >= _tokens.Count) return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Number } };
            if (_tokens[i].Kind != CommandTokenKind.Number)
                return Incomplete(preview, "Expected a number after At.", CommandTokenKind.Number);

            double percent = _tokens[i].NumericValue!.Value;
            i++;
            if (i < _tokens.Count) return Incomplete(preview, "Nothing may follow the At value.", CommandTokenKind.Enter);

            selfTerminates = false; // ends in a numeric token - needs ENTER per §14, same as Fixture/Group AT
            operation = new SetDmxAddressCommand(addresses, PercentToByte(percent));
        }
        else
        {
            return Incomplete(preview, $"Unexpected token '{_tokens[i].DisplayText}'.");
        }

        if (!finalize && !selfTerminates)
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Enter } };

        return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true, ReadyOperation = operation };
    }

    /// <summary>
    /// Quick Patch form B: "DMX &lt;addr&gt; [THRU &lt;addr2&gt;] FIXTURE &lt;n&gt; [ENTER]" -
    /// _tokens[0]=Dmx, _tokens[1]=Number(startAddress) when this is called (see ResolveDmx's own
    /// branch). The address range's span, divided by the active patch mode's footprint, determines
    /// how many fixtures to create - numbered consecutively starting at the given Fixture number,
    /// mirroring form A's "advance by fixture footprint" rule in reverse.
    /// </summary>
    private CommandComposition ResolveQuickPatchFromDmx(bool finalize, string preview)
    {
        double startAddress = _tokens[1].NumericValue!.Value;
        double endAddress = startAddress;
        int i = 2;

        if (i < _tokens.Count && _tokens[i].Kind == CommandTokenKind.Thru)
        {
            i++;
            if (i >= _tokens.Count || _tokens[i].Kind != CommandTokenKind.Number)
                return Incomplete(preview, "Expected a number after Thru.", CommandTokenKind.Number);
            endAddress = _tokens[i].NumericValue!.Value;
            i++;
        }

        if (i >= _tokens.Count)
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Fixture } };

        if (_tokens[i].Kind != CommandTokenKind.Fixture)
            return Incomplete(preview, "Expected FIXTURE after the DMX address range.", CommandTokenKind.Fixture);
        i++;

        if (i >= _tokens.Count)
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Number } };
        if (_tokens[i].Kind != CommandTokenKind.Number)
            return Incomplete(preview, "Expected a starting Fixture number.", CommandTokenKind.Number);

        int startNumber = (int)_tokens[i].NumericValue!.Value;
        i++;

        if (i < _tokens.Count)
            return Incomplete(preview, "Nothing may follow the starting Fixture number.", CommandTokenKind.Enter);

        if (!finalize)
            return new CommandComposition { Tokens = _tokens.ToList(), PreviewText = preview, ExpectedNext = new[] { CommandTokenKind.Enter } };

        if (_context.DefaultPatchProfile is null || _context.DefaultPatchMode is null)
            return Incomplete(preview, "Select a fixture profile/mode on the Patch screen first.");

        int footprint = Math.Max(1, _context.DefaultPatchMode.FootprintSize);
        int span = (int)Math.Abs(endAddress - startAddress) + 1;
        int count = Math.Max(1, span / footprint);
        var numbers = Enumerable.Range(startNumber, count).ToList();

        return BuildQuickPatchComposition(preview, numbers, (int)Math.Min(startAddress, endAddress));
    }

    /// <summary>Turns a parsed Fixture clause list (Anchor/Plus/Minus/Thru - the SAME clause shape
    /// Resolve() already uses for SELECTING existing fixtures) into an ordered, deduplicated list
    /// of fixture Numbers, for Quick Patch form A - deliberately independent of Patch.Fixtures
    /// lookups (TryResolveSingle), since these fixtures don't exist yet; "already patched" is a
    /// PatchFixturesCommand validation failure, not something to silently skip over here.</summary>
    private static List<int> ClausesToNumbers(List<Clause> clauses)
    {
        var numbers = new List<int>();
        foreach (var clause in clauses)
        {
            switch (clause.Op)
            {
                case ClauseOp.Anchor:
                case ClauseOp.Plus:
                    int n = (int)clause.Number;
                    if (!numbers.Contains(n)) numbers.Add(n);
                    break;

                case ClauseOp.Minus:
                    numbers.Remove((int)clause.Number);
                    break;

                case ClauseOp.Thru:
                    int index = clauses.IndexOf(clause);
                    double from = clauses[index - 1].Number;
                    double to = clause.Number;
                    int lo = (int)Math.Min(from, to), hi = (int)Math.Max(from, to);
                    for (int i = lo; i <= hi; i++)
                        if (!numbers.Contains(i)) numbers.Add(i);
                    break;
            }
        }
        return numbers;
    }

    /// <summary>The ONE place both Quick Patch forms build the shared PatchFixturesCommand -
    /// reads DefaultPatchProfile/DefaultPatchMode/DefaultPatchUniverseId off ConsoleContext (kept
    /// in sync with the PATCH screen's own selection by MainViewModel), so the Command Surface
    /// never has its own, second notion of "which profile is active". Addresses advance by the
    /// mode's own footprint per fixture, same rule AddFixture/PatchFixturesCommand already use.</summary>
    private CommandComposition BuildQuickPatchComposition(string preview, IReadOnlyList<int> fixtureNumbers, int startAddress)
    {
        if (_context.DefaultPatchProfile is not { } profile || _context.DefaultPatchMode is not { } mode)
            return Incomplete(preview, "Select a fixture profile/mode on the Patch screen first.");

        if (fixtureNumbers.Count == 0)
            return Incomplete(preview, "No fixture numbers resolved to patch.");

        int footprint = Math.Max(1, mode.FootprintSize);
        int universeId = _context.DefaultPatchUniverseId;

        var requests = new List<PatchFixturesCommand.Request>(fixtureNumbers.Count);
        for (int j = 0; j < fixtureNumbers.Count; j++)
        {
            int address = startAddress + j * footprint;
            string name = fixtureNumbers.Count == 1 ? profile.DisplayName : $"{profile.DisplayName} {j + 1}";
            requests.Add(new PatchFixturesCommand.Request(fixtureNumbers[j], universeId, address, name));
        }

        return new CommandComposition
        {
            Tokens = _tokens.ToList(), PreviewText = preview, IsComplete = true,
            ReadyAction = new PatchFixturesCommand(profile, mode, requests),
        };
    }

    /// <summary>§4/DMX DIRECT ADDRESSING follow-up §1: validate Universe existence/range and DMX
    /// address range 1-512 - never silently wrap, never silently clamp. Universe numbering is
    /// authoritatively 1-based on the operator-facing side (DMX 1.1 is the FIRST universe, first
    /// address - symmetric with the address's own 1-based DMX512 convention), even though the
    /// engine's internal universeId stays 0-based (ToInternalUniverseId translates explicitly,
    /// once, never left implicit). Universe has no fixed upper bound in this engine
    /// (DmxOutputEngine.EnsureUniverse creates one on demand for any non-negative internal id -
    /// see that type's own doc comment), so "existence" here means "operator Universe is at least
    /// 1"; address range 1-512 is DMX512's own hard protocol limit (Universe.ChannelCount).</summary>
    private static bool ValidateDmxAddress(int universe, int address, out string? error)
    {
        if (universe < 1) { error = $"Invalid Universe {universe} - must be 1 or greater."; return false; }
        if (address < 1 || address > 512) { error = $"Invalid DMX address {address} - must be 1-512."; return false; }
        error = null;
        return true;
    }

    /// <summary>The one, explicit translation point from the operator-facing 1-based Universe
    /// number to the engine's internal 0-based universeId (DMX DIRECT ADDRESSING follow-up §1) -
    /// every other Universe-numbering site in this class calls this rather than repeating "- 1"
    /// inline, so the offset lives in exactly one place.</summary>
    private static int ToInternalUniverseId(int operatorUniverse) => operatorUniverse - 1;

    /// <summary>§3: AT values are semantic operator percentages, never raw byte literals typed by
    /// the operator - converted to a DMX byte only here, at the Application/DMX boundary, using
    /// the exact same clamp-then-round formula AdjustIntensityCommand already uses.</summary>
    private static byte PercentToByte(double percent) =>
        (byte)Math.Round(Math.Clamp(percent, 0.0, 100.0) / 100.0 * 255.0);
}

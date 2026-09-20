using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Application.Commands.Playback;
using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Application.Macros;
using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using DmxConsole.Web.EditorToolBar;

namespace DmxConsole.Web.Services;

/// <summary>
/// Bridges a CommandSurface (touch keypad, physical keyboard, future MIDI/HID) to the
/// UI-independent CommandComposer. Touch, keyboard and GUI selection all converge on the same
/// ConsoleContext selection and SelectionCycleState.
/// </summary>
public sealed class CommandSurfaceViewModel
{
    private readonly ConsoleContext _context;
    private readonly CommandDispatcher _dispatcher;
    private readonly EditorContextStack _editorContext;
    private readonly MacroRecorder _macroRecorder;
    private readonly MacroPlaybackService _macroPlayer;
    private CommandComposer _composer;
    private string _pendingDigits = string.Empty;
    private bool _mirrorsExistingSelection;

    /// <summary>Set the instant LEARN MACRO is pressed with no recording in progress - the next
    /// MACRO N press chooses which slot to record into and starts recording (docs/COMMAND_SURFACE_KEY_SPEC.md
    /// MACROS §3). A second LEARN MACRO press while armed (no slot chosen yet) cancels the arm -
    /// the same "armed for exactly the next relevant key" idiom as ShiftArmed.</summary>
    private bool _learnArmed;

    /// <summary>Set when a LEARN MACRO + MACRO N press targets a slot that already holds a
    /// non-empty Macro - re-arms LEARN so pressing the SAME slot again explicitly confirms
    /// overwrite (§11: no silent overwrite), the same two-press-to-confirm idiom this Command
    /// Surface already uses for RELEASE ENTER.</summary>
    private int? _pendingOverwriteSlot;

    /// <summary>RELEASE is a two-step, contextual gesture (RELEASE-panel slice): the first bare
    /// RELEASE press (empty command line) never mutates anything - it only arms the family-choice
    /// softkey context (<see cref="ArmedReleaseFamilies"/>), exposed by CommandSurface.razor as
    /// ALL/INTENSITY/POSITION/COLOR/BEAM/IMAGE/SHAPE. A second RELEASE press, or ENTER, confirms
    /// whatever families are armed (empty = every family). Any other composing key (a digit, a
    /// token, Backspace, Shift, a Macro key) disarms it - the same "any other key cancels a
    /// two-press gesture" idiom already used for Macro-overwrite confirmation. CLEAR is the one
    /// key documented to dismiss it explicitly (see PressClear) - not merely "any other key", but a
    /// requirement of its own (§15/§D).</summary>
    public bool ReleaseArmed { get; private set; }

    private readonly HashSet<AttributeClass> _armedReleaseFamilies = new();

    /// <summary>The family/families chosen so far while <see cref="ReleaseArmed"/> is true - empty
    /// means "no family chosen yet", which confirms differently depending on HOW it's confirmed
    /// (see ConfirmArmedRelease's own doc comment). Exposed read-only; CommandSurface.razor's
    /// softkeys mutate it only through <see cref="ToggleReleaseFamily"/>.</summary>
    public IReadOnlySet<AttributeClass> ArmedReleaseFamilies => _armedReleaseFamilies;

    private void DisarmReleaseContext()
    {
        ReleaseArmed = false;
        _armedReleaseFamilies.Clear();
    }

    public CommandComposition Current { get; private set; }
    public string? DispatchError { get; private set; }

    /// <summary>SHIFT (docs/COMMAND_SURFACE_KEY_SPEC.md §18) - a toggle-arm modifier rather than a
    /// held key (there is no physical "hold" gesture on a touch keypad): press SHIFT once to arm
    /// it for exactly the next key; that next key consumes it (whether or not it has a defined
    /// Shift meaning) and it clears. Only SHIFT+RELEASE has a defined v1 meaning
    /// (Release All Playbacks, §7) - every other combination is deliberately left unassigned
    /// (§18: "do not invent meanings"), so pressing e.g. Shift then a digit just silently clears
    /// Shift and behaves like the digit alone, never an error.</summary>
    public bool ShiftArmed { get; private set; }

    public void PressShift()
    {
        ShiftArmed = !ShiftArmed;
        Changed?.Invoke();
    }

    /// <summary>Whether the keypad is expanded - fixed console infrastructure (always present),
    /// same as EncoderDrawerViewModel.IsOpen, just collapsible to reclaim screen space rather
    /// than conjured/removed by context.</summary>
    public bool IsOpen { get; private set; } = true;

    public event Action? Changed;

    public void Open() { IsOpen = true; Changed?.Invoke(); }
    public void Close() { IsOpen = false; Changed?.Invoke(); }
    public void Toggle() { IsOpen = !IsOpen; Changed?.Invoke(); }

    public CommandSurfaceViewModel(ConsoleContext context, CommandDispatcher dispatcher, EditorContextStack editorContext)
    {
        _context = context;
        _dispatcher = dispatcher;
        _editorContext = editorContext;
        _composer = new CommandComposer(context);
        _macroRecorder = new MacroRecorder(dispatcher, context.Macros);
        _macroPlayer = new MacroPlaybackService(dispatcher, context.Macros, _macroRecorder);
        Current = _composer.Current;
    }

    /// <summary>True the instant LEARN MACRO is armed, waiting for a MACRO key to pick the slot -
    /// distinct from <see cref="IsRecordingMacro"/> (actively recording into a chosen slot).</summary>
    public bool IsLearnArmed => _learnArmed;

    /// <summary>True while a Macro slot is actively recording (docs/COMMAND_SURFACE_KEY_SPEC.md
    /// MACROS §13 - "no hidden recording state").</summary>
    public bool IsRecordingMacro => _macroRecorder.IsRecording;

    public int? RecordingMacroSlot => _macroRecorder.RecordingSlot;

    /// <summary>Selection-expression completion != programming-execution completion (§A). A pure
    /// selection ("FIXTURE 1 THRU 20 ENTER", no AT) resets the CommandComposer's own working
    /// tokens immediately - that reset is structural, required so the NEXT gesture dispatches as
    /// its own small, separate command rather than re-replaying every prior clause in one
    /// ever-growing composite (see Push's own doc comment) - but the operator-visible Task line
    /// must NOT look idle just because of that internal reset. This holds the just-resolved
    /// selection's preview text so DisplayPreview keeps showing it until either a real programming
    /// execution happens (cleared there - see Push/PressCaptureAll/ConfirmArmedRelease) or the
    /// operator begins composing something new (superseded there, never explicitly cleared, since
    /// DisplayPreview always prefers live composer/pending-digit state when either is non-empty).
    /// Also cleared by PressClear.</summary>
    private string? _selectionContextEcho;

    public string DisplayPreview
    {
        get
        {
            if (_pendingDigits.Length > 0) return $"{Current.PreviewText} {_pendingDigits}".TrimStart();
            return Current.PreviewText.Length > 0 ? Current.PreviewText : (_selectionContextEcho ?? string.Empty);
        }
    }

    /// <summary>
    /// Mirrors a fixture selection made outside the keypad into the same CommandComposer. The
    /// GUI has already mutated Selection, so resolving this mirrored line must be idempotent.
    /// </summary>
    public void SynchronizeFixtureSelection(IEnumerable<PatchedFixture> fixtures)
    {
        _pendingDigits = string.Empty;
        DispatchError = null;
        DisarmReleaseContext();
        ShiftArmed = false;
        DisarmLearn();
        _composer.Reset();

        var ordered = fixtures.Distinct().OrderBy(f => f.Number).ToList();
        _composer.ReplaceSelectionOnResolve = false;
        _mirrorsExistingSelection = ordered.Count > 0;

        if (ordered.Count > 0)
        {
            _composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
            foreach (var fixture in ordered)
                _composer.Push(CommandToken.Number(fixture.Number));
            _context.SelectionCycle.MarkSelectionSynchronized();
        }

        Current = _composer.Current;
        Changed?.Invoke();
    }

    public void PressDigit(char digit)
    {
        if (digit is < '0' or > '9') return;
        DisarmReleaseContext();
        ShiftArmed = false;
        DisarmLearn();
        _selectionContextEcho = null; // a new composing key permanently supersedes the echo (§A)

        // A number added to a mirrored selection is a new selection gesture unless it is the
        // value following AT. This matters for the recorded selection-gesture history.
        if (_mirrorsExistingSelection && !Current.Tokens.Any(t => t.Kind == CommandTokenKind.At))
            _mirrorsExistingSelection = false;

        _pendingDigits += digit;
        DispatchError = null;
        Changed?.Invoke();
    }

    public void PressDecimalPoint()
    {
        DisarmReleaseContext();
        ShiftArmed = false;
        DisarmLearn();
        _selectionContextEcho = null; // a new composing key permanently supersedes the echo (§A)

        // DMX Universe.Address (§8's dot ambiguity): while the composer is expecting a
        // DmxAddress next, "." is a literal Universe/Address separator - never Recall, never an
        // ordinary decimal point. Entirely context-driven via Current.ExpectedNext (computed by
        // CommandComposer), never a string hack here or in Razor. Checked FIRST, before Recall/
        // decimal-point logic, so it can never be shadowed by either.
        if (Current.ExpectedNext.Contains(CommandTokenKind.DmxAddress))
        {
            if (_pendingDigits.Length > 0 && !_pendingDigits.Contains('.'))
            {
                _pendingDigits += ".";
                Changed?.Invoke();
            }
            return;
        }

        if (_pendingDigits.Length == 0 &&
            (Current.Tokens.Count == 0 || Current.Tokens[^1].Kind is CommandTokenKind.Fixture or CommandTokenKind.Group or CommandTokenKind.At))
        {
            Push(CommandToken.Simple(CommandTokenKind.Recall));
            return;
        }
        if (_pendingDigits.Contains('.')) return;
        _pendingDigits += _pendingDigits.Length == 0 ? "0." : ".";
        Changed?.Invoke();
    }

    public void PressToken(CommandTokenKind kind)
    {
        // Backspace and Clear each have exactly ONE behavior, implemented by their own dedicated
        // key handler (PressBackspace / PressClear). Delegating here - before any token-composition
        // logic runs - guarantees that a generic/programmatic caller (a physical keyboard binding,
        // a future MIDI/HID surface, a soft key, a test) can never reach a second, divergent
        // meaning for either key.
        if (kind == CommandTokenKind.Backspace) { PressBackspace(); return; }
        if (kind == CommandTokenKind.Clear) { PressClear(); return; }

        // RELEASE-armed ENTER (§C): ENTER confirms whatever family/families are armed on the
        // RELEASE softkey panel (empty = "release ALL Programmer values for the current
        // Selection") - the second of the two documented confirmation gestures (the other being a
        // second bare RELEASE press - see PressRelease/ConfirmArmedRelease). This is checked before
        // the generic disarm below because ENTER here is itself the confirmation, not a cancel.
        if (kind == CommandTokenKind.Enter && ReleaseArmed)
        {
            ConfirmArmedRelease(viaSecondRelease: false);
            return;
        }
        DisarmReleaseContext();
        ShiftArmed = false;
        DisarmLearn();
        // Any composing key permanently supersedes a pure-selection Task-line echo (§A) - not
        // merely "hidden while Current.PreviewText is non-empty": otherwise Backspace-ing back
        // down to an empty composition would let a stale echo from an earlier, already-superseded
        // gesture leak back through DisplayPreview.
        _selectionContextEcho = null;

        CommitPendingDigits();

        // A lone family token acts as a pure context selector (armed, waiting for HOME/RELEASE/
        // PRESET/a Parameter Picker choice) - pressing a DIFFERENT family key while in exactly
        // that state replaces it instead of appending, so the operator never needs an explicit
        // CLEAR just to switch which family is armed (PARAMETER PICKER follow-up). Scoped
        // narrowly: this only fires when the family token is standing entirely ALONE - a family
        // token that is already part of a real, further-built command (e.g. mid Preset-recall
        // composition) is left completely alone, preserving existing grammar/Backspace-to-correct
        // behavior untouched.
        if (CommandComposer.FamilyFor(kind) is not null
            && Current.Tokens.Count == 1
            && CommandComposer.FamilyFor(Current.Tokens[0].Kind) is not null)
        {
            _composer.Reset();
        }

        var objectType = kind switch
        {
            CommandTokenKind.Fixture => EditorObjectType.Fixture,
            CommandTokenKind.Group => EditorObjectType.Group,
            CommandTokenKind.Cue => EditorObjectType.Cue,
            _ => (EditorObjectType?)null,
        };
        if (objectType is { } type) _editorContext.EnterObject(type, kind.ToString().ToUpperInvariant());

        if (kind is CommandTokenKind.Fixture or CommandTokenKind.Group)
        {
            _mirrorsExistingSelection = false;
            _composer.ReplaceSelectionOnResolve =
                _context.SelectionCycle.StartFreshOnNextSelection || _context.Selection.Items.Count == 0;
        }
        else if (kind is CommandTokenKind.Plus or CommandTokenKind.Minus or CommandTokenKind.Thru)
        {
            _mirrorsExistingSelection = false;
        }

        Push(CommandToken.Simple(kind));
    }

    /// <summary>
    /// CAPTURE ALL (docs/COMMAND_SURFACE_KEY_SPEC.md §8) - always instant/self-terminating
    /// regardless of whatever partial command is in progress (there is no composed variant,
    /// unlike RELEASE) - cancels any in-progress composition first, then dispatches directly.
    /// Deliberately does NOT go through the generic Push() completion path used for
    /// selection-building commands: Push() also records a SelectionCycleState gesture and
    /// remembers "last selection" on every successful dispatch, which is correct for commands
    /// that build/mutate Selection but would be a spurious, meaningless gesture entry for CAPTURE
    /// ALL, which never touches Selection at all (§5/§9 of the requesting spec are explicit that
    /// CAPTURE ALL must not alter selection - bypassing Push() here is what keeps that literally
    /// true rather than merely "the value didn't change but a gesture was recorded anyway").
    /// </summary>
    public void PressCaptureAll()
    {
        DisarmReleaseContext();
        ShiftArmed = false;
        DisarmLearn();
        _pendingDigits = string.Empty;
        _composer.Reset();

        var composition = _composer.Push(CommandToken.Simple(CommandTokenKind.CaptureAll));
        DispatchError = null;

        if (composition.IsComplete && composition.ReadyOperation is not null)
        {
            var result = _dispatcher.Dispatch(composition.ReadyOperation);
            DispatchError = result.Success ? null : (result.Error ?? "Capture All failed.");
            // A successful Capture commit is a programming action (Selection Cycle stabilization
            // slice, item 8) - it must close the cycle so the NEXT Fixture/Group selection starts
            // fresh, even though (deliberately, per this method's own doc comment) it never
            // touches Selection itself and therefore never records a selection gesture. Called
            // directly rather than through Push()'s composition.EndsSelectionCycle path, since
            // this bypasses Push(). This rule applies to "a successful Capture commit" generically,
            // not specifically to whatever this method happens to be named today - PressCaptureAll
            // is currently the only Capture entry point the Command Surface exposes; a future
            // slice may add bare CAPTURE / CAPTURE FIXTURE.../SHIFT+CAPTURE=CAPTURE ALL grammar
            // without changing this rule.
            if (result.Success) _context.SelectionCycle.MarkExecutionCompleted();
            _composer.Reset();
            _selectionContextEcho = null; // a programming execution returns the Task line to true idle (§A)
            Current = _composer.Current; // idle line - "reverts to idle after successful execution"
        }
        else
        {
            DispatchError = composition.Error;
            Current = composition;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// PARAMETER RELEASE's addressed-channel key (docs/COMMAND_SURFACE_KEY_SPEC.md §7, e.g. "PAN")
    /// - the Command Surface's entry point for a Parameter token, mirroring PressToken but for a
    /// token that carries a ChannelType payload rather than being a bare CommandTokenKind. Like
    /// pressing any other composing key, this disarms both two-press gestures (RELEASE ENTER,
    /// Shift) - only bare RELEASE itself ever arms the escalation, never a Parameter/Family key.
    ///
    /// Always starts a fresh, self-contained "&lt;Parameter&gt; RELEASE" composition, discarding
    /// whatever was in progress first (same "instant, self-contained trigger" idiom as
    /// PressCaptureAll/PressMacroSlot) - this is what makes the PARAMETER PICKER's documented
    /// workflow literal: "POSITION, PAN, RELEASE" resolves to exactly "PAN RELEASE", never
    /// "POSITION PAN RELEASE" (which the composer's grammar doesn't and shouldn't recognize -
    /// there is no family+parameter combined rule, only the parameter's own).
    /// </summary>
    public void PressParameter(ChannelType channelType)
    {
        DisarmReleaseContext();
        ShiftArmed = false;
        DisarmLearn();
        _pendingDigits = string.Empty;
        _mirrorsExistingSelection = false;
        _composer.Reset();

        Push(CommandToken.Parameter(channelType));
    }

    /// <summary>
    /// RELEASE (docs/COMMAND_SURFACE_KEY_SPEC.md §7, RELEASE-panel slice). Mid-composition (a
    /// family token already pushed, e.g. "POSITION") this remains ordinary composer grammar -
    /// "&lt;Family&gt; RELEASE" self-terminates immediately via PressToken, unchanged. On an EMPTY
    /// command line, RELEASE is a two-step CONTEXTUAL gesture, never an immediate mutation: the
    /// first press only arms the family-choice softkey context (<see cref="ReleaseArmed"/>,
    /// exposed as ALL/INTENSITY/POSITION/COLOR/BEAM/IMAGE/SHAPE) - a second RELEASE press (or
    /// ENTER, see PressToken) confirms it. The legacy "RELEASE fires immediately, ENTER escalates
    /// to Clear Entire Editor" two-press gesture is retired - it conflicted with this spec and is
    /// not preserved.
    /// </summary>
    public void PressRelease()
    {
        DisarmLearn();

        if (ShiftArmed)
        {
            // SHIFT+RELEASE = Release All Playbacks (§7) - a runtime/non-undoable Action on
            // Executors, never the Programmer. Entirely independent of the family-arm state
            // machine below; disarms it as a side effect of consuming Shift, same as any other key.
            ShiftArmed = false;
            DisarmReleaseContext();
            var shiftResult = _dispatcher.DispatchAction(new ReleaseAllPlaybacksAction());
            DispatchError = shiftResult.Success ? null : (shiftResult.Error ?? "Release All Playbacks failed.");
            Changed?.Invoke();
            return;
        }

        if (Current.Tokens.Count > 0 || _pendingDigits.Length > 0)
        {
            DisarmReleaseContext();
            ShiftArmed = false;
            PressToken(CommandTokenKind.Release);
            return;
        }

        DispatchError = null;

        if (!ReleaseArmed)
        {
            // First RELEASE on an empty line: arm the contextual panel only. No Programmer
            // mutation, no Selection mutation, no SelectionCycle change - purely a UI-context
            // change, exactly like opening the Encoder Drawer's category rail.
            ReleaseArmed = true;
            _armedReleaseFamilies.Clear();
            Changed?.Invoke();
            return;
        }

        // Second bare RELEASE press while already armed confirms it (the other confirmation is
        // ENTER, handled in PressToken).
        ConfirmArmedRelease(viaSecondRelease: true);
    }

    /// <summary>Toggles one family in/out of the RELEASE softkey panel's current selection - only
    /// meaningful while <see cref="ReleaseArmed"/> is true; a no-op otherwise (the panel isn't
    /// shown, so nothing should be reachable to call this). Multiple families may be armed at
    /// once (§C: "User may choose one or multiple families").</summary>
    public void ToggleReleaseFamily(AttributeClass family)
    {
        if (!ReleaseArmed) return;
        if (!_armedReleaseFamilies.Remove(family)) _armedReleaseFamilies.Add(family);
        Changed?.Invoke();
    }

    /// <summary>The RELEASE softkey panel's "ALL" option (§C/§D) - explicitly resets the armed
    /// family set back to empty (still requires ENTER/a second RELEASE to confirm, same as any
    /// other choice on this panel). "ALL" and specific families are mutually exclusive by
    /// construction: choosing ALL clears whatever families were armed, and choosing any family
    /// leaves the set non-empty, which is what makes it NOT "ALL" at confirm time.</summary>
    public void SelectReleaseAll()
    {
        if (!ReleaseArmed) return;
        _armedReleaseFamilies.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Confirms whatever the RELEASE softkey panel currently has armed and disarms it. With one or
    /// more families chosen, both confirmation gestures (ENTER and a second RELEASE) behave
    /// IDENTICALLY: release those families for the CURRENT SELECTION only (§C "Family release").
    /// With NO family chosen, the two gestures diverge - this is the one place the confirmation
    /// method itself changes the outcome:
    ///   - ENTER (viaSecondRelease: false) = release ALL Programmer values for the current
    ///     Selection (scoped, like a bare RELEASE always used to be).
    ///   - a second RELEASE (viaSecondRelease: true) = Clear Entire Programmer globally, for
    ///     every patched fixture, independent of the current Selection.
    /// A successful release is a programming action - closes the SelectionCycle, exactly like
    /// family-qualified RELEASE grammar already does.
    /// </summary>
    private void ConfirmArmedRelease(bool viaSecondRelease)
    {
        ReleaseArmed = false;
        var families = _armedReleaseFamilies.ToList();
        _armedReleaseFamilies.Clear();
        DispatchError = null;

        CommandResult result;
        if (families.Count == 0 && viaSecondRelease)
        {
            // RELEASE RELEASE, no family: Clear Entire Programmer globally - independent of
            // Selection, so no "select at least one fixture" guard applies here.
            result = _dispatcher.Dispatch(new ReleaseCommand(_context.Patch.Fixtures.ToList(), null));
        }
        else
        {
            var targets = _context.Selection.Items.ToList();
            if (targets.Count == 0)
            {
                DispatchError = "Select at least one fixture first.";
                Changed?.Invoke();
                return;
            }

            if (families.Count == 0)
            {
                // RELEASE ENTER, no family: release ALL Programmer values for the current Selection.
                result = _dispatcher.Dispatch(new ReleaseCommand(targets, null));
            }
            else
            {
                var commands = families.Select(f => (IConsoleCommand)new ReleaseCommand(targets, f)).ToList();
                result = commands.Count == 1 ? _dispatcher.Dispatch(commands[0]) : _dispatcher.DispatchBatch(commands);
            }
        }

        DispatchError = result.Success ? null : (result.Error ?? "Release failed.");
        if (result.Success)
        {
            _context.SelectionCycle.MarkExecutionCompleted();
            _selectionContextEcho = null; // a programming execution returns the Task line to true idle (§A)
        }
        Changed?.Invoke();
    }

    public void PressBackspace()
    {
        DisarmReleaseContext();
        ShiftArmed = false;
        DisarmLearn();
        _mirrorsExistingSelection = false;

        if (_pendingDigits.Length > 0)
        {
            _pendingDigits = _pendingDigits[..^1];
            Changed?.Invoke();
            return;
        }

        Push(CommandToken.Simple(CommandTokenKind.Backspace));
    }

    /// <summary>
    /// CLEAR (docs/COMMAND_SURFACE_KEY_SPEC.md §15, redefined again for the RELEASE-panel slice).
    /// There is exactly ONE operator CLEAR semantic, converged onto from every UI entry point
    /// (SelectionBar/ChannelsView/FixturesView Clear buttons all call this same method - see their
    /// own Razor code). One press:
    ///   1. clears the current Fixture/Group Selection;
    ///   2. clears/resets the CommandComposer AND the pending-digit buffer - the Task line
    ///      returns to idle (a correction from the earlier "CLEAR is selection-only, Backspace is
    ///      the only command-line editor" design: CLEAR now resets BOTH);
    ///   3. resets SelectionCycle to a clean idle state (ClearSelectionAction itself does this -
    ///      see that type's own doc comment - so every other caller of that Action gets it too,
    ///      not just this one);
    ///   4. dismisses the RELEASE softkey context immediately if armed (see ReleaseArmed) - CLEAR
    ///      is the one key documented to do this explicitly, unlike Shift/LEARN MACRO below;
    ///   5. leaves Programmer completely untouched;
    ///   6. is not Undo - applied as a non-undoable <see cref="ClearSelectionAction"/> via
    ///      CommandDispatcher.DispatchAction, so it can never enter Undo history and can never
    ///      clear the Redo stack (also excluded from Macro recording - see MacroRecorder);
    ///   7. is not Backspace - Backspace remains the ONLY editor of partially-typed digits/tokens
    ///      that have not yet resolved into a completed command; CLEAR always acts on the whole
    ///      Selection/Task line at once, never one token/gesture at a time;
    ///   8. never triggers RELEASE behavior - beyond dismissing an armed RELEASE context, CLEAR
    ///      never dispatches a Release itself.
    ///
    /// Otherwise selection-only in spirit: a live Shift arm or LEARN MACRO arm/recording is left
    /// exactly as it was - those belong to their own keys, and CLEAR silently disarming them would
    /// make an unrelated key's next press behave differently for no reason the operator could see.
    /// There is no CLEAR CLEAR escalation of any kind - repeated presses just repeat the same
    /// immediate reset.
    /// </summary>
    public void PressClear()
    {
        DisarmReleaseContext();
        _pendingDigits = string.Empty;
        _composer.Reset();
        _selectionContextEcho = null;

        var result = _dispatcher.DispatchAction(new ClearSelectionAction());

        DispatchError = result.Success ? null : (result.Error ?? "Clear failed.");
        Current = _composer.Current;
        Changed?.Invoke();
    }

    /// <summary>
    /// LEARN MACRO (docs/COMMAND_SURFACE_KEY_SPEC.md MACROS §1/§3) - a toggle: with no recording
    /// in progress, arms/disarms "waiting for a MACRO key to pick the slot" (the next MACRO N
    /// press starts recording into that slot - see <see cref="PressMacroSlot"/>); while actively
    /// recording, stops and saves. Always instant/self-terminating, always resets any in-progress
    /// command-line composition first, exactly like CAPTURE ALL - it is a fixed physical key, not
    /// part of the composed grammar.
    /// </summary>
    public void PressLearnMacro()
    {
        DisarmReleaseContext();
        // SHIFT + LEARN MACRO is reserved for a future Macro Manager/Edit screen (§1) - not
        // implemented in v1, so it silently behaves like a bare LEARN MACRO press, the same
        // "unassigned combo" rule ShiftArmed's own doc comment already establishes for every
        // other undefined Shift combination.
        ShiftArmed = false;
        _pendingDigits = string.Empty;
        _composer.Reset();
        DispatchError = null;

        if (_macroRecorder.IsRecording)
        {
            var stopResult = _macroRecorder.Stop();
            DispatchError = stopResult.Success ? null : stopResult.Error;
            _learnArmed = false;
            _pendingOverwriteSlot = null;
            Current = _composer.Current;
            Changed?.Invoke();
            return;
        }

        if (_learnArmed)
        {
            // Second LEARN MACRO with no slot chosen yet (or while an overwrite confirmation was
            // pending) cancels the arm - explicit, no hidden state (§12).
            _learnArmed = false;
            _pendingOverwriteSlot = null;
        }
        else
        {
            _learnArmed = true;
        }

        Current = _composer.Current;
        Changed?.Invoke();
    }

    /// <summary>
    /// MACRO 1-4 (docs/COMMAND_SURFACE_KEY_SPEC.md MACROS §1/§4). SHIFT+MACRO N resolves to slot
    /// N+4 (§1's fixed mapping) - Shift is consumed here exactly like every other key consumes an
    /// armed Shift. Three behaviors depending on state:
    ///   - LEARN MACRO armed, slot free (or this exact slot's overwrite already confirmed once):
    ///     starts recording into the slot.
    ///   - LEARN MACRO armed, slot already holds a non-empty Macro, not yet confirmed: rejects
    ///     with a structured message and re-arms for an explicit second press of the SAME slot to
    ///     confirm overwrite (§11 - no silent overwrite).
    ///   - Not armed: plays the Macro back immediately (self-terminating, no ENTER - §4). Rejected
    ///     cleanly by MacroPlaybackService if a Macro is currently recording or already playing
    ///     back (§8/§9 - no nested/recursive playback).
    /// </summary>
    public void PressMacroSlot(int baseSlot)
    {
        DisarmReleaseContext();

        int slot = ShiftArmed ? baseSlot + 4 : baseSlot;
        ShiftArmed = false;
        _pendingDigits = string.Empty;
        _composer.Reset();
        DispatchError = null;

        if (_learnArmed)
        {
            bool confirmOverwrite = _pendingOverwriteSlot == slot;
            _pendingOverwriteSlot = null;

            var startResult = _macroRecorder.Start(slot, confirmOverwrite);
            if (startResult.Success)
            {
                _learnArmed = false; // now actively recording - the next LEARN MACRO press stops/saves, not re-arms
            }
            else if (startResult.Macro is not null)
            {
                // "Already exists" - re-arm for an explicit second press of this SAME slot to
                // confirm overwrite, the same two-press-to-confirm idiom this Command Surface
                // already uses for RELEASE ENTER.
                _pendingOverwriteSlot = slot;
                DispatchError = $"{startResult.Error} Press MACRO {slot} again to overwrite, or LEARN MACRO to cancel.";
                Current = _composer.Current;
                Changed?.Invoke();
                return;
            }

            DispatchError = startResult.Success ? null : startResult.Error;
            Current = _composer.Current;
            Changed?.Invoke();
            return;
        }

        var playResult = _macroPlayer.Play(slot);
        DispatchError = playResult.Success ? null : (playResult.Error ?? "Macro playback failed.");
        Current = _composer.Current;
        Changed?.Invoke();
    }

    /// <summary>
    /// Explicit cancel for an in-progress LEARN MACRO recording (docs/COMMAND_SURFACE_KEY_SPEC.md
    /// MACROS §12) - discards everything recorded so far, leaving the slot's previous Macro
    /// definition (if any) completely unchanged. No dedicated physical key was specified for this
    /// v1 slice (CLEAR/Backspace/Escape all keep their own normal meanings per §12's own
    /// instruction) - exposed here as a small UI affordance next to the recording indicator; a
    /// future Macro Manager (SHIFT+LEARN MACRO, §1/§14) is the natural home for a more
    /// discoverable control later.
    /// </summary>
    public void CancelLearnMacro()
    {
        DispatchError = null;
        if (_macroRecorder.IsRecording)
        {
            var result = _macroRecorder.Cancel();
            DispatchError = result.Success ? null : result.Error;
        }
        _learnArmed = false;
        _pendingOverwriteSlot = null;
        Changed?.Invoke();
    }

    private void DisarmLearn()
    {
        _learnArmed = false;
        _pendingOverwriteSlot = null;
    }

    private void CommitPendingDigits()
    {
        if (_pendingDigits.Length == 0) return;

        // DMX Universe.Address (§8): a DmxAddress was expected while these digits were entered
        // (PressDecimalPoint already restricted "." to this one literal-separator meaning in that
        // context), so "1.101" here means Universe 1 / Address 101, never the fractional number
        // 1.101 - resolved once, structurally, via the same ExpectedNext signal, not re-guessed
        // from the string's shape.
        if (Current.ExpectedNext.Contains(CommandTokenKind.DmxAddress) && _pendingDigits.Contains('.'))
        {
            var parts = _pendingDigits.Split('.');
            int universe = parts.Length == 2 && int.TryParse(parts[0], out var u) ? u : -1;
            int address = parts.Length == 2 && int.TryParse(parts[1], out var a) ? a : -1;
            Push(CommandToken.DmxAddress(universe, address));
            _pendingDigits = string.Empty;
            return;
        }

        if (double.TryParse(_pendingDigits, out var value)) Push(CommandToken.Number(value));
        _pendingDigits = string.Empty;
    }

    private void Push(CommandToken token)
    {
        var composition = _composer.Push(token);
        DispatchError = null;

        if (composition.EmptyClearRequested)
        {
            // Empty-line CLEAR is handled in PressClear so this path is only a defensive fallback.
            Current = _composer.Current;
            Changed?.Invoke();
            return;
        }

        if (composition.IsComplete && composition.ReadyOperation is not null)
        {
            var result = _dispatcher.Dispatch(composition.ReadyOperation);
            if (result.Success)
            {
                _context.SelectionCycle.RememberSelection(_context.Selection.Items);
                if (composition.ResolvedGroupNumber is int groupNumber) _context.SelectionCycle.RememberGroup(groupNumber);
                if (composition.AppliedAtPercent is double atPercent) _context.SelectionCycle.RememberAt(atPercent);
                if (!_mirrorsExistingSelection)
                    _context.SelectionCycle.RecordGesture();

                if (composition.EndsSelectionCycle)
                {
                    _context.SelectionCycle.MarkExecutionCompleted();
                    _composer.ReplaceSelectionOnResolve = true;
                    // A programming execution returns the Task line to true idle (§A) - clears
                    // whatever selection-context echo a prior pure-selection gesture left showing.
                    _selectionContextEcho = null;
                }
                else
                {
                    _context.SelectionCycle.MarkSelectionStarted();
                    _composer.ReplaceSelectionOnResolve = false;
                    // A pure selection expression completing must NOT make the Task line look
                    // idle (§A) - echo what was just resolved so DisplayPreview keeps showing it
                    // even though the composer's own working tokens are about to reset below.
                    _selectionContextEcho = composition.PreviewText;
                }

                _mirrorsExistingSelection = false;
                _composer.Reset();
                Current = _composer.Current;
            }
            else
            {
                DispatchError = result.Error ?? "Command failed.";
                Current = composition;
            }
            Changed?.Invoke();
            return;
        }

        Current = composition;
        Changed?.Invoke();
    }
}

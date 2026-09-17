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
    private bool _clearArmedForFullSelection;
    private bool _mirrorsExistingSelection;

    /// <summary>Set the instant LEARN MACRO is pressed with no recording in progress - the next
    /// MACRO N press chooses which slot to record into and starts recording (docs/COMMAND_SURFACE_KEY_SPEC.md
    /// MACROS §3). A second LEARN MACRO press while armed (no slot chosen yet) cancels the arm -
    /// the same "armed for exactly the next relevant key" idiom as ShiftArmed.</summary>
    private bool _learnArmed;

    /// <summary>Set when a LEARN MACRO + MACRO N press targets a slot that already holds a
    /// non-empty Macro - re-arms LEARN so pressing the SAME slot again explicitly confirms
    /// overwrite (§11: no silent overwrite), the same two-press-to-confirm idiom this Command
    /// Surface already uses for CLEAR CLEAR / RELEASE ENTER.</summary>
    private int? _pendingOverwriteSlot;

    /// <summary>Set the instant a bare RELEASE (empty command line) fires "release current
    /// selection" - if ENTER is the very next press, with nothing else in between, it escalates
    /// to "Clear Entire Editor/Programmer" for every patched fixture (docs/COMMAND_SURFACE_KEY_SPEC.md
    /// §7's "RELEASE ENTER"). Any other press disarms it - this is the same two-press-gesture
    /// pattern as _clearArmedForFullSelection/CLEAR CLEAR, applied to RELEASE/RELEASE-ENTER.</summary>
    private bool _releaseArmedForFullClear;

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

    public string DisplayPreview => _pendingDigits.Length == 0 ? Current.PreviewText : $"{Current.PreviewText} {_pendingDigits}".TrimStart();

    /// <summary>
    /// Mirrors a fixture selection made outside the keypad into the same CommandComposer. The
    /// GUI has already mutated Selection, so resolving this mirrored line must be idempotent.
    /// </summary>
    public void SynchronizeFixtureSelection(IEnumerable<PatchedFixture> fixtures)
    {
        _pendingDigits = string.Empty;
        DispatchError = null;
        _clearArmedForFullSelection = false;
        _releaseArmedForFullClear = false;
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
        _clearArmedForFullSelection = false;
        _releaseArmedForFullClear = false;
        ShiftArmed = false;
        DisarmLearn();

        // A number added to a mirrored selection is a new selection gesture unless it is the
        // value following AT. This matters for single-CLEAR history.
        if (_mirrorsExistingSelection && !Current.Tokens.Any(t => t.Kind == CommandTokenKind.At))
            _mirrorsExistingSelection = false;

        _pendingDigits += digit;
        DispatchError = null;
        Changed?.Invoke();
    }

    public void PressDecimalPoint()
    {
        _clearArmedForFullSelection = false;
        _releaseArmedForFullClear = false;
        ShiftArmed = false;
        DisarmLearn();

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
        _clearArmedForFullSelection = false;

        // RELEASE ENTER (§7): bare ENTER pressed immediately after a bare RELEASE, with nothing
        // else typed in between, escalates to Clear Entire Editor/Programmer for every patched
        // fixture - a deliberate two-press gesture (see PressRelease), not composer grammar. Any
        // other token (including a second Enter with nothing armed) disarms it below as normal.
        if (kind == CommandTokenKind.Enter && _releaseArmedForFullClear && Current.Tokens.Count == 0 && _pendingDigits.Length == 0)
        {
            _releaseArmedForFullClear = false;
            ShiftArmed = false;
            DisarmLearn();
            var result = _dispatcher.Dispatch(new ReleaseCommand(_context.Patch.Fixtures.ToList(), null));
            DispatchError = result.Success ? null : (result.Error ?? "Release failed.");
            Changed?.Invoke();
            return;
        }
        _releaseArmedForFullClear = false;
        ShiftArmed = false;
        DisarmLearn();

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
        _clearArmedForFullSelection = false;
        _releaseArmedForFullClear = false;
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
            _composer.Reset();
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
        _clearArmedForFullSelection = false;
        _releaseArmedForFullClear = false;
        ShiftArmed = false;
        DisarmLearn();
        _pendingDigits = string.Empty;
        _mirrorsExistingSelection = false;
        _composer.Reset();

        Push(CommandToken.Parameter(channelType));
    }

    /// <summary>
    /// RELEASE (docs/COMMAND_SURFACE_KEY_SPEC.md §7). Mid-composition (a family token already
    /// pushed, e.g. "POSITION") this is ordinary grammar - the composer resolves
    /// "&lt;Family&gt; RELEASE" itself via PressToken. On an EMPTY command line, RELEASE is a
    /// two-press gesture rather than composer grammar: it fires immediately as "release the
    /// current selection, all families" (self-terminating, like any other unambiguous Action
    /// key), and arms _releaseArmedForFullClear so an immediately-following bare ENTER (see
    /// PressToken) escalates to "Clear Entire Editor/Programmer" for every patched fixture -
    /// never inferred from a timeout.
    /// </summary>
    public void PressRelease()
    {
        _clearArmedForFullSelection = false;
        DisarmLearn();

        if (ShiftArmed)
        {
            ShiftArmed = false;
            _releaseArmedForFullClear = false;
            var shiftResult = _dispatcher.DispatchAction(new ReleaseAllPlaybacksAction());
            DispatchError = shiftResult.Success ? null : (shiftResult.Error ?? "Release All Playbacks failed.");
            Changed?.Invoke();
            return;
        }

        if (Current.Tokens.Count > 0 || _pendingDigits.Length > 0)
        {
            _releaseArmedForFullClear = false;
            ShiftArmed = false;
            PressToken(CommandTokenKind.Release);
            return;
        }

        DispatchError = null;
        var targets = _context.Selection.Items.ToList();
        if (targets.Count == 0)
        {
            DispatchError = "Select at least one fixture first.";
            _releaseArmedForFullClear = false;
            ShiftArmed = false;
            Changed?.Invoke();
            return;
        }

        var result = _dispatcher.Dispatch(new ReleaseCommand(targets, null));
        DispatchError = result.Success ? null : (result.Error ?? "Release failed.");
        _releaseArmedForFullClear = result.Success;
        Changed?.Invoke();
    }

    public void PressBackspace()
    {
        _clearArmedForFullSelection = false;
        _releaseArmedForFullClear = false;
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
    /// CLEAR (docs/COMMAND_SURFACE_KEY_SPEC.md §15). Priority, highest first: (A) while a numeric
    /// token is being entered, CLEAR behaves exactly like Backspace, one digit at a time - this is
    /// the same digit buffer PressBackspace already edits, so it shares that exact behavior rather
    /// than discarding the whole partial number the way this method used to. (B) with no pending
    /// digit but an active command line, CLEAR removes the last logical token/gesture (pushed to
    /// the composer, same as before). (C)/(D) with an empty command line, CLEAR follows the
    /// operator-defined selection semantics: first CLEAR removes the last selection gesture (a
    /// whole Group/range counts as one); a second consecutive CLEAR clears the entire selection.
    /// Both mutations are ordinary undoable Application commands.
    /// </summary>
    public void PressClear()
    {
        _releaseArmedForFullClear = false;
        ShiftArmed = false;
        DisarmLearn();

        if (_pendingDigits.Length > 0)
        {
            _pendingDigits = _pendingDigits[..^1];
            Changed?.Invoke();
            return;
        }

        if (Current.Tokens.Count > 0)
        {
            _clearArmedForFullSelection = false;
            _mirrorsExistingSelection = false;
            Push(CommandToken.Simple(CommandTokenKind.Clear));
            return;
        }

        DispatchError = null;

        if (_clearArmedForFullSelection)
        {
            var result = _dispatcher.Dispatch(new ClearSelectionCommand());
            if (result.Success)
            {
                _context.SelectionCycle.ClearGestureHistory();
                _context.SelectionCycle.MarkSelectionStarted();
            }
            _clearArmedForFullSelection = false;
            _composer.Reset();
            Current = _composer.Current;
            Changed?.Invoke();
            return;
        }

        if (_context.SelectionCycle.TryPopGesture(out var baseline))
        {
            var result = _dispatcher.Dispatch(new ReplaceSelectionCommand(baseline));
            _clearArmedForFullSelection = result.Success;
        }
        else if (_context.Selection.Items.LastOrDefault() is { } last)
        {
            // Compatibility fallback for a selection that predates gesture tracking.
            var result = _dispatcher.Dispatch(new RemoveFixtureFromSelectionCommand(last));
            _clearArmedForFullSelection = result.Success;
        }
        else
        {
            _clearArmedForFullSelection = true;
        }

        _composer.Reset();
        _composer.ReplaceSelectionOnResolve = false;
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
        _clearArmedForFullSelection = false;
        _releaseArmedForFullClear = false;
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
        _clearArmedForFullSelection = false;
        _releaseArmedForFullClear = false;

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
                // already uses for CLEAR CLEAR / RELEASE ENTER.
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
            bool startsFresh = _composer.ReplaceSelectionOnResolve;
            IReadOnlyList<PatchedFixture> baseline = startsFresh
                ? Array.Empty<PatchedFixture>()
                : _context.Selection.Items.ToList();

            var result = _dispatcher.Dispatch(composition.ReadyOperation);
            if (result.Success)
            {
                _context.SelectionCycle.RememberSelection(_context.Selection.Items);
                if (composition.ResolvedGroupNumber is int groupNumber) _context.SelectionCycle.RememberGroup(groupNumber);
                if (composition.AppliedAtPercent is double atPercent) _context.SelectionCycle.RememberAt(atPercent);
                if (!_mirrorsExistingSelection)
                    _context.SelectionCycle.RecordGesture(baseline, startsFresh);

                if (composition.EndsSelectionCycle)
                {
                    _context.SelectionCycle.MarkExecutionCompleted();
                    _composer.ReplaceSelectionOnResolve = true;
                }
                else
                {
                    _context.SelectionCycle.MarkSelectionStarted();
                    _composer.ReplaceSelectionOnResolve = false;
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

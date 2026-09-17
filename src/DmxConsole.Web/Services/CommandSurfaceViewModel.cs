using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Application.Commands.Playback;
using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Application.Commands.Selection;
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
    private CommandComposer _composer;
    private string _pendingDigits = string.Empty;
    private bool _clearArmedForFullSelection;
    private bool _mirrorsExistingSelection;

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
        Current = _composer.Current;
    }

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
            var result = _dispatcher.Dispatch(new ReleaseCommand(_context.Patch.Fixtures.ToList(), null));
            DispatchError = result.Success ? null : (result.Error ?? "Release failed.");
            Changed?.Invoke();
            return;
        }
        _releaseArmedForFullClear = false;
        ShiftArmed = false;

        CommitPendingDigits();

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
    /// PARAMETER RELEASE's addressed-channel key (docs/COMMAND_SURFACE_KEY_SPEC.md §7, e.g. "PAN")
    /// - the Command Surface's entry point for a Parameter token, mirroring PressToken but for a
    /// token that carries a ChannelType payload rather than being a bare CommandTokenKind. Like
    /// pressing any other composing key, this disarms both two-press gestures (RELEASE ENTER,
    /// Shift) - only bare RELEASE itself ever arms the escalation, never a Parameter/Family key.
    /// </summary>
    public void PressParameter(ChannelType channelType)
    {
        _clearArmedForFullSelection = false;
        _releaseArmedForFullClear = false;
        ShiftArmed = false;
        CommitPendingDigits();
        _mirrorsExistingSelection = false;

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

    private void CommitPendingDigits()
    {
        if (_pendingDigits.Length == 0) return;
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

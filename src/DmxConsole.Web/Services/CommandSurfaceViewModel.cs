using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Application.Commands.Selection;
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

    public CommandComposition Current { get; private set; }
    public string? DispatchError { get; private set; }

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

    public void PressBackspace()
    {
        _clearArmedForFullSelection = false;
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
    /// CLEAR follows the operator-defined selection semantics when the command line is empty:
    /// first CLEAR removes the last selection gesture (a whole Group/range counts as one);
    /// a second consecutive CLEAR clears the entire selection. Both mutations are ordinary
    /// undoable Application commands. While a command is being composed, CLEAR remains a line
    /// edit and does not touch live Selection.
    /// </summary>
    public void PressClear()
    {
        _pendingDigits = string.Empty;

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

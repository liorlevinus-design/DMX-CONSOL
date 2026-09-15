using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Core.Fixtures;
using DmxConsole.Web.EditorToolBar;

namespace DmxConsole.Web.Services;

/// <summary>
/// Bridges a CommandSurface (touch keypad, physical keyboard, future MIDI/HID) to the
/// UI-independent CommandComposer. Owns exactly one piece of state the composer itself
/// deliberately does NOT: the digits the operator is currently typing before they become a
/// committed CommandToken.Number - a numeric keypad reports individual digit presses, but the
/// composer's grammar only ever deals in whole tokens.
/// </summary>
public sealed class CommandSurfaceViewModel
{
    private readonly ConsoleContext _context;
    private readonly CommandDispatcher _dispatcher;
    private readonly EditorContextStack _editorContext;
    private CommandComposer _composer;
    private string _pendingDigits = string.Empty;

    public CommandComposition Current { get; private set; }

    public string? DispatchError { get; private set; }

    public event Action? Changed;

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
    /// Mirrors a fixture selection made outside the keypad (mouse/touch Views) into the SAME
    /// CommandComposer used by the Command Surface. It does not dispatch a second selection
    /// mutation; it only makes a subsequent keypad action such as AT 50 target the fixtures that
    /// are already visibly selected.
    /// </summary>
    public void SynchronizeFixtureSelection(IEnumerable<PatchedFixture> fixtures)
    {
        _pendingDigits = string.Empty;
        DispatchError = null;
        _composer.Reset();

        var ordered = fixtures
            .Distinct()
            .OrderBy(f => f.Number)
            .ToList();

        // The GUI has already applied this selection. Re-resolving it before AT must be
        // idempotent and must not clear/toggle the same fixtures away.
        _composer.ReplaceSelectionOnResolve = false;

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
        _pendingDigits += digit;
        DispatchError = null;
        Changed?.Invoke();
    }

    public void PressDecimalPoint()
    {
        if (_pendingDigits.Contains('.')) return;
        _pendingDigits += _pendingDigits.Length == 0 ? "0." : ".";
        Changed?.Invoke();
    }

    /// <summary>Every non-numeric token (object types, operators, Enter) - commits any pending
    /// digits as a Number token first, then pushes this one.</summary>
    public void PressToken(CommandTokenKind kind)
    {
        CommitPendingDigits();

        var objectType = kind switch
        {
            CommandTokenKind.Fixture => EditorObjectType.Fixture,
            CommandTokenKind.Group => EditorObjectType.Group,
            CommandTokenKind.Cue => EditorObjectType.Cue,
            _ => (EditorObjectType?)null,
        };
        if (objectType is { } type) _editorContext.EnterObject(type, kind.ToString().ToUpperInvariant());

        // Fixture/Group selections append while the current cycle is still open. Once an
        // execution (AT today) closes it, the first new object selection replaces the old cycle.
        if (kind is CommandTokenKind.Fixture or CommandTokenKind.Group)
        {
            _composer.ReplaceSelectionOnResolve =
                _context.SelectionCycle.StartFreshOnNextSelection || _context.Selection.Items.Count == 0;
        }

        Push(CommandToken.Simple(kind));
    }

    public void PressBackspace()
    {
        if (_pendingDigits.Length > 0)
        {
            _pendingDigits = _pendingDigits[..^1];
            Changed?.Invoke();
            return;
        }

        Push(CommandToken.Simple(CommandTokenKind.Backspace));
    }

    public void PressClear()
    {
        _pendingDigits = string.Empty;
        Push(CommandToken.Simple(CommandTokenKind.Clear));
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
            // CLEAR semantics are refined in the next slice; for now preserve the existing
            // explicit empty-line behavior through the same undoable selection command path.
            _dispatcher.Dispatch(new ClearSelectionCommand());
            _context.SelectionCycle.MarkSelectionStarted();
            Current = _composer.Current;
            Changed?.Invoke();
            return;
        }

        if (composition.IsComplete && composition.ReadyOperation is not null)
        {
            var result = _dispatcher.Dispatch(composition.ReadyOperation);
            if (result.Success)
            {
                if (composition.EndsSelectionCycle)
                {
                    // Selection stays visible. Only the *next* new selection starts fresh.
                    _context.SelectionCycle.MarkExecutionCompleted();
                    _composer.ReplaceSelectionOnResolve = true;
                }
                else
                {
                    // A selection-only Enter is another element in the same cycle; the next
                    // Fixture/Group command therefore appends without requiring +.
                    _context.SelectionCycle.MarkSelectionStarted();
                    _composer.ReplaceSelectionOnResolve = false;
                }

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

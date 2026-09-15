using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Web.EditorToolBar;

namespace DmxConsole.Web.Services;

/// <summary>
/// Bridges a CommandSurface (touch keypad, physical keyboard, future MIDI/HID) to the
/// UI-independent CommandComposer. Owns exactly one piece of state the composer itself
/// deliberately does NOT: the digits the operator is currently typing before they become a
/// committed CommandToken.Number - a numeric keypad reports individual digit presses, but the
/// composer's grammar only ever deals in whole tokens (see CommandComposer's own doc comment).
/// Any non-digit key first commits whatever digits are pending, then pushes itself.
/// </summary>
public sealed class CommandSurfaceViewModel
{
    private readonly ConsoleContext _context;
    private readonly CommandDispatcher _dispatcher;
    private readonly EditorContextStack _editorContext;
    private CommandComposer _composer;
    private string _pendingDigits = string.Empty;

    public CommandComposition Current { get; private set; }

    /// <summary>Set only by a Dispatch-time failure (the composer already validated references
    /// during composition - see CommandComposer.Resolve - so this path is rare, e.g. a command
    /// that fails at Execute for a reason the composer couldn't foresee). Distinct from
    /// Current.Error (a composition/parse-time problem) - both render on CommandLine, but this
    /// one implies the command LOOKED complete and valid, then failed to actually run.</summary>
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

    /// <summary>What CommandLine actually renders - the composer's preview plus whatever digits
    /// are still being typed (shown with a trailing cursor-ish underscore, e.g. "FIXTURE 1 AT 7_").</summary>
    public string DisplayPreview => _pendingDigits.Length == 0 ? Current.PreviewText : $"{Current.PreviewText} {_pendingDigits}".TrimStart();

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

        // H1.6 §19 - there is ONE shared operator context. Typing an object-type token here
        // enters the same EditorContextStack a View row click would, so the Editor Tool Bar
        // reacts identically regardless of which surface the operator used (Scenario D).
        var objectType = kind switch
        {
            CommandTokenKind.Fixture => EditorObjectType.Fixture,
            CommandTokenKind.Group => EditorObjectType.Group,
            CommandTokenKind.Cue => EditorObjectType.Cue,
            _ => (EditorObjectType?)null,
        };
        if (objectType is { } type) _editorContext.EnterObject(type, kind.ToString().ToUpperInvariant());

        Push(CommandToken.Simple(kind));
    }

    /// <summary>Backspace edits the in-progress number if there is one (removing the last typed
    /// digit), otherwise removes the last already-committed token - matches how a physical
    /// console keypad's Backspace/Clear behaves while still typing a value.</summary>
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

    /// <summary>Clear always discards any in-progress digits too, even though CommandComposer's
    /// own Clear only knows about already-committed tokens.</summary>
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
            // The composer never touches ConsoleContext itself - this is that explicit,
            // separate dispatch, scoped to Selection only (never the Programmer - see
            // CommandComposer's own doc comment for why that boundary was chosen).
            _dispatcher.Dispatch(new ClearSelectionCommand());
            Current = _composer.Current;
            Changed?.Invoke();
            return;
        }

        if (composition.IsComplete && composition.ReadyOperation is not null)
        {
            var result = _dispatcher.Dispatch(composition.ReadyOperation);
            if (result.Success)
            {
                _composer.Reset();
                Current = _composer.Current;
            }
            else
            {
                // Preserve the entered command for correction - never silently discard it on failure.
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

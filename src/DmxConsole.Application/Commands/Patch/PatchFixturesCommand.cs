using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Patch;

/// <summary>
/// The ONE shared Patch operation (Quick Patch stabilization slice): creates a batch of new
/// fixtures at explicit, operator-chosen Numbers/addresses, using one FixtureProfile/FixtureMode
/// for all of them. Both the PATCH screen (MainViewModel.AddFixture) and the Command Surface's
/// Quick Patch grammar ("FIXTURE 1 THRU 8 AT DMX 11 ENTER" / "DMX 11 THRU 18 FIXTURE 1 ENTER")
/// build a list of <see cref="Request"/> and dispatch this same command - never two patch
/// engines, never duplicated numbering/address/atomicity logic.
///
/// Atomic: every requested Number is validated as free BEFORE any fixture is created (Patch.Add's
/// own silent-renumber-onto-next-free-number fallback is deliberately bypassed, same rule
/// AddFixture already established - a console operator asking for #105 must get #105 or an
/// explicit conflict, never a silent substitute), and if fixture creation itself throws partway
/// through (e.g. an address range overlapping an existing fixture), everything already added in
/// THIS call is rolled back - "patch 8" never leaves 3 half-applied on failure. This atomicity is
/// entirely internal to Execute and does not depend on Undo in any way.
///
/// IConsoleAction, not IConsoleCommand (Patch/Undo-architecture slice): Patch is STRUCTURAL show
/// data, not ordinary Programming state - it must never enter the same Undo stack as AT values,
/// HOME, Preset recall, etc. (undoing three ordinary programming steps must never unexpectedly
/// repatch/delete fixtures as a side effect). Dispatched via CommandDispatcher.DispatchAction,
/// which structurally never touches UndoRedoService - the same guarantee Go/Back/Stop/Flash
/// already rely on. This means a successful Quick Patch cannot currently be undone by any stack;
/// a separate Structural Show/Patch history is future work, not built in this slice. Deliberately
/// still fully recordable/replayable into a Macro (MacroRecorder.OnActionExecuted records every
/// Action except ClearSelectionAction) - an operator who deliberately records a Patch into a
/// Macro gets that back on replay, dispatched through this exact same Execute, atomically and
/// rollback-safe every time (e.g. replaying against already-patched numbers fails cleanly with
/// the same "already patched" error, never partial/corrupt state).
/// </summary>
public sealed class PatchFixturesCommand : IConsoleAction
{
    public readonly record struct Request(int Number, int UniverseId, int Address, string Name);

    private readonly FixtureProfile _profile;
    private readonly FixtureMode _mode;
    private readonly IReadOnlyList<Request> _requests;

    public PatchFixturesCommand(FixtureProfile profile, FixtureMode mode, IReadOnlyList<Request> requests)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
        _requests = requests;
    }

    public CommandResult Execute(ConsoleContext context)
    {
        if (_requests.Count == 0)
            return CommandResult.Failed(ConsoleActionType.PatchFixtures, "Nothing to patch.");

        foreach (var request in _requests)
        {
            if (context.Patch.FindByNumber(request.Number) is not null)
                return CommandResult.Failed(ConsoleActionType.PatchFixtures,
                    $"Fixture #{request.Number} is already patched - choose a different starting number or count.");
        }

        var created = new List<PatchedFixture>(_requests.Count);
        try
        {
            foreach (var request in _requests)
            {
                var fixture = new PatchedFixture(_profile, _mode, request.UniverseId, request.Address, request.Name)
                {
                    Number = request.Number,
                };
                context.Patch.Add(fixture);
                created.Add(fixture);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            foreach (var fixture in created) context.Patch.Remove(fixture);
            return CommandResult.Failed(ConsoleActionType.PatchFixtures, ex.Message);
        }

        return new CommandResult { ActionType = ConsoleActionType.PatchFixtures, AffectedFixtures = created };
    }
}

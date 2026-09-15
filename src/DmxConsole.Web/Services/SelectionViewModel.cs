using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Application;
using DmxConsole.Application.Commands;
using DmxConsole.Application.Commands.Groups;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;

namespace DmxConsole.Web.Services;

/// <summary>
/// Drives the console's shared "current selection": fixture number buttons, Thru/Odd/Even/
/// Next/Previous, and saved Groups. Every mutation goes through the CommandDispatcher -
/// this class only decides WHICH command to build (including the two-step Thru gesture,
/// a pure UI workflow concern) and reads back the resulting FixtureSelection/GroupManager
/// for display, both exposed unchanged via ConsoleContext.
/// </summary>
public partial class SelectionViewModel : ObservableObject
{
    private readonly ConsoleContext _context;
    private readonly CommandDispatcher _dispatcher;

    public FixtureSelection Selection => _context.Selection;
    public GroupManager Groups => _context.Groups;

    /// <summary>Fixture number a "Thru" is waiting to be completed against, or null if not armed.</summary>
    [ObservableProperty] private int? _pendingThruStart;

    [ObservableProperty] private string _newGroupName = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public SelectionViewModel(ConsoleContext context, CommandDispatcher dispatcher)
    {
        _context = context;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Tapping/selecting a new object follows the shared selection-cycle rule: before execution
    /// selections accumulate; after an execution the existing selection remains visible, but
    /// the first new selection atomically clears the old cycle and starts a new one.
    /// </summary>
    [RelayCommand]
    private void TapFixtureNumber(PatchedFixture fixture)
    {
        if (PendingThruStart is int startNumber)
        {
            DispatchSelectionGesture(new SelectRangeCommand(startNumber, fixture.Number));
            PendingThruStart = null;
        }
        else
        {
            DispatchSelectionGesture(new ToggleFixtureCommand(fixture));
        }
    }

    [RelayCommand]
    private void ArmThru()
    {
        var last = Selection.Items.LastOrDefault();
        if (last is null)
        {
            StatusMessage = "Select a fixture first, then Thru.";
            return;
        }

        PendingThruStart = last.Number;
    }

    [RelayCommand] private void SelectOdd() => _dispatcher.Dispatch(new SelectOddCommand());

    [RelayCommand] private void SelectEven() => _dispatcher.Dispatch(new SelectEvenCommand());

    [RelayCommand]
    private void ClearSelection()
    {
        _dispatcher.Dispatch(new ClearSelectionCommand());
        _context.SelectionCycle.MarkSelectionStarted();
        PendingThruStart = null;
    }

    [RelayCommand] private void Next() => DispatchSelectionGesture(new NextFixtureCommand());

    [RelayCommand] private void Previous() => DispatchSelectionGesture(new PreviousFixtureCommand());

    [RelayCommand]
    private void AddGroupToSelection(FixtureGroup group) =>
        DispatchSelectionGesture(new AddGroupToSelectionCommand(group));

    private void DispatchSelectionGesture(IConsoleCommand command)
    {
        IConsoleCommand operation = command;
        if (_context.SelectionCycle.StartFreshOnNextSelection)
        {
            // One transaction: Undo restores the pre-gesture selection, never an intermediate
            // "cleared but not yet selected" state.
            operation = new CompositeCommand(new IConsoleCommand[]
            {
                new ClearSelectionCommand(),
                command,
            });
        }

        var result = _dispatcher.Dispatch(operation);
        if (result.Success) _context.SelectionCycle.MarkSelectionStarted();
    }

    [RelayCommand]
    private void SaveGroup()
    {
        if (Selection.Items.Count == 0)
        {
            StatusMessage = "Nothing selected to save as a group.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewGroupName) ? $"Group {Groups.Groups.Count + 1}" : NewGroupName;
        _dispatcher.Dispatch(new CreateGroupCommand(name));
        NewGroupName = string.Empty;
    }

    [RelayCommand]
    private void RemoveGroup(FixtureGroup group)
    {
        var result = _dispatcher.Dispatch(new RemoveGroupCommand(group));
        if (!result.Success) StatusMessage = result.Error ?? "Could not remove that group.";
    }
}

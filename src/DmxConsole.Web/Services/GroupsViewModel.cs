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
/// Drives the first-class Groups View - Store/Update/Rename/Remove/Apply, all through
/// CommandDispatcher. Group Apply participates in the same shared selection-cycle state as
/// Fixture clicks and the Command Surface; there is no separate "Groups selection" model.
/// </summary>
public partial class GroupsViewModel : ObservableObject
{
    private readonly ConsoleContext _context;
    private readonly CommandDispatcher _dispatcher;

    public GroupManager Groups => _context.Groups;
    public FixtureSelection Selection => _context.Selection;

    [ObservableProperty] private string _newGroupName = string.Empty;
    [ObservableProperty] private int? _newGroupNumber;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public GroupsViewModel(ConsoleContext context, CommandDispatcher dispatcher)
    {
        _context = context;
        _dispatcher = dispatcher;
    }

    /// <summary>Adds the group's members to the current selection - "Apply".</summary>
    [RelayCommand]
    private void Apply(FixtureGroup group)
    {
        bool startsFresh = _context.SelectionCycle.StartFreshOnNextSelection;
        IReadOnlyList<PatchedFixture> baseline = startsFresh
            ? Array.Empty<PatchedFixture>()
            : Selection.Items.ToList();

        IConsoleCommand operation = new AddGroupToSelectionCommand(group);
        if (startsFresh)
        {
            operation = new CompositeCommand(new IConsoleCommand[]
            {
                new ClearSelectionCommand(),
                operation,
            });
        }

        var result = _dispatcher.Dispatch(operation);
        if (result.Success)
        {
            _context.SelectionCycle.RecordGesture(baseline, startsFresh);
            _context.SelectionCycle.RememberSelection(Selection.Items);
            _context.SelectionCycle.RememberGroup(group.Number);
        }
    }

    [RelayCommand]
    private void Store()
    {
        string name = string.IsNullOrWhiteSpace(NewGroupName) ? $"Group {Groups.Groups.Count + 1}" : NewGroupName;
        var result = _dispatcher.Dispatch(new StoreGroupCommand(name, NewGroupNumber));
        StatusMessage = result.Success
            ? $"Stored Group #{result.Group!.Number} \"{result.Group.Name}\"."
            : result.Error ?? "Could not store group.";
        if (result.Success) { NewGroupName = string.Empty; NewGroupNumber = null; }
    }

    [RelayCommand]
    private void Update(FixtureGroup group)
    {
        var result = _dispatcher.Dispatch(new StoreGroupCommand(group.Name, group.Number, group));
        StatusMessage = result.Success ? $"Updated Group #{group.Number} \"{group.Name}\"." : result.Error ?? "Could not update group.";
    }

    public void Rename(FixtureGroup group, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        _dispatcher.Dispatch(new RenameGroupCommand(group, newName));
    }

    [RelayCommand]
    private void Remove(FixtureGroup group)
    {
        var result = _dispatcher.Dispatch(new RemoveGroupCommand(group));
        if (!result.Success) StatusMessage = result.Error ?? "Could not remove that group.";
    }
}

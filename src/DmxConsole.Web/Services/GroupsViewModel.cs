using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Application;
using DmxConsole.Application.Commands.Groups;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Core.Selection;

namespace DmxConsole.Web.Services;

/// <summary>
/// Drives the first-class Groups View - Store/Update/Rename/Remove/Apply, all through
/// CommandDispatcher (StoreGroupCommand/RenameGroupCommand/RemoveGroupCommand/
/// AddGroupToSelectionCommand). Reads the same GroupManager/FixtureSelection every other
/// selection-related surface reads (ConsoleContext.Groups/Selection) - Groups are no longer
/// something only visible inside SelectionBar's save-as-group corner.
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
    private void Apply(FixtureGroup group) => _dispatcher.Dispatch(new AddGroupToSelectionCommand(group));

    /// <summary>Stores the current selection as a brand-new Group at the explicit
    /// NewGroupNumber (or auto-assigned if left blank).</summary>
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

    /// <summary>Re-stores an existing Group's membership (and optionally name) from the
    /// current selection - "Update".</summary>
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

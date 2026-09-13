using System.Collections.ObjectModel;

namespace DmxConsole.Core.Selection;

/// <summary>Owns the show's saved fixture Groups.</summary>
public sealed class GroupManager
{
    public ObservableCollection<FixtureGroup> Groups { get; } = new();

    public FixtureGroup CreateFromSelection(string name, FixtureSelection selection)
    {
        var group = new FixtureGroup(name, selection.Items);
        Groups.Add(group);
        return group;
    }

    public void Remove(FixtureGroup group) => Groups.Remove(group);
}

using System.Collections.ObjectModel;
using System.Linq;

namespace DmxConsole.Core.Selection;

/// <summary>Owns the show's saved fixture Groups.</summary>
public sealed class GroupManager
{
    public ObservableCollection<FixtureGroup> Groups { get; } = new();

    public FixtureGroup? FindByNumber(int number) => Groups.FirstOrDefault(g => g.Number == number);

    /// <summary>Assigns the next free Number unless one was already given (and isn't taken) -
    /// same NextFreeNumber pattern as Patch/PresetLibrary/ExecutorBank.</summary>
    public FixtureGroup CreateFromSelection(string name, FixtureSelection selection, int? number = null)
    {
        int assigned = number is > 0 && FindByNumber(number.Value) is null ? number.Value : NextFreeNumber();
        var group = new FixtureGroup(name, selection.Items, assigned);
        Groups.Add(group);
        return group;
    }

    public void Remove(FixtureGroup group) => Groups.Remove(group);

    private int NextFreeNumber() => Groups.Count == 0 ? 1 : Groups.Max(g => g.Number) + 1;
}

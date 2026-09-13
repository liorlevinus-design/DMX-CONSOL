using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;

namespace DmxConsole.Web.Services;

/// <summary>
/// Drives the console's shared "current selection": fixture number buttons, Thru/Odd/Even/
/// Next/Previous, and saved Groups. This is UI orchestration only - the actual selection
/// algebra lives in <see cref="FixtureSelection"/>/<see cref="GroupManager"/> in Core.
/// </summary>
public partial class SelectionViewModel : ObservableObject
{
    private readonly Patch _patch;

    public FixtureSelection Selection { get; } = new();
    public GroupManager Groups { get; } = new();

    /// <summary>Fixture number a "Thru" is waiting to be completed against, or null if not armed.</summary>
    [ObservableProperty] private int? _pendingThruStart;

    [ObservableProperty] private string _newGroupName = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public SelectionViewModel(Patch patch)
    {
        _patch = patch;
    }

    /// <summary>Tapping a fixture number: completes a pending Thru range if armed, otherwise toggles it.</summary>
    [RelayCommand]
    private void TapFixtureNumber(PatchedFixture fixture)
    {
        if (PendingThruStart is int startNumber)
        {
            Selection.SelectRange(_patch, startNumber, fixture.Number);
            PendingThruStart = null;
        }
        else
        {
            Selection.Toggle(fixture);
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

    [RelayCommand] private void SelectOdd() => Selection.FilterOdd();

    [RelayCommand] private void SelectEven() => Selection.FilterEven();

    [RelayCommand] private void ClearSelection()
    {
        Selection.Clear();
        PendingThruStart = null;
    }

    [RelayCommand] private void Next() => Selection.Next(_patch);

    [RelayCommand] private void Previous() => Selection.Previous(_patch);

    [RelayCommand]
    private void AddGroupToSelection(FixtureGroup group) => Selection.AddGroup(group);

    [RelayCommand]
    private void SaveGroup()
    {
        if (Selection.Items.Count == 0)
        {
            StatusMessage = "Nothing selected to save as a group.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewGroupName) ? $"Group {Groups.Groups.Count + 1}" : NewGroupName;
        Groups.CreateFromSelection(name, Selection);
        NewGroupName = string.Empty;
    }

    [RelayCommand]
    private void RemoveGroup(FixtureGroup group) => Groups.Remove(group);
}

using CommunityToolkit.Mvvm.ComponentModel;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Web.Services;

/// <summary>One row in a fixture multi-select list (used to build an effect's target group).</summary>
public partial class FixtureSelectionItem : ObservableObject
{
    public PatchedFixture Fixture { get; }
    public string Label => Fixture.Name;

    [ObservableProperty] private bool _isSelected;

    public FixtureSelectionItem(PatchedFixture fixture) => Fixture = fixture;
}

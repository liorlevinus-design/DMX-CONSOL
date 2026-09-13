using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Core.Tests;

public class FixtureSelectionTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer",
        Manufacturer = "Test",
        Model = "Dimmer",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "1ch",
                Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } },
            },
        },
    };

    /// <summary>Builds a patch with <paramref name="count"/> fixtures, numbered 1..count in order.</summary>
    private static Patch BuildPatch(int count)
    {
        var patch = new Patch();
        var profile = Dimmer1();
        for (int i = 0; i < count; i++)
            patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1 + i, $"F{i + 1}"));
        return patch;
    }

    [Fact]
    public void Toggle_AddsThenRemoves()
    {
        var patch = BuildPatch(1);
        var selection = new FixtureSelection();
        var fixture = patch.Fixtures[0];

        selection.Toggle(fixture);
        Assert.True(selection.Contains(fixture));

        selection.Toggle(fixture);
        Assert.False(selection.Contains(fixture));
    }

    [Fact]
    public void SelectRange_AddsInclusiveAscendingByNumber()
    {
        var patch = BuildPatch(10);
        var selection = new FixtureSelection();

        selection.SelectRange(patch, 3, 6);

        Assert.Equal(new[] { 3, 4, 5, 6 }, selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void SelectRange_AcceptsReversedBounds()
    {
        var patch = BuildPatch(10);
        var selection = new FixtureSelection();

        selection.SelectRange(patch, 6, 3);

        Assert.Equal(new[] { 3, 4, 5, 6 }, selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void SelectRange_AppendsToExistingSelection()
    {
        var patch = BuildPatch(10);
        var selection = new FixtureSelection();
        selection.Add(patch.FindByNumber(1)!);

        selection.SelectRange(patch, 8, 9);

        Assert.Equal(new[] { 1, 8, 9 }, selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void FilterOdd_KeepsFirstThirdFifthBySelectionOrder()
    {
        var patch = BuildPatch(6);
        var selection = new FixtureSelection();
        selection.SelectRange(patch, 1, 6);

        selection.FilterOdd();

        Assert.Equal(new[] { 1, 3, 5 }, selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void FilterEven_KeepsSecondFourthSixthBySelectionOrder()
    {
        var patch = BuildPatch(6);
        var selection = new FixtureSelection();
        selection.SelectRange(patch, 1, 6);

        selection.FilterEven();

        Assert.Equal(new[] { 2, 4, 6 }, selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void Next_FromEmpty_SelectsFirstFixture()
    {
        var patch = BuildPatch(3);
        var selection = new FixtureSelection();

        selection.Next(patch);

        Assert.Equal(new[] { 1 }, selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void Next_AdvancesAndReplacesSelection()
    {
        var patch = BuildPatch(3);
        var selection = new FixtureSelection();
        selection.Add(patch.FindByNumber(1)!);

        selection.Next(patch);

        Assert.Equal(new[] { 2 }, selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void Next_WrapsAtEnd()
    {
        var patch = BuildPatch(3);
        var selection = new FixtureSelection();
        selection.Add(patch.FindByNumber(3)!);

        selection.Next(patch);

        Assert.Equal(new[] { 1 }, selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void Previous_WrapsAtStart()
    {
        var patch = BuildPatch(3);
        var selection = new FixtureSelection();
        selection.Add(patch.FindByNumber(1)!);

        selection.Previous(patch);

        Assert.Equal(new[] { 3 }, selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void AddGroup_AddsAllMemberFixtures()
    {
        var patch = BuildPatch(5);
        var selection = new FixtureSelection();
        var group = new FixtureGroup("Back Wash", new[] { patch.FindByNumber(2)!, patch.FindByNumber(4)! });

        selection.AddGroup(group);

        Assert.Equal(new[] { 2, 4 }, selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void GroupManager_CreateFromSelection_SnapshotsCurrentMembers()
    {
        var patch = BuildPatch(5);
        var selection = new FixtureSelection();
        selection.SelectRange(patch, 1, 3);
        var manager = new GroupManager();

        var group = manager.CreateFromSelection("Front", selection);

        Assert.Single(manager.Groups);
        Assert.Equal(new[] { 1, 2, 3 }, group.Fixtures.Select(f => f.Number));

        // Mutating the selection afterwards must not affect the already-saved group.
        selection.Clear();
        Assert.Equal(new[] { 1, 2, 3 }, group.Fixtures.Select(f => f.Number));
    }
}

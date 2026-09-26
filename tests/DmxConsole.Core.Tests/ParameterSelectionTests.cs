using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Core.Tests;

/// <summary>CLAUDE.md §16 (PSEL-1 through PSEL-5) - the shared, first-class Parameter Selection
/// state. Mirrors FixtureSelectionTests' conventions but exercises Parameter Selection's own
/// narrower semantics (ordered, no duplicates, logical-parameter folding, no compatibility
/// filtering) directly against the Core type, with no Application/Web wiring involved.</summary>
public class ParameterSelectionTests
{
    [Fact]
    public void StartsEmpty()
    {
        var selection = new ParameterSelection();

        Assert.True(selection.IsEmpty);
        Assert.Equal(0, selection.Count);
        Assert.Empty(selection.Items);
    }

    [Fact]
    public void Select_OneParameter_AddsIt()
    {
        var selection = new ParameterSelection();

        selection.Select(ChannelType.Pan);

        Assert.False(selection.IsEmpty);
        Assert.Equal(1, selection.Count);
        Assert.True(selection.Contains(ChannelType.Pan));
        Assert.Equal(new[] { ChannelType.Pan }, selection.Items);
    }

    [Fact]
    public void Select_MultipleParameters_PreservesInsertionOrder()
    {
        var selection = new ParameterSelection();

        selection.Select(ChannelType.Pan);
        selection.Select(ChannelType.Tilt);
        selection.Select(ChannelType.ColorRed);

        Assert.Equal(new[] { ChannelType.Pan, ChannelType.Tilt, ChannelType.ColorRed }, selection.Items);
    }

    [Fact]
    public void Select_DuplicateOfAlreadyPresentParameter_DoesNotCreateADuplicateEntry()
    {
        var selection = new ParameterSelection();

        selection.Select(ChannelType.Pan);
        selection.Select(ChannelType.Tilt);
        selection.Select(ChannelType.Pan); // duplicate

        Assert.Equal(new[] { ChannelType.Pan, ChannelType.Tilt }, selection.Items);
        Assert.Equal(2, selection.Count);
    }

    [Fact]
    public void Select_PanFine_NormalizesToLogicalPan()
    {
        var selection = new ParameterSelection();

        selection.Select(ChannelType.PanFine);

        Assert.Equal(new[] { ChannelType.Pan }, selection.Items);
        Assert.DoesNotContain(ChannelType.PanFine, selection.Items);
        Assert.True(selection.Contains(ChannelType.Pan));
        Assert.True(selection.Contains(ChannelType.PanFine)); // Contains also normalizes
    }

    [Fact]
    public void Select_TiltFine_NormalizesToLogicalTilt()
    {
        var selection = new ParameterSelection();

        selection.Select(ChannelType.TiltFine);

        Assert.Equal(new[] { ChannelType.Tilt }, selection.Items);
        Assert.DoesNotContain(ChannelType.TiltFine, selection.Items);
        Assert.True(selection.Contains(ChannelType.Tilt));
    }

    [Fact]
    public void Select_CoarseThenFineOfSamePair_DoesNotDuplicate()
    {
        var selection = new ParameterSelection();

        selection.Select(ChannelType.Pan);
        selection.Select(ChannelType.PanFine); // same logical parameter

        Assert.Equal(new[] { ChannelType.Pan }, selection.Items);
    }

    [Fact]
    public void Remove_NormalizesBeforeRemoving()
    {
        var selection = new ParameterSelection();
        selection.Select(ChannelType.Pan);
        selection.Select(ChannelType.Tilt);

        selection.Remove(ChannelType.PanFine); // removes logical Pan

        Assert.Equal(new[] { ChannelType.Tilt }, selection.Items);
    }

    [Fact]
    public void Clear_EmptiesTheSelection()
    {
        var selection = new ParameterSelection();
        selection.Select(ChannelType.Pan);
        selection.Select(ChannelType.Tilt);

        selection.Clear();

        Assert.True(selection.IsEmpty);
        Assert.Empty(selection.Items);
    }
}

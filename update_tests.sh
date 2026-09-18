#!/bin/bash
cat << 'INNER_EOF' > tests/DmxConsole.Web.Tests/CommandSurfaceViewModelClearTests.cs
using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Application.Commands;
using DmxConsole.Application.Commands.Groups;
using DmxConsole.Application.Commands.Programmer;
using DmxConsole.Application.Commands.Selection;
using DmxConsole.Core.Fixtures;
using DmxConsole.Web.EditorToolBar;
using DmxConsole.Web.Services;
using FluentAssertions;
using Xunit;

namespace DmxConsole.Web.Tests;

public class CommandSurfaceViewModelClearTests
{
    private readonly ConsoleContext _context;
    private readonly CommandDispatcher _dispatcher;
    private readonly CommandSurfaceViewModel _sfc;
    private readonly PatchedFixture _f1 = new(1, new FixtureChannel(1, 1, ChannelType.Intensity, "Dimmer"));
    private readonly PatchedFixture _f2 = new(2, new FixtureChannel(2, 1, ChannelType.Intensity, "Dimmer"));
    private readonly PatchedFixture _f3 = new(3, new FixtureChannel(3, 1, ChannelType.Intensity, "Dimmer"));

    public CommandSurfaceViewModelClearTests()
    {
        _context = new ConsoleContext(new UndoRedoService());
        _context.Patch.PatchFixture(_f1);
        _context.Patch.PatchFixture(_f2);
        _context.Patch.PatchFixture(_f3);
        _dispatcher = new CommandDispatcher(_context);
        _sfc = new CommandSurfaceViewModel(_context, _dispatcher, new EditorContextStack());
    }

    [Fact]
    public void TestA_ConsecutiveSelectionsAccumulate_And_ComposerIdle()
    {
        // Fixture 1 ENTER
        _sfc.PressToken(CommandTokenKind.Fixture);
        _sfc.PressDigit('1');
        _sfc.PressToken(CommandTokenKind.Enter);

        // Fixture 2 ENTER
        _sfc.PressToken(CommandTokenKind.Fixture);
        _sfc.PressDigit('2');
        _sfc.PressToken(CommandTokenKind.Enter);

        // Fixture 3 ENTER
        _sfc.PressToken(CommandTokenKind.Fixture);
        _sfc.PressDigit('3');
        _sfc.PressToken(CommandTokenKind.Enter);

        _context.Selection.Items.Should().Equal(_f1, _f2, _f3);
        _sfc.Current.Tokens.Should().BeEmpty();
    }

    [Fact]
    public void TestB_ProgrammingOperationClosesCycle_NextSelectionStartsFresh()
    {
        var groupA = new FixtureGroup(1, "A", new[] { _f1 });
        var groupB = new FixtureGroup(2, "B", new[] { _f2, _f3 });
        _context.Groups.Groups.Add(groupA);
        _context.Groups.Groups.Add(groupB);

        // Group A ENTER
        _sfc.PressToken(CommandTokenKind.Group);
        _sfc.PressDigit('1');
        _sfc.PressToken(CommandTokenKind.Enter);

        // AT 50 ENTER
        _sfc.PressToken(CommandTokenKind.At);
        _sfc.PressDigit('5');
        _sfc.PressDigit('0');
        _sfc.PressToken(CommandTokenKind.Enter);

        // Group A remains selected, cycle closed
        _context.Selection.Items.Should().Equal(_f1);
        _context.Programmer.IsKnockedOut(_f1.Channels[0]).Should().BeFalse();

        // Group B ENTER
        _sfc.PressToken(CommandTokenKind.Group);
        _sfc.PressDigit('2');
        _sfc.PressToken(CommandTokenKind.Enter);

        // Only Group B selected
        _context.Selection.Items.Should().Equal(_f2, _f3);
    }

    [Fact]
    public void TestC_ClearWithPopulatedEditor_EmptiesSelection_LeavesEditor()
    {
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f1));
        _dispatcher.Dispatch(new AdjustIntensityCommand(new[] { _f1 }, AdjustOperation.Absolute, 50));

        _context.Selection.Items.Should().Equal(_f1);
        _context.Programmer.GetChannelState(_f1.Channels[0]).Value.Should().Be(128); // 50%

        _sfc.PressClear();

        _context.Selection.Items.Should().BeEmpty();
        _context.Programmer.GetChannelState(_f1.Channels[0]).Value.Should().Be(128);
    }

    [Fact]
    public void TestD_ClearWhileComposerHasUnfinishedTokens_ClearsSelection_LeavesComposer()
    {
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f1));

        _sfc.PressDigit('1');
        _sfc.PressToken(CommandTokenKind.Thru);

        _context.Selection.Items.Should().Equal(_f1);
        _sfc.Current.Tokens.Count.Should().BeGreaterThan(0);

        _sfc.PressClear();

        _context.Selection.Items.Should().BeEmpty();
        _sfc.Current.Tokens.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TestE_BackspaceWithPendingDigits_RemovesFinalDigit_LeavesSelection()
    {
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f1));

        _sfc.PressDigit('5');
        _sfc.PressDigit('0');

        _sfc.DisplayPreview.Should().Be("50");

        _sfc.PressBackspace();

        _sfc.DisplayPreview.Should().Be("5");
        _context.Selection.Items.Should().Equal(_f1);
    }

    [Fact]
    public void TestF_BackspaceWithComposerTokens_RemovesFinalToken_LeavesSelection()
    {
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f1));

        _sfc.PressToken(CommandTokenKind.Fixture);
        _sfc.PressDigit('2');
        _sfc.PressToken(CommandTokenKind.Thru);

        _sfc.Current.Tokens.Last().Kind.Should().Be(CommandTokenKind.Thru);

        _sfc.PressBackspace();

        _sfc.Current.Tokens.Last().Kind.Should().Be(CommandTokenKind.Number);
        _context.Selection.Items.Should().Equal(_f1);
    }

    [Fact]
    public void TestG_OddEvenReverse_PreserveOrderedSelectionSemantics()
    {
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f2));
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f3));
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f1));

        _context.Selection.Items.Should().Equal(_f2, _f3, _f1);

        _dispatcher.Dispatch(new SelectOddCommand());
        _context.Selection.Items.Should().Equal(_f2, _f1);

        // Reset
        _dispatcher.Dispatch(new ClearSelectionCommand());
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f2));
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f3));
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f1));

        _dispatcher.Dispatch(new SelectEvenCommand());
        _context.Selection.Items.Should().Equal(_f3);

        // Reset
        _dispatcher.Dispatch(new ClearSelectionCommand());
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f2));
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f3));
        _dispatcher.Dispatch(new AddFixtureToSelectionCommand(_f1));

        _dispatcher.Dispatch(new ReverseSelectionCommand());
        _context.Selection.Items.Should().Equal(_f1, _f3, _f2);
    }
}
INNER_EOF

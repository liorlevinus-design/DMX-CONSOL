using System.Collections.ObjectModel;
using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
using DmxConsole.Core.Selection;
using DmxConsole.Web.EditorToolBar;
using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>
/// Legacy UI cleanup slice - PresetPanel's row is now the immediate "apply" action (no separate
/// Apply button). This proves the full intended chain end to end through PresetViewModel, the
/// same ViewModel PresetPanel.razor's row click dispatches to: current Selection -> Apply ->
/// values land in PROGRAMMER for that Selection -> Selection stays selected -> SelectionCycle
/// closes so the next Fixture/Group gesture starts fresh (mirrors
/// CommandSurfaceViewModelSelectionCycleTests' pattern for HOME/RELEASE/CAPTURE ALL).
/// </summary>
public class PresetViewModelSelectionCycleTests
{
    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer", Manufacturer = "Test", Model = "Dimmer",
        Modes = new[] { new FixtureMode { Name = "1ch", Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } } } },
    };

    private static (ConsoleContext Context, CommandSurfaceViewModel Surface, PresetViewModel Presets, PatchedFixture A, PatchedFixture B) BuildRig()
    {
        var patch = new Patch();
        var profile = Dimmer1();
        var a = new PatchedFixture(profile, profile.Modes[0], 0, 1) { Number = 1 };
        var b = new PatchedFixture(profile, profile.Modes[0], 0, 2) { Number = 2 };
        patch.Add(a);
        patch.Add(b);
        var engine = new DmxOutputEngine(patch);
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(),
            engine, new PresetLibrary(), new ExecutorBank());
        var dispatcher = new CommandDispatcher(context, new UndoRedoService(context));
        var surface = CommandSurfaceViewModelTestSupport.BuildCommandSurfaceViewModel(context, dispatcher, new EditorContextStack());
        var programmerVm = new ProgrammerViewModel(context, dispatcher, new ObservableCollection<ChannelFaderViewModel>());
        var presets = new PresetViewModel(context, dispatcher, programmerVm);
        return (context, surface, presets, a, b);
    }

    private static void SelectFixture(CommandSurfaceViewModel surface, int number)
    {
        surface.PressToken(CommandTokenKind.Fixture);
        foreach (char c in number.ToString()) surface.PressDigit(c);
        surface.PressToken(CommandTokenKind.Enter);
    }

    [Fact]
    public void ApplyPreset_WritesProgrammerForCurrentSelection_KeepsSelection_AndClosesSelectionCycle()
    {
        var (context, surface, presets, a, b) = BuildRig();
        var preset = new Preset { Class = AttributeClass.Intensity, Name = "Half" };
        preset.Values[ChannelType.Dimmer] = 128;
        context.Presets.Add(preset);

        SelectFixture(surface, a.Number);
        Assert.Contains(a, context.Selection.Items);

        presets.ApplyCommand.Execute(preset);

        // Preset values are written into PROGRAMMER, scoped to the fixtures that were selected.
        var dimmerIndex = a.AbsoluteIndex(a.FindChannel(ChannelType.Dimmer)!);
        Assert.True(context.Programmer.HasStoredValue(a.UniverseId, dimmerIndex, out var stored));
        Assert.Equal(128, stored);

        // Selection remains selected - Apply (like Capture All/HOME) never touches Selection itself.
        Assert.Contains(a, context.Selection.Items);

        // SelectionCycle closes - the next Fixture gesture replaces rather than accumulates.
        Assert.True(context.SelectionCycle.StartFreshOnNextSelection);
        SelectFixture(surface, b.Number);
        Assert.Equal(new[] { b }, context.Selection.Items);
    }

    [Fact]
    public void ApplyPreset_OnlyWritesProgrammerForFixturesThatWereActuallySelected()
    {
        var (context, surface, presets, a, b) = BuildRig();
        var preset = new Preset { Class = AttributeClass.Intensity, Name = "Half" };
        preset.Values[ChannelType.Dimmer] = 128;
        context.Presets.Add(preset);

        SelectFixture(surface, a.Number);
        presets.ApplyCommand.Execute(preset);

        var bDimmerIndex = b.AbsoluteIndex(b.FindChannel(ChannelType.Dimmer)!);
        Assert.False(context.Programmer.HasStoredValue(b.UniverseId, bDimmerIndex, out _));
    }

    [Fact]
    public void ApplyPreset_WithNoSelection_DoesNotCloseSelectionCycle_AndWritesNothing()
    {
        var (context, _, presets, a, _) = BuildRig();
        var preset = new Preset { Class = AttributeClass.Intensity, Name = "Half" };
        preset.Values[ChannelType.Dimmer] = 128;
        context.Presets.Add(preset);

        presets.ApplyCommand.Execute(preset);

        Assert.False(context.SelectionCycle.StartFreshOnNextSelection);
        var dimmerIndex = a.AbsoluteIndex(a.FindChannel(ChannelType.Dimmer)!);
        Assert.False(context.Programmer.HasStoredValue(a.UniverseId, dimmerIndex, out _));
    }
}

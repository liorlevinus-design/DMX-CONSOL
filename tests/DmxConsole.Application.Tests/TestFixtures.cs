using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Tests;

/// <summary>Shared helpers for building a small patched rig + a fresh ConsoleContext/Dispatcher.</summary>
internal static class TestFixtures
{
    public static FixtureProfile Dimmer1() => new()
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
    public static Patch BuildPatch(int count)
    {
        var patch = new Patch();
        var profile = Dimmer1();
        for (int i = 0; i < count; i++)
            patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1 + i, $"F{i + 1}"));
        return patch;
    }

    public static (ConsoleContext Context, CommandDispatcher Dispatcher, UndoRedoService UndoRedo) BuildConsole(int fixtureCount)
    {
        var patch = BuildPatch(fixtureCount);
        var engine = new DmxOutputEngine(patch); // never Started/Ticked here - these tests don't touch the Programmer
        var context = new ConsoleContext(patch, new Programmer(), new FixtureSelection(), new GroupManager(), engine, new Core.Presets.PresetLibrary(), new ExecutorBank());
        var undoRedo = new UndoRedoService(context);
        var dispatcher = new CommandDispatcher(context, undoRedo);
        return (context, dispatcher, undoRedo);
    }
}

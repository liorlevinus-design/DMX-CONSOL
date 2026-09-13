using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;

namespace DmxConsole.Application;

/// <summary>
/// The console's real operational state: what every Command reads and mutates. This is
/// deliberately NOT a place for conversational/NL context ("them", "10 more") - that kind
/// of short-term memory belongs to whatever interprets natural language into commands,
/// not to the console itself. Anything here must also make sense for a plain UI, a macro,
/// or a MIDI/OSC trigger with no notion of a conversation at all.
/// </summary>
public sealed class ConsoleContext
{
    public Patch Patch { get; }
    public Programmer Programmer { get; }
    public FixtureSelection Selection { get; }
    public GroupManager Groups { get; }

    // Future steps add: CueLists, PresetLibrary, EffectsEngine, etc. - not part of Step C0.

    public ConsoleContext(Patch patch, Programmer programmer, FixtureSelection selection, GroupManager groups)
    {
        Patch = patch;
        Programmer = programmer;
        Selection = selection;
        Groups = groups;
    }
}

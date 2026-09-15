using DmxConsole.Core.Engine;
using DmxConsole.Core.Effects;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;
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

    /// <summary>
    /// Shared operator selection-cycle state: selection survives an execution, while the next
    /// new selection knows whether to append to the current cycle or start a fresh one.
    /// </summary>
    public SelectionCycleState SelectionCycle { get; } = new();

    /// <summary>
    /// Read-only view of the engine's actual merged output - what a Relative adjustment
    /// treats as "the current value", since a Cue/Effect can be driving a channel with no
    /// Programmer override present at all. Narrowed to this one read-only method (not the
    /// full DmxOutputEngine) so Commands can never reach engine lifecycle/layer wiring.
    /// </summary>
    public IEffectiveOutputReader EffectiveOutput { get; }

    public PresetLibrary Presets { get; }

    /// <summary>The show's Executors - Step F. Playback Sources (CueLists today) are assigned
    /// onto these handles; Commands/Actions never touch a CueList's engine registration
    /// directly, only through the Executor wrapping it.</summary>
    public ExecutorBank Executors { get; }
    public EffectBank Effects { get; }

    public ConsoleContext(Patch patch, Programmer programmer, FixtureSelection selection, GroupManager groups,
        IEffectiveOutputReader effectiveOutput, PresetLibrary presets, ExecutorBank executors,
        EffectBank? effects = null)
    {
        Patch = patch;
        Programmer = programmer;
        Selection = selection;
        Groups = groups;
        EffectiveOutput = effectiveOutput;
        Presets = presets;
        Executors = executors;
        Effects = effects ?? new EffectBank();
    }
}

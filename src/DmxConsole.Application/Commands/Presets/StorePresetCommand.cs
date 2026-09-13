using DmxConsole.Core;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Presets;

namespace DmxConsole.Application.Commands.Presets;

/// <summary>
/// Records (or updates) a Preset from the target fixtures' current Programmer state - the
/// same source RecordCue already uses (HasStoredValue, falling back to the fixture's channel
/// DefaultValue), because a Preset captures "the look I built", not "what's currently on
/// air" (that's the EffectiveOutput concern AdjustIntensityCommand's Relative math uses,
/// a different question). Recording merges: only the ChannelTypes actually present among
/// the targets for this AttributeClass are added/overwritten - other entries already in an
/// existing Preset (e.g. from a different fixture type recorded earlier) are left alone.
/// </summary>
public sealed class StorePresetCommand : IConsoleCommand
{
    private readonly PresetLibrary _library;
    private readonly IReadOnlyList<PatchedFixture> _targets;
    private readonly AttributeClass _class;
    private readonly string _name;
    private readonly int _number;
    private readonly Preset? _existingPreset;

    private Preset? _target;
    private bool _wasNewlyCreated;
    private Dictionary<ChannelType, byte>? _previousValues;

    public StorePresetCommand(PresetLibrary library, IReadOnlyList<PatchedFixture> targets, AttributeClass attributeClass,
        string name, int number, Preset? existingPreset = null)
    {
        _library = library;
        _targets = targets;
        _class = attributeClass;
        _name = name;
        _number = number;
        _existingPreset = existingPreset;
    }

    public CommandResult Execute(ConsoleContext context)
    {
        var candidateValues = new Dictionary<ChannelType, byte>();
        var affected = new List<PatchedFixture>();

        foreach (var fixture in _targets)
        {
            bool touchedAny = false;
            foreach (var channel in fixture.ChannelsForAttribute(_class))
            {
                byte value = context.Programmer.HasStoredValue(fixture.UniverseId, fixture.AbsoluteIndex(channel), out var stored)
                    ? stored
                    : channel.DefaultValue;
                candidateValues[channel.Type] = value;
                touchedAny = true;
            }

            if (touchedAny) affected.Add(fixture);
        }

        if (candidateValues.Count == 0)
        {
            return CommandResult.Failed(ConsoleActionType.StorePreset,
                $"None of the selected fixtures have {_class} channels - nothing to store.");
        }

        _wasNewlyCreated = _existingPreset is null;
        var preset = _existingPreset ?? new Preset { Class = _class, Name = _name, Number = _number };
        _previousValues = new Dictionary<ChannelType, byte>(preset.Values); // snapshot before merge, for Undo

        foreach (var (channelType, value) in candidateValues) preset.Values[channelType] = value;
        if (_wasNewlyCreated) _library.Add(preset);
        _target = preset;

        return new CommandResult
        {
            ActionType = ConsoleActionType.StorePreset,
            AffectedFixtures = affected,
            AffectedAttributes = new[] { _class },
            Preset = preset,
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_target is null) return; // Execute failed or was never called

        if (_wasNewlyCreated)
        {
            _library.Remove(_target);
        }
        else
        {
            _target.Values.Clear();
            foreach (var (channelType, value) in _previousValues!) _target.Values[channelType] = value;
        }
    }
}

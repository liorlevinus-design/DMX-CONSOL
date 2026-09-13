using System.Collections.ObjectModel;
using DmxConsole.Core.Engine;

namespace DmxConsole.Core.Presets;

/// <summary>
/// The show's saved Presets. Numbering is scoped per AttributeClass - like real consoles'
/// separate Intensity/Position/Color/Beam pools, "Position preset 4" and "Color preset 4"
/// are unrelated entries and never clash.
/// </summary>
public sealed class PresetLibrary : IPresetResolver
{
    public ObservableCollection<Preset> Presets { get; } = new();

    public IEnumerable<Preset> ForClass(AttributeClass cls) => Presets.Where(p => p.Class == cls);

    public Preset? FindByNumber(AttributeClass cls, int number) =>
        Presets.FirstOrDefault(p => p.Class == cls && p.Number == number);

    /// <summary>Adds a preset, assigning the next free number within its Class unless one was already set (and isn't taken).</summary>
    public void Add(Preset preset)
    {
        if (preset.Number <= 0 || FindByNumber(preset.Class, preset.Number) is not null)
            preset.Number = NextFreeNumber(preset.Class);

        Presets.Add(preset);
    }

    public bool Remove(Preset preset) => Presets.Remove(preset);

    public int NextFreeNumber(AttributeClass cls)
    {
        var used = ForClass(cls).ToList();
        return used.Count == 0 ? 1 : used.Max(p => p.Number) + 1;
    }

    /// <summary>Resolves a CueValue.PresetRef at playback time: finds the preset by Id (searching all
    /// classes - a Cue only ever stores the Id, not the class) and looks up the channel type in its
    /// Values. Returns false - never throws - if the preset was deleted or doesn't contain this
    /// ChannelType, the same "silently doesn't contribute" rule ApplyPresetCommand uses (Step D).</summary>
    public bool TryResolve(Guid presetId, ChannelType channelType, out byte value)
    {
        var preset = Presets.FirstOrDefault(p => p.Id == presetId);
        if (preset is not null && preset.Values.TryGetValue(channelType, out var found))
        {
            value = found;
            return true;
        }

        value = 0;
        return false;
    }
}

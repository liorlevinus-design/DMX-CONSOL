using System.Collections.ObjectModel;

namespace DmxConsole.Core.Presets;

/// <summary>
/// The show's saved Presets. Numbering is scoped per AttributeClass - like real consoles'
/// separate Intensity/Position/Color/Beam pools, "Position preset 4" and "Color preset 4"
/// are unrelated entries and never clash.
/// </summary>
public sealed class PresetLibrary
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
}

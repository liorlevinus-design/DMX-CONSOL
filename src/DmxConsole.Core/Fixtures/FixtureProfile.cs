namespace DmxConsole.Core.Fixtures;

/// <summary>
/// The definition of a fixture "type" (make/model), independent of where it is patched.
/// A profile can expose several modes (personalities) with different footprints.
/// </summary>
public sealed class FixtureProfile
{
    /// <summary>Stable identifier, e.g. "generic-rgb-3ch" - used to reference this profile from a patch file.</summary>
    public string Id { get; init; } = string.Empty;

    public string Manufacturer { get; init; } = "Generic";

    public string Model { get; init; } = string.Empty;

    public IReadOnlyList<FixtureMode> Modes { get; init; } = Array.Empty<FixtureMode>();

    public FixtureMode GetMode(string name) =>
        Modes.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Fixture '{Manufacturer} {Model}' has no mode named '{name}'.");

    public string DisplayName => $"{Manufacturer} {Model}";
}

using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Presets;

/// <summary>
/// Answers "which of the six AttributeClass families does the PROGRAMMER currently touch, for
/// this set of fixtures" - the one shared place that question is computed, used by the Command
/// Surface's STORE grammar to decide whether "STORE PRESET n" is unambiguous (exactly one touched
/// family - store directly) or needs the operator to choose (more than one). A family counts as
/// "touched" only if at least one target fixture has a channel of that class with a value the
/// Programmer is actually holding (Programmer.HasStoredValue) - matching exactly what
/// StorePresetCommand itself would capture for that family, so "touched" and "what Store would
/// actually record" never disagree.
/// </summary>
public static class StorePresetFamilyDetector
{
    private static readonly AttributeClass[] AllFamilies =
    {
        AttributeClass.Intensity, AttributeClass.Position, AttributeClass.Color,
        AttributeClass.Beam, AttributeClass.Image, AttributeClass.Shape,
    };

    public static IReadOnlyList<AttributeClass> TouchedFamilies(IReadOnlyList<PatchedFixture> targets, DmxConsole.Core.Engine.Programmer programmer)
    {
        var touched = new List<AttributeClass>();
        foreach (var family in AllFamilies)
        {
            bool any = targets.Any(fixture => fixture.ChannelsForAttribute(family)
                .Any(channel => programmer.HasStoredValue(fixture.UniverseId, fixture.AbsoluteIndex(channel), out _)));
            if (any) touched.Add(family);
        }
        return touched;
    }
}

using DmxConsole.Core.Fixtures;

namespace DmxConsole.Core.Selection;

/// <summary>
/// The one shared primitive for turning (ordered Fixture Selection) x (ordered logical Parameter
/// Selection) into concrete write targets (CLAUDE.md §16, PSEL-1/PSEL-2/PSEL-5). Prior slices each
/// re-derived this compatibility check locally - <c>ParameterPickerViewModel.FixtureSupportsParameter</c>,
/// <c>EncoderDrawerViewModel.BuildSlot</c>, <see cref="ParameterSelection"/>'s own Normalize - this
/// type is the first place it is centralized so a new caller (Parameter AT) doesn't need to grow a
/// fourth copy. It is a pure query: it never mutates the Programmer, Selection, or Parameter
/// Selection, and never throws for an incompatible pair - PSEL-2 requires silent-but-reported
/// skipping, never a hard failure.
///
/// Ordering: fixtures are walked in their given (ordered Fixture Selection) order, and for each
/// fixture, parameters are walked in their given (ordered Parameter Selection) order - so
/// <see cref="Result.Targets"/> preserves both orders exactly (PSEL-1), which is what lets a caller
/// distribute a value once per FIXTURE POSITION and then fan that same value out to every
/// compatible parameter for that fixture, never a flattened (fixture, parameter) cross-product
/// distribution.
///
/// PSEL-5: a parameter is expected to already be the normalized/logical (primary/coarse)
/// <see cref="ChannelType"/> - the same normalization <see cref="ParameterSelection"/> already
/// applies on Select. This resolver does not re-normalize its input, but it does look at every
/// semantic component (<see cref="ChannelTypeExtensions.SemanticComponents"/>) of a parameter when
/// searching for the fixture's actual channel, exactly like
/// <c>ParameterPickerViewModel.FixtureSupportsParameter</c> already does, so a fixture profile that
/// unusually only exposes the fine half of a pair is still found rather than false-negatived.
/// </summary>
public static class ParameterTargetResolver
{
    /// <summary>One resolved (fixture, logical parameter) write target, plus the fixture's actual
    /// <see cref="FixtureChannel"/> to write through (never necessarily the fine component - the
    /// first present semantic component, matching existing precedent).</summary>
    public readonly record struct Target(PatchedFixture Fixture, ChannelType Parameter, FixtureChannel Channel);

    /// <summary>A (fixture, logical parameter) pair that was requested but is not supported by that
    /// fixture's profile - reported for operator feedback, never a failure.</summary>
    public readonly record struct SkippedTarget(PatchedFixture Fixture, ChannelType Parameter);

    public sealed record Result(IReadOnlyList<Target> Targets, IReadOnlyList<SkippedTarget> Skipped);

    /// <summary>Resolves every compatible (fixture, parameter) pair, in fixture-then-parameter
    /// order, deduplicating identical pairs (e.g. a parameter token repeated by mistake) rather
    /// than emitting the same target twice.</summary>
    public static Result Resolve(IReadOnlyList<PatchedFixture> fixtures, IReadOnlyList<ChannelType> parameters)
    {
        var targets = new List<Target>();
        var skipped = new List<SkippedTarget>();
        var seen = new HashSet<(Guid FixtureId, ChannelType Parameter)>();

        foreach (var fixture in fixtures)
        {
            foreach (var parameter in parameters)
            {
                if (!seen.Add((fixture.Id, parameter))) continue;

                var channel = FindComponentChannel(fixture, parameter);
                if (channel is null) skipped.Add(new SkippedTarget(fixture, parameter));
                else targets.Add(new Target(fixture, parameter, channel));
            }
        }

        return new Result(targets, skipped);
    }

    /// <summary>True if any semantic component of <paramref name="parameter"/> exists on
    /// <paramref name="fixture"/>'s profile - the exact compatibility check
    /// <c>ParameterPickerViewModel.FixtureSupportsParameter</c> already established.</summary>
    public static bool FixtureSupportsParameter(PatchedFixture fixture, ChannelType parameter) =>
        FindComponentChannel(fixture, parameter) is not null;

    private static FixtureChannel? FindComponentChannel(PatchedFixture fixture, ChannelType parameter)
    {
        foreach (var component in parameter.SemanticComponents())
        {
            var channel = fixture.FindChannel(component);
            if (channel is not null) return channel;
        }
        return null;
    }
}

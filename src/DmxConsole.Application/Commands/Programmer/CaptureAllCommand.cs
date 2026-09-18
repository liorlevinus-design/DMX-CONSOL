using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// CAPTURE ALL (docs/COMMAND_SURFACE_KEY_SPEC.md §8): "Effective Live State -> Editor". For
/// every patched fixture's every channel, if some output layer is CURRENTLY actively
/// contributing to it (<see cref="DmxConsole.Core.Engine.IEffectiveOutputReader.GetOwner"/> is
/// non-null - the exact same "LiveOnStage" ownership test <see cref="DmxConsole.Application.Live.LiveChannelState"/>
/// already uses, not a new provenance concept), the channel's current effective (fully merged,
/// post-fade/post-effect) byte is written into the Programmer as a new Editor value.
///
/// This is deliberately NOT scoped to the current Selection (§8's own text: "capture the current
/// effective LIVE output", never "capture the current selection's output") and NOT gated on
/// Intensity being above zero (§4: a fixture can be meaningfully live via Position/Color/Beam/
/// Image/Shape alone, at Intensity 0) - unlike CueList's AllParamsForSelected/AllParamsIfActive
/// Store filters (DmxConsole.Core/Engine/CueList.cs), which both scope to Selection AND gate on
/// Intensity>0 for a different purpose (deciding what belongs in a stored Cue). CAPTURE ALL's own
/// gate is ownership, not intensity or selection - a channel with no active owner at all (a bare,
/// untouched default) is never captured, so this never dumps every patched fixture's defaults
/// into the Editor.
///
/// Reuses ProgrammerChannelCommandBase's existing snapshot/undo machinery unchanged (the same
/// "capture before, ApplyToChannel decides per-channel, restore verbatim on Undo" pattern every
/// other Programmer-mutating command already uses) - no second merge path, no duplicated
/// playback math, no new provenance system.
/// </summary>
public sealed class CaptureAllCommand : ProgrammerChannelCommandBase
{
    public CaptureAllCommand(IReadOnlyList<PatchedFixture> allPatchedFixtures)
        : base(allPatchedFixtures, attributeFilter: null)
    {
    }

    protected override ConsoleActionType ActionType => ConsoleActionType.CaptureAll;

    protected override bool ApplyToChannel(ConsoleContext context, PatchedFixture fixture, FixtureChannel channel)
    {
        int idx = fixture.AbsoluteIndex(channel);

        // The ownership gate: nothing is actively contributing to this channel right now (a bare,
        // untouched default) - never captured. This is the "what counts as active" decision
        // (§4), made via the existing ownership infrastructure, not a new heuristic.
        if (context.EffectiveOutput.GetOwner(fixture.UniverseId, idx) is null) return false;

        byte effectiveValue = context.EffectiveOutput.GetEffectiveValue(fixture.UniverseId, idx);
        context.Programmer.SetChannel(fixture.UniverseId, idx, effectiveValue);
        return true;
    }

    public override IConsoleCommand CreateFreshInstance() => new CaptureAllCommand(Targets);
}

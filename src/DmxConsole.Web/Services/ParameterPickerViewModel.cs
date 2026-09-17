using DmxConsole.Application;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Web.Services;

/// <summary>One semantic parameter as the picker presents it. A coarse/fine pair (Pan+PanFine,
/// Tilt+TiltFine) is folded into ONE option - Type is always the "coarse" representative from
/// ChannelTypeExtensions.SemanticComponents (docs/COMMAND_SURFACE_KEY_SPEC.md PARAMETER PICKER
/// §7 - "Pan coarse/fine remain one semantic PAN"). Common = every selected fixture supports it;
/// Partial = only some do - the two are mutually exclusive by construction (see
/// ParameterPickerViewModel.Options).</summary>
public sealed record ParameterOption(ChannelType Type, string Label, bool Common, bool Partial);

/// <summary>
/// Command Surface's contextual parameter picker (docs/COMMAND_SURFACE_KEY_SPEC.md PARAMETER
/// PICKER slice) - closes the gap between the parameter-level grammar/API that already exists
/// (PAN RELEASE, ZOOM RELEASE via CommandSurfaceViewModel.PressParameter/CommandComposer's own
/// "&lt;Parameter&gt; RELEASE" rule) and having any visible way to reach it.
///
/// Not a new taxonomy, not a new command: this is a read-only query over the CURRENT Selection's
/// real FixtureChannel data, grouped by the SAME AttributeClass six-family model everything else
/// in this console already uses (ChannelTypeExtensions.ToAttributeClass), and its only write path
/// is CommandSurfaceViewModel.PressParameter - the exact same call a future Encoder Drawer/EXAM/
/// hardware entry point would also make, never a duplicated dispatch.
///
/// "Which family is armed" is read directly from CommandSurfaceViewModel's own composition state
/// (a lone family token pushed and waiting for HOME/RELEASE/PRESET - CommandComposer.FamilyFor is
/// the SAME mapping the composer's own grammar already uses) rather than tracked as a second,
/// parallel piece of state here - so the picker can never disagree with what the command line
/// itself is showing.
/// </summary>
public sealed class ParameterPickerViewModel
{
    private readonly ConsoleContext _context;
    private readonly CommandSurfaceViewModel _commandSurface;

    public ParameterPickerViewModel(ConsoleContext context, CommandSurfaceViewModel commandSurface)
    {
        _context = context;
        _commandSurface = commandSurface;
    }

    /// <summary>The family currently armed in the Command Surface's own command line - a lone
    /// family token pushed and waiting for HOME/RELEASE/PRESET. Null whenever that specific state
    /// doesn't hold (idle line, mid-composition on something else, a Parameter already chosen,
    /// ...) - the picker requires a family first (docs/... PARAMETER PICKER §5's "Option A"),
    /// the simplest deterministic behavior that maps directly onto grammar state already exposed,
    /// no new CommandSurfaceViewModel field needed.</summary>
    public AttributeClass? ArmedFamily =>
        _commandSurface.Current.Tokens.Count == 1
            ? CommandComposer.FamilyFor(_commandSurface.Current.Tokens[0].Kind)
            : null;

    /// <summary>Shown only when a family is armed AND the current Selection resolves at least one
    /// parameter for it - an irrelevant picker is never rendered floating with nothing to offer.</summary>
    public bool IsVisible => ArmedFamily is not null && Options.Count > 0;

    /// <summary>True only when a family is armed, fixtures ARE selected, but genuinely none of
    /// them has a channel in that family - the honest "No &lt;Family&gt; parameters" state (§8),
    /// rendered instead of the picker silently disappearing as if nothing were pressed.</summary>
    public bool IsEmptyForArmedFamily => ArmedFamily is not null && _context.Selection.Items.Count > 0 && Options.Count == 0;

    /// <summary>
    /// Every semantic parameter present on at least one selected fixture within the armed family,
    /// coarse/fine folded to one option each (§7), in ascending ChannelType declaration order.
    /// Common = every selected fixture supports it; Partial = only some do (§6) - never both.
    /// A selection of zero fixtures always yields no options (§8: no fixture selected means no
    /// parameter to show, never a fake/global list).
    /// </summary>
    public IReadOnlyList<ParameterOption> Options
    {
        get
        {
            if (ArmedFamily is not { } family) return Array.Empty<ParameterOption>();

            var selected = _context.Selection.Items;
            if (selected.Count == 0) return Array.Empty<ParameterOption>();

            var primaries = selected
                .SelectMany(f => f.Mode.Channels.Select(c => c.Type))
                .Where(t => t.ToAttributeClass() == family)
                .Select(t => t.SemanticComponents()[0])
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            var options = new List<ParameterOption>(primaries.Count);
            foreach (var type in primaries)
            {
                int supportingCount = selected.Count(f => FixtureSupportsParameter(f, type));
                bool common = supportingCount == selected.Count;
                options.Add(new ParameterOption(type, EncoderDrawerViewModel.ParameterLabel(type), common, !common));
            }
            return options;
        }
    }

    /// <summary>A fixture "supports" a semantic parameter if it has ANY of that parameter's
    /// components (e.g. either Pan or PanFine counts as "has Pan") - consistent with
    /// ReleaseParameterCommand's own atomic coarse+fine release, never a stricter/looser rule
    /// invented separately here.</summary>
    private static bool FixtureSupportsParameter(PatchedFixture fixture, ChannelType type) =>
        type.SemanticComponents().Any(component => fixture.FindChannel(component) is not null);

    /// <summary>The ONLY write path - routes into the exact same PressParameter any other entry
    /// point (present or future) would call, never a duplicated dispatch.</summary>
    public void SelectParameter(ChannelType type) => _commandSurface.PressParameter(type);
}

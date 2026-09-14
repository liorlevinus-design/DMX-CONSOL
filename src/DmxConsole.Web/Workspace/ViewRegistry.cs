namespace DmxConsole.Web.Workspaces;

/// <summary>Static metadata for one hostable View kind - its display name and the Razor
/// component type PaneHost renders via DynamicComponent. AllowMultipleInstances is false for a
/// view that only ever makes sense as a singleton (reserved for future kinds; every kind
/// registered in this milestone allows multiple instances).</summary>
public sealed record ViewDescriptor(ViewKind Kind, string DisplayName, Type ComponentType, bool AllowMultipleInstances = true);

/// <summary>The set of View kinds this build knows how to host. A narrow, explicit registry
/// (not reflection-based scanning) so it's always obvious what's available and nothing shows up
/// by accident.</summary>
public sealed class ViewRegistry
{
    private readonly Dictionary<ViewKind, ViewDescriptor> _descriptors = new();

    public void Register(ViewDescriptor descriptor) => _descriptors[descriptor.Kind] = descriptor;

    /// <summary>Never throws for an unregistered kind - graceful lookup, consistent with this
    /// codebase's "no silent guessing, no unhandled crash" convention (matches
    /// PresetLibrary.TryResolve, Patch.TryGetChannelType, etc.).</summary>
    public bool TryGet(ViewKind kind, out ViewDescriptor descriptor) => _descriptors.TryGetValue(kind, out descriptor!);

    public IReadOnlyCollection<ViewDescriptor> All => _descriptors.Values;
}

namespace DmxConsole.Core.Engine;

/// <summary>
/// Narrow write-capability for "make sure this Universe exists and is tracked for merge/output" -
/// deliberately separate from <see cref="IEffectiveOutputReader"/> (a pure reader) and deliberately
/// NOT the engine's lifecycle/layer-registration surface (Start/Stop/AddLayer/RemoveLayer stay
/// off-limits to Commands, per IEffectiveOutputReader's own doc comment). Allocating a Universe is
/// a narrow, idempotent data-level operation - the same thing DmxOutputEngine already does
/// implicitly today whenever a fixture is patched (see DmxOutputEngine.RebuildChannelMap) - this
/// interface just gives DMX DIRECT ADDRESSING (or any future caller needing the same thing) an
/// explicit way to trigger it WITHOUT a fixture ever being patched.
/// </summary>
public interface IUniverseAllocator
{
    /// <summary>Ensures a Universe with this id exists and is tracked by the engine for merge/
    /// output, even with zero patched fixtures. Idempotent - calling it again for an id that
    /// already exists is a harmless no-op.</summary>
    void EnsureUniverse(int universeId);
}

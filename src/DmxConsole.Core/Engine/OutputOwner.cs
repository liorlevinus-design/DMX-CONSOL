namespace DmxConsole.Core.Engine;

public enum OwnerKind { Programmer, Executor, Effect, Other }

/// <summary>Structured, presentation-independent identity of whichever layer currently owns a
/// channel's winning value. Id is a stable identity, never derived from a mutable, renamable,
/// collidable Name - two Executors named identically still get distinct Ids, and renumbering or
/// renaming an Executor never changes its Id.</summary>
public sealed record OutputOwner(string Id, string DisplayName, OwnerKind Kind, Guid? ExecutorId, int? ExecutorNumber);

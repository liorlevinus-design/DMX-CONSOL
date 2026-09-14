namespace DmxConsole.Application;

public enum UndoRisk { Safe, Destructive }

/// <summary>One way Undo-ing a command could go. A command's PrepareUndo() can offer more than
/// one of these in the future (e.g. a Store that mutates Programmer as a side effect could offer
/// a real non-destructive "restore editor only" alternative alongside the destructive full
/// undo) - the shape is a list precisely so that becomes additive, not a rewrite.</summary>
public sealed record UndoOption(string Id, string Description, UndoRisk Risk);

/// <summary>What Undo-ing the top of the stack would offer.</summary>
public sealed record UndoProposal(string Description, IReadOnlyList<UndoOption> Options)
{
    public static UndoProposal SingleSafe(string description) =>
        new(description, new[] { new UndoOption("undo", description, UndoRisk.Safe) });
}

/// <summary>Opt-in - a command that doesn't implement this is treated as a single implicit Safe
/// option (today's exact behavior for every pre-Step-F command: none of them delete a freshly
/// created persistent object on Undo, they only restore-or-toggle previous field state).</summary>
public interface IHasUndoRisk
{
    UndoProposal PrepareUndo();
}

public readonly record struct UndoOutcome(bool Performed, UndoProposal? Proposal);

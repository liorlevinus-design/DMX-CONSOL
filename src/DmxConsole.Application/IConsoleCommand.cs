namespace DmxConsole.Application;

/// <summary>
/// A single, semantic, reversible console action - the only unit of mutation that ever
/// touches a <see cref="ConsoleContext"/>. Every client (Blazor UI, macros, MIDI/OSC, and
/// eventually a Natural Language layer) speaks to the console exclusively by constructing
/// these and handing them to a <see cref="CommandDispatcher"/> - never by reaching into
/// Core objects directly.
/// </summary>
public interface IConsoleCommand
{
    /// <summary>
    /// Applies the action and returns what happened. Must capture whatever it needs
    /// internally to Undo. A command must either fully apply and return Success, or leave
    /// state completely untouched and return a failed CommandResult (e.g. via
    /// <see cref="CommandResult.Failed"/>) - never partially mutate and then fail, since
    /// <see cref="Commands.CompositeCommand"/> only rolls back commands that reported success.
    /// </summary>
    CommandResult Execute(ConsoleContext context);

    /// <summary>Reverts exactly what the most recent Execute did.</summary>
    void Undo(ConsoleContext context);
}

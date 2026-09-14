using System.Collections.ObjectModel;

namespace DmxConsole.Core.Engine;

/// <summary>
/// The show's Executors - plain collection, parallel to GroupManager/PresetLibrary. Supports far
/// more Executors than physical faders visible at once (every reference console does); Number is
/// deliberately not tied to any physical fader slot, leaving room for future Paging without
/// renumbering anything. Does not itself talk to DmxOutputEngine - wiring an Executor into the
/// engine happens at the same call site that already does this today for CueList (MainViewModel),
/// keeping that coupling explicit and in one place.
/// </summary>
public sealed class ExecutorBank
{
    private readonly IChannelTypeLookup? _channelTypeLookup;

    public ExecutorBank(IChannelTypeLookup? channelTypeLookup = null)
    {
        _channelTypeLookup = channelTypeLookup;
    }

    public ObservableCollection<Executor> Executors { get; } = new();

    public Executor? FindByNumber(int number) => Executors.FirstOrDefault(e => e.Number == number);

    /// <summary>Creates a new Executor, assigning the next free Number unless one was already
    /// given (and isn't taken) - same NextFreeNumber pattern as Patch/PresetLibrary.</summary>
    public Executor Add(int? number = null)
    {
        int assigned = number is > 0 && FindByNumber(number.Value) is null
            ? number.Value
            : NextFreeNumber();
        var executor = new Executor(assigned, _channelTypeLookup);
        Executors.Add(executor);
        return executor;
    }

    public bool Remove(Executor executor) => Executors.Remove(executor);

    private int NextFreeNumber() => Executors.Count == 0 ? 1 : Executors.Max(e => e.Number) + 1;
}

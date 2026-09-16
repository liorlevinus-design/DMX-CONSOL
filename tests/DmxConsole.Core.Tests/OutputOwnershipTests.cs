using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using DmxConsole.Core.Selection;
using Xunit;

namespace DmxConsole.Core.Tests;

public class OutputOwnershipTests
{
    private sealed class StubEffectiveOutputReader : IEffectiveOutputReader
    {
        public byte GetEffectiveValue(int universeId, int channelIndex) => 0;
        public OutputOwner? GetOwner(int universeId, int channelIndex) => null;
    }

    private static readonly FixtureSelection EmptySelection = new();
    private static readonly IEffectiveOutputReader Stub = new StubEffectiveOutputReader();

    private static Cue Record(CueList cueList, Patch patch, Programmer programmer, string name, double number) =>
        cueList.RecordCue(patch, programmer, EmptySelection, Stub, name, number,
            new CueStoreOptions(new CueTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage));

    private static FixtureProfile Dimmer1() => new()
    {
        Id = "test-dimmer",
        Manufacturer = "Test",
        Model = "Dimmer",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "1ch",
                Channels = new[] { new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0 } },
            },
        },
    };

    private static (Patch Patch, DmxOutputEngine Engine) BuildRig()
    {
        var patch = new Patch();
        var profile = Dimmer1();
        patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1));
        return (patch, new DmxOutputEngine(patch));
    }

    [Fact]
    public void GetOwner_ProgrammerDrivenChannel_ReportsProgrammerKind()
    {
        var (_, engine) = BuildRig();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 100);
        engine.AddLayer(programmer);

        engine.Tick();

        var owner = engine.GetOwner(0, 0);
        Assert.NotNull(owner);
        Assert.Equal(OwnerKind.Programmer, owner!.Kind);
        Assert.Equal("programmer", owner.Id);
    }

    [Fact]
    public void GetOwner_ExecutorDrivenChannel_ReportsExecutorIdAndNumber()
    {
        var (patch, engine) = BuildRig();
        var programmer = new Programmer();
        programmer.SetChannel(0, 0, 50);
        var cueList = new CueList();
        Record(cueList, patch, programmer, "Cue 1", 1);
        cueList.Go();

        var executor = new Executor(7, patch);
        executor.Assign(cueList);
        engine.AddLayer(executor);

        engine.Tick();

        var owner = engine.GetOwner(0, 0);
        Assert.NotNull(owner);
        Assert.Equal(OwnerKind.Executor, owner!.Kind);
        Assert.Equal(executor.Id, owner.ExecutorId);
        Assert.Equal(7, owner.ExecutorNumber);
    }

    [Fact]
    public void GetOwner_UntouchedChannel_ReturnsNull()
    {
        var (_, engine) = BuildRig();
        engine.Tick();

        Assert.Null(engine.GetOwner(0, 0));
    }

    [Fact]
    public void GetOwner_TwoExecutorsWithSameName_AreStillDistinguishable()
    {
        var (patch, engine) = BuildRig();

        var programmerA = new Programmer();
        programmerA.SetChannel(0, 0, 10);
        var cueListA = new CueList();
        Record(cueListA, patch, programmerA, "Cue A", 1);
        cueListA.Go();
        var executorA = new Executor(1, patch) { Name = "Front" };
        executorA.Assign(cueListA);

        var programmerB = new Programmer();
        programmerB.SetChannel(0, 0, 90);
        var cueListB = new CueList();
        Record(cueListB, patch, programmerB, "Cue B", 1);
        cueListB.Go();
        var executorB = new Executor(2, patch) { Name = "Front", Priority = 300 }; // same Name, higher Priority - B wins the merge
        executorB.Assign(cueListB);

        engine.AddLayer(executorA);
        engine.AddLayer(executorB);
        engine.Tick();

        var owner = engine.GetOwner(0, 0);
        Assert.NotNull(owner);
        Assert.Equal("Front", owner!.DisplayName);
        Assert.Equal(executorB.Id, owner.ExecutorId); // identity resolves to B specifically, not just "some Executor named Front"
        Assert.NotEqual(executorA.Id, owner.ExecutorId);
    }
}

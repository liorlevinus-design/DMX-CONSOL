using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;
using Xunit;

namespace DmxConsole.Core.Tests;

public class DmxOutputEngineTests
{
    private static FixtureProfile DimmerAndRgb() => new()
    {
        Id = "test-dimmer-rgb",
        Manufacturer = "Test",
        Model = "DimmerRGB",
        Modes = new[]
        {
            new FixtureMode
            {
                Name = "4ch",
                Channels = new[]
                {
                    new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 0 },
                    new FixtureChannel { Name = "Red", Type = ChannelType.ColorRed, Offset = 1, DefaultValue = 10 },
                    new FixtureChannel { Name = "Green", Type = ChannelType.ColorGreen, Offset = 2 },
                    new FixtureChannel { Name = "Blue", Type = ChannelType.ColorBlue, Offset = 3 },
                },
            },
        },
    };

    [Fact]
    public void Tick_WithNoLayers_OutputsFixtureDefaults()
    {
        var patch = new Patch();
        var profile = DimmerAndRgb();
        patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1));

        var engine = new DmxOutputEngine(patch);
        byte[]? published = null;
        engine.UniverseOutputReady += (_, data) => published = data;

        engine.Tick();

        Assert.NotNull(published);
        Assert.Equal(0, published![0]);  // Dimmer default
        Assert.Equal(10, published[1]);  // Red default
    }

    [Fact]
    public void Tick_ProgrammerOverride_WinsOverDefault()
    {
        var patch = new Patch();
        var profile = DimmerAndRgb();
        patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1));

        var engine = new DmxOutputEngine(patch);
        var programmer = new Programmer();
        engine.AddLayer(programmer);

        programmer.SetChannel(universeId: 0, channelIndex: 0, value: 255); // Dimmer (HTP)
        programmer.SetChannel(universeId: 0, channelIndex: 1, value: 200); // Red (LTP)

        byte[]? published = null;
        engine.UniverseOutputReady += (_, data) => published = data;
        engine.Tick();

        Assert.Equal(255, published![0]);
        Assert.Equal(200, published[1]);
    }

    [Fact]
    public void Tick_HtpChannel_TakesMaxOfDefaultAndLayer()
    {
        var patch = new Patch();
        var profile = DimmerAndRgb();
        patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1));

        var engine = new DmxOutputEngine(patch);
        var programmer = new Programmer();
        engine.AddLayer(programmer);

        // Dimmer default is 0, programmer sets a lower non-zero value - HTP should still pick the higher one.
        programmer.SetChannel(universeId: 0, channelIndex: 0, value: 5);

        byte[]? published = null;
        engine.UniverseOutputReady += (_, data) => published = data;
        engine.Tick();

        Assert.Equal(5, published![0]); // max(default=0, programmer=5)
    }

    [Fact]
    public void Tick_PanTiltCalibration_InvertsAndClampsOutput()
    {
        var profile = new FixtureProfile
        {
            Id = "test-mover",
            Manufacturer = "Test",
            Model = "Mover",
            Modes = new[]
            {
                new FixtureMode
                {
                    Name = "2ch",
                    Channels = new[]
                    {
                        new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0 },
                        new FixtureChannel { Name = "Tilt", Type = ChannelType.Tilt, Offset = 1 },
                    },
                },
            },
        };

        var patch = new Patch();
        var fixture = new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1);
        fixture.Calibration.InvertPan = true;
        fixture.Calibration.TiltMax = 200;
        patch.Add(fixture);

        var engine = new DmxOutputEngine(patch);
        var programmer = new Programmer();
        engine.AddLayer(programmer);
        programmer.SetChannel(0, 0, 100); // Pan raw
        programmer.SetChannel(0, 1, 255); // Tilt raw, should clamp to 200

        byte[]? published = null;
        engine.UniverseOutputReady += (_, data) => published = data;
        engine.Tick();

        Assert.Equal(255 - 100, published![0]); // inverted
        Assert.Equal(200, published[1]);         // clamped
    }

    [Fact]
    public void ComputeUniverse_SamplesEachLayersContribution_ExactlyOncePerChannel()
    {
        var patch = new Patch();
        var profile = DimmerAndRgb();
        patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1));

        var engine = new DmxOutputEngine(patch);
        var spy = new CallCountingLayer();
        engine.AddLayer(spy);

        engine.Tick();

        // One universe of Universe.ChannelCount channels - the spy must be asked exactly once
        // per channel, never twice (Priority/HTP/LTP resolution must operate on a captured
        // snapshot, not re-invoke TryGetChannelValue).
        Assert.Equal(Universe.ChannelCount, spy.TotalCalls);
        Assert.All(spy.CallsPerChannel.Values, count => Assert.Equal(1, count));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualPriorityBaseAwareLayers_ObserveSameFrozenLowerTierBase(bool reverseOrder)
    {
        var patch = new Patch();
        var profile = DimmerAndRgb();
        patch.Add(new PatchedFixture(profile, profile.Modes[0], universeId: 0, startAddress: 1));

        var engine = new DmxOutputEngine(patch);
        var lower = new ConstantLayer("Lower", priority: 100, value: 40);
        var a = new BaseRecordingLayer("A", priority: 300, addedValue: 10);
        var b = new BaseRecordingLayer("B", priority: 300, addedValue: 20);
        engine.AddLayer(lower);
        engine.AddLayer(reverseOrder ? b : a);
        engine.AddLayer(reverseOrder ? a : b);

        engine.Tick();

        Assert.Equal((byte?)40, a.ObservedBase);
        Assert.Equal((byte?)40, b.ObservedBase);
    }

    /// <summary>Spy IOutputLayer that records how many times TryGetChannelValue was called for
    /// each channel - used to prove the merge loop samples each layer once per channel.</summary>
    private sealed class CallCountingLayer : IOutputLayer
    {
        public string Name => "Spy";
        public int Priority => 100;
        public bool IsActive => true;

        public int TotalCalls { get; private set; }
        public Dictionary<int, int> CallsPerChannel { get; } = new();

        public bool TryGetChannelValue(int universeId, int channelIndex, out byte value)
        {
            TotalCalls++;
            CallsPerChannel[channelIndex] = CallsPerChannel.GetValueOrDefault(channelIndex) + 1;
            value = 0;
            return false; // doesn't actually contribute - only measuring how often it's asked
        }
    }

    private sealed class ConstantLayer(string name, int priority, byte value) : IOutputLayer
    {
        public string Name => name;
        public int Priority => priority;
        public bool IsActive => true;

        public bool TryGetChannelValue(int universeId, int channelIndex, out byte contribution)
        {
            contribution = value;
            return channelIndex == 0;
        }
    }

    private sealed class BaseRecordingLayer(string name, int priority, byte addedValue)
        : IOutputLayer, IBaseAwareLayer
    {
        public string Name => name;
        public int Priority => priority;
        public bool IsActive => true;
        public byte? ObservedBase { get; private set; }

        public bool TryGetChannelValue(int universeId, int channelIndex, out byte value) =>
            TryGetChannelValue(universeId, channelIndex, 0, out value);

        public bool TryGetChannelValue(int universeId, int channelIndex, byte baseValue, out byte value)
        {
            if (channelIndex != 0)
            {
                value = 0;
                return false;
            }

            ObservedBase = baseValue;
            value = (byte)(baseValue + addedValue);
            return true;
        }
    }
}

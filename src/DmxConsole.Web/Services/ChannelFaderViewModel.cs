using CommunityToolkit.Mvvm.ComponentModel;
using DmxConsole.Core;
using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Web.Services;

/// <summary>
/// One channel of one patched fixture, as seen by a fader. Setting Value writes
/// straight into the Programmer layer, which DmxOutputEngine picks up on its next tick.
/// </summary>
public partial class ChannelFaderViewModel : ObservableObject
{
    private readonly Programmer _programmer;

    public PatchedFixture Fixture { get; }
    public FixtureChannel Channel { get; }

    public int UniverseId => Fixture.UniverseId;
    public int ChannelIndex => Fixture.AbsoluteIndex(Channel);

    public string FixtureName => Fixture.Name;
    public string ChannelLabel => Channel.Name;
    public string ChannelTypeName => Channel.Type.ToString();

    [ObservableProperty]
    private byte _value;

    public ChannelFaderViewModel(PatchedFixture fixture, FixtureChannel channel, Programmer programmer)
    {
        Fixture = fixture;
        Channel = channel;
        _programmer = programmer;
        _value = channel.DefaultValue;
    }

    partial void OnValueChanged(byte value)
    {
        _programmer.SetChannel(UniverseId, ChannelIndex, value);
    }
}

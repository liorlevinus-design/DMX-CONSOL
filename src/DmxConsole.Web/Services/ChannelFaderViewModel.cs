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

    /// <summary>Guards the Programmer write-back in OnValueChanged during RefreshFromProgrammer -
    /// see that method's own doc comment for why this must exist.</summary>
    private bool _suppressProgrammerWriteback;

    partial void OnValueChanged(byte value)
    {
        if (_suppressProgrammerWriteback) return;
        _programmer.SetChannel(UniverseId, ChannelIndex, value);
    }

    /// <summary>Resyncs the displayed value FROM the Programmer's own current state, without
    /// writing back to it - unlike setting <see cref="Value"/> directly (reserved for genuine
    /// user-driven fader drags, where writing to the Programmer is exactly the point). Any "make
    /// the UI match what the Programmer actually holds" refresh (RefreshAllFaders, Clear
    /// Programmer) must go through this, never through <see cref="Value"/> - setting Value there
    /// would immediately re-store the just-cleared/just-refreshed value straight back into the
    /// Programmer via OnValueChanged, silently undoing the very Release/Restore/ClearAll it was
    /// refreshing after.</summary>
    public void RefreshFromProgrammer(byte value)
    {
        _suppressProgrammerWriteback = true;
        Value = value;
        _suppressProgrammerWriteback = false;
    }
}

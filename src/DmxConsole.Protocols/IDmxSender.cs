namespace DmxConsole.Protocols;

/// <summary>
/// Something that can transmit one universe's worth of DMX data over the wire.
/// Implementations (Art-Net, sACN, USB-DMX...) are wired to
/// DmxOutputEngine.UniverseOutputReady by the application layer.
/// </summary>
public interface IDmxSender : IDisposable
{
    string ProtocolName { get; }

    /// <summary>Sends a full 512-byte universe snapshot. Must be cheap/non-blocking enough to call at ~40Hz.</summary>
    void Send(int universeId, byte[] data);
}

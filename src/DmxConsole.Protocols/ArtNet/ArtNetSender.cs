using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DmxConsole.Protocols.ArtNet;

/// <summary>
/// Sends Art-Net (ArtDMX, OpCode 0x5000) packets over UDP, one per universe.
/// Internal universe id (0-255) is mapped directly onto Art-Net's combined
/// SubUni byte (Net stays 0), which covers up to 256 universes - plenty for this console.
/// </summary>
public sealed class ArtNetSender : IDmxSender
{
    public const int Port = 6454;
    private static readonly byte[] IdBytes = Encoding.ASCII.GetBytes("Art-Net\0");

    private readonly UdpClient _client;
    private readonly IPEndPoint _target;
    private readonly Dictionary<int, byte> _sequences = new();
    private readonly object _lock = new();

    public string ProtocolName => "Art-Net";

    /// <param name="targetAddress">Broadcast (e.g. 255.255.255.255) or a specific node's unicast IP.</param>
    public ArtNetSender(IPAddress? targetAddress = null)
    {
        _client = new UdpClient();
        _client.EnableBroadcast = true;
        _target = new IPEndPoint(targetAddress ?? IPAddress.Broadcast, Port);
    }

    public void Send(int universeId, byte[] data)
    {
        if (data.Length > 512) throw new ArgumentException("DMX universe data cannot exceed 512 bytes.", nameof(data));
        if (universeId is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(universeId), "ArtNetSender supports universe ids 0-255.");

        byte sequence;
        lock (_lock)
        {
            _sequences.TryGetValue(universeId, out var seq);
            seq = seq == 255 ? (byte)1 : (byte)(seq + 1); // 0 means "sequencing disabled"; wrap 1..255
            _sequences[universeId] = seq;
            sequence = seq;
        }

        int length = data.Length;
        var packet = new byte[18 + length];
        IdBytes.CopyTo(packet, 0);
        packet[8] = 0x00;  // OpCode low byte
        packet[9] = 0x50;  // OpCode high byte -> 0x5000 = OpOutput/ArtDMX
        packet[10] = 0;    // ProtVerHi
        packet[11] = 14;   // ProtVerLo
        packet[12] = sequence;
        packet[13] = 0;    // Physical port, informational only
        packet[14] = (byte)(universeId & 0xFF); // SubUni
        packet[15] = 0;    // Net
        packet[16] = (byte)((length >> 8) & 0xFF); // LengthHi
        packet[17] = (byte)(length & 0xFF);        // LengthLo
        Array.Copy(data, 0, packet, 18, length);

        _client.Send(packet, packet.Length, _target);
    }

    public void Dispose() => _client.Dispose();
}

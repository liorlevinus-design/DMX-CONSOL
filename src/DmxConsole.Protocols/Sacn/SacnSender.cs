using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DmxConsole.Protocols.Sacn;

/// <summary>
/// Sends sACN / E1.31 (ANSI E1.31-2018) data packets over UDP multicast, one per universe.
/// Each universe is sent to its standard multicast group 239.255.hi.lo (hi/lo = universe id
/// split into bytes), port 5568, so any sACN-compliant node/monitor on the network can join it.
/// </summary>
public sealed class SacnSender : IDmxSender
{
    public const int Port = 5568;

    private const uint VectorRootE131Data = 0x00000004;
    private const uint VectorE131DataPacket = 0x00000002;
    private const byte VectorDmpSetProperty = 0x02;
    private const byte AddressTypeAndDataType = 0xA1;

    private readonly Guid _cid = Guid.NewGuid();
    private readonly byte[] _sourceName;
    private readonly UdpClient _client;
    private readonly Dictionary<int, byte> _sequences = new();
    private readonly object _lock = new();

    public string ProtocolName => "sACN (E1.31)";

    /// <summary>Priority 0-200 as sent in the sACN packet; higher wins when multiple sources send the same universe.</summary>
    public byte Priority { get; set; } = 100;

    public SacnSender(string sourceName = "DMX Console")
    {
        _sourceName = new byte[64];
        var nameBytes = Encoding.UTF8.GetBytes(sourceName);
        Array.Copy(nameBytes, _sourceName, Math.Min(nameBytes.Length, 63));

        _client = new UdpClient();
        _client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 8);
    }

    public static IPAddress MulticastGroupFor(int universeId) =>
        IPAddress.Parse($"239.255.{(universeId >> 8) & 0xFF}.{universeId & 0xFF}");

    public void Send(int universeId, byte[] data)
    {
        if (data.Length > 512) throw new ArgumentException("DMX universe data cannot exceed 512 bytes.", nameof(data));
        if (universeId is < 1 or > 63999) throw new ArgumentOutOfRangeException(nameof(universeId), "sACN universe ids must be 1-63999.");

        byte sequence;
        lock (_lock)
        {
            _sequences.TryGetValue(universeId, out var seq);
            seq = unchecked((byte)(seq + 1));
            _sequences[universeId] = seq;
            sequence = seq;
        }

        var packet = BuildPacket(universeId, sequence, data);
        var target = new IPEndPoint(MulticastGroupFor(universeId), Port);
        _client.Send(packet, packet.Length, target);
    }

    private byte[] BuildPacket(int universeId, byte sequence, byte[] dmxData)
    {
        int dmxDataLength = 1 + dmxData.Length; // + DMX start code
        int dmpPduLength = 10 + dmxDataLength;
        int framingPduLength = 77 + dmpPduLength;
        int rootPduLength = 22 + framingPduLength;
        int totalLength = 16 + rootPduLength;

        var buf = new byte[totalLength];
        int o = 0;

        // --- Root Layer ---
        WriteU16(buf, ref o, 0x0010); // Preamble Size
        WriteU16(buf, ref o, 0x0000); // Post-amble Size
        WriteBytes(buf, ref o, Encoding.ASCII.GetBytes("ASC-E1.17\0\0\0")); // ACN Packet Identifier (12 bytes)
        WriteU16(buf, ref o, FlagsAndLength(rootPduLength));
        WriteU32(buf, ref o, VectorRootE131Data);
        WriteBytes(buf, ref o, _cid.ToByteArray()); // CID (16 bytes)

        // --- Framing Layer ---
        WriteU16(buf, ref o, FlagsAndLength(framingPduLength));
        WriteU32(buf, ref o, VectorE131DataPacket);
        WriteBytes(buf, ref o, _sourceName); // Source Name (64 bytes, pre-padded)
        buf[o++] = Priority;
        WriteU16(buf, ref o, 0); // Sync Address (0 = not synchronized)
        buf[o++] = sequence;
        buf[o++] = 0; // Options
        WriteU16(buf, ref o, (ushort)universeId);

        // --- DMP Layer ---
        WriteU16(buf, ref o, FlagsAndLength(dmpPduLength));
        buf[o++] = VectorDmpSetProperty;
        buf[o++] = AddressTypeAndDataType;
        WriteU16(buf, ref o, 0x0000); // First Property Address
        WriteU16(buf, ref o, 0x0001); // Address Increment
        WriteU16(buf, ref o, (ushort)dmxDataLength); // Property value count (start code + slots)
        buf[o++] = 0x00; // DMX start code
        WriteBytes(buf, ref o, dmxData);

        return buf;
    }

    private static ushort FlagsAndLength(int length) => (ushort)(0x7000 | (length & 0x0FFF));

    private static void WriteU16(byte[] buf, ref int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(offset, 2), value);
        offset += 2;
    }

    private static void WriteU32(byte[] buf, ref int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(offset, 4), value);
        offset += 4;
    }

    private static void WriteBytes(byte[] buf, ref int offset, byte[] source)
    {
        Array.Copy(source, 0, buf, offset, source.Length);
        offset += source.Length;
    }

    public void Dispose() => _client.Dispose();
}

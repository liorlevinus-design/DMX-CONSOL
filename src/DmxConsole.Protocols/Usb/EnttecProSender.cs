using System.IO.Ports;

namespace DmxConsole.Protocols.Usb;

/// <summary>
/// Sends DMX over an Enttec DMX USB PRO (or compatible "Pro" firmware) widget using its
/// framed host protocol: the widget itself handles all DMX-line timing (break, MAB,
/// slot rate), so this is far more reliable from user-mode .NET than bit-banging a
/// break signal the way <see cref="EnttecOpenDmxSender"/> has to.
/// One widget only outputs one universe - use one sender instance per widget/COM port.
/// </summary>
public sealed class EnttecProSender : IDmxSender
{
    private const byte StartOfMessage = 0x7E;
    private const byte EndOfMessage = 0xE7;
    private const byte LabelOutputOnlySendDmx = 6;

    private readonly SerialPort _port;
    private readonly object _lock = new();

    public string ProtocolName => "Enttec DMX USB PRO";

    /// <summary>Which internal universe id this widget's single output is bound to.</summary>
    public int UniverseId { get; }

    public EnttecProSender(string comPortName, int universeId = 0)
    {
        UniverseId = universeId;
        _port = new SerialPort(comPortName, 250000, Parity.None, 8, StopBits.Two)
        {
            Handshake = Handshake.None,
            WriteTimeout = 500,
        };
        _port.Open();
    }

    /// <summary>Lists the serial ports currently visible to Windows - the user picks their widget's COM port from here.</summary>
    public static string[] ListAvailablePorts() => SerialPort.GetPortNames();

    /// <summary>Builds the raw "Output Only Send DMX Packet" frame - pure/testable, no I/O.</summary>
    public static byte[] BuildFrame(byte[] dmxData)
    {
        if (dmxData.Length > 512) throw new ArgumentException("DMX universe data cannot exceed 512 bytes.", nameof(dmxData));

        int payloadLength = 1 + dmxData.Length; // + DMX start code
        var frame = new byte[4 + payloadLength + 1];
        frame[0] = StartOfMessage;
        frame[1] = LabelOutputOnlySendDmx;
        frame[2] = (byte)(payloadLength & 0xFF);
        frame[3] = (byte)((payloadLength >> 8) & 0xFF);
        frame[4] = 0x00; // DMX start code
        Array.Copy(dmxData, 0, frame, 5, dmxData.Length);
        frame[^1] = EndOfMessage;
        return frame;
    }

    public void Send(int universeId, byte[] data)
    {
        if (universeId != UniverseId) return; // this widget only carries the one universe it was bound to
        var frame = BuildFrame(data);

        lock (_lock)
        {
            if (_port.IsOpen) _port.Write(frame, 0, frame.Length);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_port.IsOpen) _port.Close();
            _port.Dispose();
        }
    }
}

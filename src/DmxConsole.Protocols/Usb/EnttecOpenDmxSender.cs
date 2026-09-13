using System.IO.Ports;

namespace DmxConsole.Protocols.Usb;

/// <summary>
/// Sends DMX over an Enttec Open DMX USB (or any compatible bare FTDI DMX widget) by
/// driving the break/mark-after-break/data sequence directly through the serial port.
/// Unlike <see cref="EnttecProSender"/>, the widget has no firmware framing of its own -
/// this class IS the DMX transmitter, so break timing rides on regular thread scheduling
/// and is not hardware-precise. It works in practice with the great majority of fixtures
/// (DMX receivers tolerate a wide range of break lengths), but for demanding rigs the
/// USB PRO's onboard timing is the more reliable choice.
/// </summary>
public sealed class EnttecOpenDmxSender : IDmxSender
{
    private readonly SerialPort _port;
    private readonly object _lock = new();

    public string ProtocolName => "Enttec Open DMX USB";

    public int UniverseId { get; }

    public EnttecOpenDmxSender(string comPortName, int universeId = 0)
    {
        UniverseId = universeId;
        _port = new SerialPort(comPortName, 250000, Parity.None, 8, StopBits.Two)
        {
            Handshake = Handshake.None,
            WriteTimeout = 500,
        };
        _port.Open();
    }

    public static string[] ListAvailablePorts() => SerialPort.GetPortNames();

    public void Send(int universeId, byte[] data)
    {
        if (universeId != UniverseId) return;
        if (data.Length > 512) throw new ArgumentException("DMX universe data cannot exceed 512 bytes.", nameof(data));

        var frame = new byte[1 + data.Length];
        frame[0] = 0x00; // DMX start code
        Array.Copy(data, 0, frame, 1, data.Length);

        lock (_lock)
        {
            if (!_port.IsOpen) return;

            // DMX512 requires >=88us break, then >=8us mark-after-break, then the data
            // at 250kbaud. SerialPort.BreakState gives us the line-level break; the
            // Sleep(1) that follows is far longer than the spec minimum (Windows can't
            // reliably sleep for microseconds), which is harmless - most receivers accept
            // a break well beyond 88us.
            _port.BreakState = true;
            Thread.Sleep(1);
            _port.BreakState = false;

            _port.Write(frame, 0, frame.Length);
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

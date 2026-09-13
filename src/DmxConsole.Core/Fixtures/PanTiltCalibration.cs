namespace DmxConsole.Core.Fixtures;

/// <summary>
/// Per-fixture position correction for moving heads/scanners: lets the same show file
/// drive fixtures that are mounted upside-down, mirrored, or with a different physical
/// pan/tilt range, without touching the cue/effect data itself.
/// </summary>
public sealed class PanTiltCalibration
{
    public bool InvertPan { get; set; }
    public bool InvertTilt { get; set; }

    /// <summary>Swap the pan and tilt DMX outputs (fixture rigged rotated 90 degrees).</summary>
    public bool SwapPanTilt { get; set; }

    /// <summary>Raw 0-255 value to add (mod 256) to the pan channel before output - re-centers "home".</summary>
    public byte PanOffset { get; set; }

    /// <summary>Raw 0-255 value to add (mod 256) to the tilt channel before output.</summary>
    public byte TiltOffset { get; set; }

    /// <summary>Clamp the usable pan range to protect rigging/cabling (0-255, min &lt;= max).</summary>
    public byte PanMin { get; set; } = 0;
    public byte PanMax { get; set; } = 255;

    public byte TiltMin { get; set; } = 0;
    public byte TiltMax { get; set; } = 255;

    public static PanTiltCalibration Identity => new();

    /// <summary>Applies invert + offset + range clamp to a raw coarse pan/tilt byte.</summary>
    public byte ApplyPan(byte rawValue)
    {
        byte v = InvertPan ? (byte)(255 - rawValue) : rawValue;
        v = unchecked((byte)(v + PanOffset));
        return Clamp(v, PanMin, PanMax);
    }

    public byte ApplyTilt(byte rawValue)
    {
        byte v = InvertTilt ? (byte)(255 - rawValue) : rawValue;
        v = unchecked((byte)(v + TiltOffset));
        return Clamp(v, TiltMin, TiltMax);
    }

    private static byte Clamp(byte value, byte min, byte max)
    {
        if (min > max) (min, max) = (max, min);
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}

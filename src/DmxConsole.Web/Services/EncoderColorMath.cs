namespace DmxConsole.Web.Services;

/// <summary>
/// Pure, browser-free HSV↔RGB conversion for the Color Picker's Hue/Saturation plane - standard
/// color math over the fixture's own real ColorRed/ColorGreen/ColorBlue channels, never an
/// invented parameter. Value (brightness) is fixed at 1.0 (full) - the picker only ever exposes
/// Hue/Saturation (an "HS" plane, per the work plan); a separate Dimmer/Intensity encoder already
/// controls overall brightness.
/// </summary>
public static class EncoderColorMath
{
    /// <summary>hue in [0,360), saturation in [0,1] -> (r,g,b) bytes at full value.</summary>
    public static (byte R, byte G, byte B) HsToRgb(double hueDegrees, double saturation)
    {
        double h = ((hueDegrees % 360) + 360) % 360;
        double s = Math.Clamp(saturation, 0, 1);

        double c = s; // chroma at V=1
        double x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        double m = 1 - c;

        var (r1, g1, b1) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return (ToByte(r1 + m), ToByte(g1 + m), ToByte(b1 + m));
    }

    /// <summary>(r,g,b) bytes -> hue in [0,360), saturation in [0,1]. Value is discarded (the
    /// picker never reads brightness back out of RGB - Dimmer owns that).</summary>
    public static (double HueDegrees, double Saturation) RgbToHs(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf));
        double min = Math.Min(rf, Math.Min(gf, bf));
        double delta = max - min;

        double hue = 0;
        if (delta > 0.0001)
        {
            if (max == rf) hue = 60 * (((gf - bf) / delta) % 6);
            else if (max == gf) hue = 60 * (((bf - rf) / delta) + 2);
            else hue = 60 * (((rf - gf) / delta) + 4);
        }
        if (hue < 0) hue += 360;

        double saturation = max <= 0.0001 ? 0 : delta / max;
        return (hue, saturation);
    }

    private static byte ToByte(double component) => (byte)Math.Clamp(Math.Round(component * 255.0), 0, 255);
}

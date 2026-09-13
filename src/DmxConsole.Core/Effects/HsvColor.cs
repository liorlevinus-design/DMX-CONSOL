namespace DmxConsole.Core.Effects;

/// <summary>Minimal HSV -> RGB conversion used by <see cref="RainbowEffect"/> to walk the color wheel.</summary>
public static class HsvColor
{
    /// <param name="hue">0..1 (fraction of the color wheel, not degrees).</param>
    /// <param name="saturation">0..1</param>
    /// <param name="value">0..1 (brightness)</param>
    public static (byte R, byte G, byte B) ToRgb(double hue, double saturation, double value)
    {
        hue = ((hue % 1.0) + 1.0) % 1.0;
        double h = hue * 6.0;
        int sector = (int)h;
        double f = h - sector;
        double p = value * (1 - saturation);
        double q = value * (1 - saturation * f);
        double t = value * (1 - saturation * (1 - f));

        var (r, g, b) = sector switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };

        return ((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }
}

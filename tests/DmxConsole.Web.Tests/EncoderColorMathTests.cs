using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

public class EncoderColorMathTests
{
    [Theory]
    [InlineData(0, 1, 255, 0, 0)]
    [InlineData(120, 1, 0, 255, 0)]
    [InlineData(240, 1, 0, 0, 255)]
    [InlineData(60, 1, 255, 255, 0)]
    [InlineData(0, 0, 255, 255, 255)]
    public void HsToRgb_ProducesExpectedPrimaryAndNeutralValues(
        double hue, double saturation, byte expectedR, byte expectedG, byte expectedB)
    {
        var actual = EncoderColorMath.HsToRgb(hue, saturation);

        Assert.Equal((expectedR, expectedG, expectedB), actual);
    }

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(255, 255, 0)]
    [InlineData(80, 160, 240)]
    public void RgbToHs_AndBack_RoundTripsHueAndSaturationAtFullValue(byte r, byte g, byte b)
    {
        // The HS picker intentionally operates at V=1. Normalize the source color to full value
        // before comparing, because brightness belongs to Intensity rather than this picker.
        byte max = Math.Max(r, Math.Max(g, b));
        byte normalizedR = (byte)Math.Round(r / (double)max * 255);
        byte normalizedG = (byte)Math.Round(g / (double)max * 255);
        byte normalizedB = (byte)Math.Round(b / (double)max * 255);

        var hs = EncoderColorMath.RgbToHs(r, g, b);
        var roundTrip = EncoderColorMath.HsToRgb(hs.HueDegrees, hs.Saturation);

        Assert.InRange(Math.Abs(roundTrip.R - normalizedR), 0, 1);
        Assert.InRange(Math.Abs(roundTrip.G - normalizedG), 0, 1);
        Assert.InRange(Math.Abs(roundTrip.B - normalizedB), 0, 1);
    }
}

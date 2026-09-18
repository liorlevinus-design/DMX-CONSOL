using DmxConsole.Core.Fixtures;
using Xunit;

namespace DmxConsole.Core.Tests;

/// <summary>Milestone 1 continued (2026-09-16) - FixtureChannel's calibrated Unit/MinValue/
/// MaxValue and the ToDisplayValue/FromDisplayValue conversions, so the Encoder Drawer can show
/// a real unit/range instead of always raw DMX. Covers ascending and reversed ranges (a profile
/// may calibrate byte 0 to its physical maximum, e.g. an inverted Pan), zero-width ranges,
/// out-of-range clamping, rounding, and construction-time validation.</summary>
public class FixtureChannelUnitTests
{
    private static FixtureChannel Channel(string unit = "DMX", double min = 0, double max = 255) =>
        new() { Name = "Test", Type = ChannelType.Generic, Offset = 0, Unit = unit, MinValue = min, MaxValue = max };

    // ---------- Default (no calibration) preserves existing behavior ----------

    [Fact]
    public void Default_Unit_Is_DMX()
    {
        var channel = new FixtureChannel { Name = "Gobo", Type = ChannelType.Gobo, Offset = 0 };
        Assert.Equal("DMX", channel.Unit);
    }

    [Fact]
    public void Default_Range_Is_0_255()
    {
        var channel = new FixtureChannel { Name = "Gobo", Type = ChannelType.Gobo, Offset = 0 };
        Assert.Equal(0, channel.MinValue);
        Assert.Equal(255, channel.MaxValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(254)]
    [InlineData(255)]
    public void Default_ToDisplayValue_IsIdentity_ForEveryByte(byte raw)
    {
        var channel = Channel();
        Assert.Equal((double)raw, channel.ToDisplayValue(raw));
    }

    [Fact]
    public void Default_FromDisplayValue_IsIdentity()
    {
        var channel = Channel();
        Assert.Equal((byte)128, channel.FromDisplayValue(128));
        Assert.Equal((byte)0, channel.FromDisplayValue(0));
        Assert.Equal((byte)255, channel.FromDisplayValue(255));
    }

    [Fact]
    public void ExistingFixtureChannelInitializers_StillCompile_WithNoCalibrationFields()
    {
        // No Unit/MinValue/MaxValue set at all - must keep compiling and behaving exactly as
        // before this feature existed (the whole point of `init` fields with defaults).
        var channel = new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, DefaultValue = 20 };
        Assert.Equal("DMX", channel.Unit);
        Assert.Equal(0, channel.MinValue);
        Assert.Equal(255, channel.MaxValue);
    }

    // ---------- Ascending range: raw -> display ----------

    [Fact]
    public void AscendingRange_ToDisplayValue_AtMin_Mid_Max()
    {
        var channel = Channel("%", 0, 100);
        Assert.Equal(0.0, channel.ToDisplayValue(0));
        Assert.Equal(100.0, channel.ToDisplayValue(255));
        Assert.InRange(channel.ToDisplayValue(128), 49.5, 50.5); // ~midpoint
    }

    [Fact]
    public void AscendingRange_WithNegativeMin_ToDisplayValue()
    {
        var channel = Channel("°", -270, 270);
        Assert.Equal(-270.0, channel.ToDisplayValue(0));
        Assert.Equal(270.0, channel.ToDisplayValue(255));
    }

    // ---------- Ascending range: display -> raw ----------

    [Fact]
    public void AscendingRange_FromDisplayValue_AtMin_Mid_Max()
    {
        var channel = Channel("%", 0, 100);
        Assert.Equal((byte)0, channel.FromDisplayValue(0));
        Assert.Equal((byte)255, channel.FromDisplayValue(100));
        Assert.Equal((byte)128, channel.FromDisplayValue(50)); // banker's rounding of 127.5
    }

    // ---------- Round-trip, both directions, ascending ----------

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)64)]
    [InlineData((byte)127)]
    [InlineData((byte)128)]
    [InlineData((byte)200)]
    [InlineData((byte)254)]
    [InlineData((byte)255)]
    public void AscendingRange_RoundTrip_RawToDisplayToRaw_StaysWithinOneUnit(byte raw)
    {
        var channel = Channel("%", 0, 100);
        double display = channel.ToDisplayValue(raw);
        byte roundTripped = channel.FromDisplayValue(display);
        Assert.InRange((int)roundTripped, raw - 1, raw + 1); // sub-byte rounding tolerance
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(25.0)]
    [InlineData(50.0)]
    [InlineData(99.0)]
    [InlineData(100.0)]
    public void AscendingRange_RoundTrip_DisplayToRawToDisplay_StaysWithinOnePercent(double display)
    {
        var channel = Channel("%", 0, 100);
        byte raw = channel.FromDisplayValue(display);
        double roundTripped = channel.ToDisplayValue(raw);
        Assert.InRange(roundTripped, display - 1.0, display + 1.0); // one raw byte's worth of tolerance
    }

    // ---------- Reversed range (MinValue > MaxValue) ----------

    [Fact]
    public void ReversedRange_ToDisplayValue_PreservesDeclaredDirection()
    {
        // Byte 0 is declared as the fixture's physical maximum angle (270°), byte 255 as its
        // physical minimum (-270°) - a real, supported calibration, not an error case.
        var channel = Channel("°", 270, -270);

        Assert.Equal(270.0, channel.ToDisplayValue(0));   // raw 0 -> MinValue (270, the higher number)
        Assert.Equal(-270.0, channel.ToDisplayValue(255)); // raw 255 -> MaxValue (-270, the lower number)
    }

    [Fact]
    public void ReversedRange_FromDisplayValue_PreservesDeclaredDirection()
    {
        var channel = Channel("°", 270, -270);

        Assert.Equal((byte)0, channel.FromDisplayValue(270));
        Assert.Equal((byte)255, channel.FromDisplayValue(-270));
    }

    [Fact]
    public void ReversedRange_ClampsToPhysicalBounds_RegardlessOfDeclarationOrder()
    {
        var channel = Channel("°", 270, -270);

        // 500° is above both declared endpoints - clamps to the physical max (270), which is
        // MinValue here, not "MaxValue" taken literally.
        Assert.Equal((byte)0, channel.FromDisplayValue(500));
        // -500° clamps to the physical min (-270), which is MaxValue here.
        Assert.Equal((byte)255, channel.FromDisplayValue(-500));
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)64)]
    [InlineData((byte)128)]
    [InlineData((byte)200)]
    [InlineData((byte)255)]
    public void ReversedRange_RoundTrip_RawToDisplayToRaw(byte raw)
    {
        var channel = Channel("°", 270, -270);
        double display = channel.ToDisplayValue(raw);
        byte roundTripped = channel.FromDisplayValue(display);
        Assert.InRange((int)roundTripped, raw - 1, raw + 1);
    }

    // ---------- Zero-width range (MinValue == MaxValue) ----------

    [Fact]
    public void ZeroWidthRange_ToDisplayValue_AlwaysReturnsTheSingleValue_RegardlessOfRaw()
    {
        var channel = Channel("%", 50, 50);

        Assert.Equal(50.0, channel.ToDisplayValue(0));
        Assert.Equal(50.0, channel.ToDisplayValue(128));
        Assert.Equal(50.0, channel.ToDisplayValue(255));
    }

    [Fact]
    public void ZeroWidthRange_FromDisplayValue_AlwaysReturnsRawZero_DocumentedNoMeaningfulInverse()
    {
        var channel = Channel("%", 50, 50);

        // There is no meaningful inverse for a zero-width range (every byte maps to the same
        // display value) - raw byte 0 is the documented, deliberate fallback, not an error.
        Assert.Equal((byte)0, channel.FromDisplayValue(50));
        Assert.Equal((byte)0, channel.FromDisplayValue(0));
        Assert.Equal((byte)0, channel.FromDisplayValue(9999));
    }

    // ---------- Out-of-range input clamping ----------

    [Fact]
    public void FromDisplayValue_ClampsAboveMax_AscendingRange()
    {
        var channel = Channel("%", 0, 100);
        Assert.Equal((byte)255, channel.FromDisplayValue(500));
    }

    [Fact]
    public void FromDisplayValue_ClampsBelowMin_AscendingRange()
    {
        var channel = Channel("%", 0, 100);
        Assert.Equal((byte)0, channel.FromDisplayValue(-50));
    }

    // ---------- Rounding behavior ----------

    [Fact]
    public void FromDisplayValue_RoundsToNearestByte_NotTruncated()
    {
        var channel = Channel("DMX", 0, 255);
        // 100.6 should round up to 101, not truncate to 100.
        Assert.Equal((byte)101, channel.FromDisplayValue(100.6));
        // 100.4 should round down to 100.
        Assert.Equal((byte)100, channel.FromDisplayValue(100.4));
    }

    [Fact]
    public void FromDisplayValue_ExactMidpoint_UsesBankersRounding()
    {
        // 100.5 is an exact tie between 100 and 101 - .NET's default MidpointRounding.ToEven
        // rounds to the nearest even integer (100).
        var channel = Channel("DMX", 0, 255);
        Assert.Equal((byte)100, channel.FromDisplayValue(100.5));
    }

    // ---------- Construction-time validation ----------

    [Fact]
    public void Unit_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new FixtureChannel { Name = "Test", Type = ChannelType.Generic, Offset = 0, Unit = null! });
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void MinValue_NonFinite_ThrowsArgumentException(double invalid)
    {
        Assert.Throws<ArgumentException>(() =>
            new FixtureChannel { Name = "Test", Type = ChannelType.Generic, Offset = 0, MinValue = invalid });
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void MaxValue_NonFinite_ThrowsArgumentException(double invalid)
    {
        Assert.Throws<ArgumentException>(() =>
            new FixtureChannel { Name = "Test", Type = ChannelType.Generic, Offset = 0, MaxValue = invalid });
    }

    // ---------- Runtime input validation (FromDisplayValue's own display argument) ----------

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FromDisplayValue_NonFiniteInput_ThrowsArgumentException(double invalid)
    {
        var channel = Channel("%", 0, 100);
        Assert.Throws<ArgumentException>(() => channel.FromDisplayValue(invalid));
    }
}

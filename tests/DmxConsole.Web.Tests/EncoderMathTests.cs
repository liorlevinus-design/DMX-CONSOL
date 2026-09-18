using DmxConsole.Web.Services;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>Milestone 1 continued (2026-09-16) - the rotary encoder's pure drag/wheel math,
/// tested independently of any browser/pointer-event plumbing.</summary>
public class EncoderMathTests
{
    [Fact]
    public void ApplyDrag_UpwardMovement_Increases()
    {
        byte result = EncoderMath.ApplyDrag(startValue: 100, deltaPixelsUp: 150, fine: false); // half of 300px range = ~127.5
        Assert.True(result > 100);
    }

    [Fact]
    public void ApplyDrag_DownwardMovement_Decreases()
    {
        byte result = EncoderMath.ApplyDrag(startValue: 100, deltaPixelsUp: -150, fine: false);
        Assert.True(result < 100);
    }

    [Fact]
    public void ApplyDrag_FullRangeDrag_ReachesMax()
    {
        byte result = EncoderMath.ApplyDrag(startValue: 0, deltaPixelsUp: 300, fine: false);
        Assert.Equal((byte)255, result);
    }

    [Fact]
    public void ApplyDrag_ClampsAtZeroAndMax()
    {
        Assert.Equal((byte)0, EncoderMath.ApplyDrag(startValue: 10, deltaPixelsUp: -1000, fine: false));
        Assert.Equal((byte)255, EncoderMath.ApplyDrag(startValue: 250, deltaPixelsUp: 1000, fine: false));
    }

    [Fact]
    public void ApplyDrag_FineMode_MovesLessForSamePixelDelta()
    {
        byte normal = EncoderMath.ApplyDrag(startValue: 100, deltaPixelsUp: 30, fine: false);
        byte fine = EncoderMath.ApplyDrag(startValue: 100, deltaPixelsUp: 30, fine: true);

        Assert.True(fine - 100 < normal - 100);
        Assert.True(fine > 100); // still moves, just less
    }

    [Fact]
    public void ApplyWheel_NegativeDeltaY_Increases()
    {
        byte result = EncoderMath.ApplyWheel(currentValue: 100, wheelDeltaY: -100, fine: false);
        Assert.Equal((byte)102, result);
    }

    [Fact]
    public void ApplyWheel_PositiveDeltaY_Decreases()
    {
        byte result = EncoderMath.ApplyWheel(currentValue: 100, wheelDeltaY: 100, fine: false);
        Assert.Equal((byte)98, result);
    }

    [Fact]
    public void ApplyWheel_FineMode_UsesSmallerStep()
    {
        byte normal = EncoderMath.ApplyWheel(currentValue: 100, wheelDeltaY: -100, fine: false);
        byte fine = EncoderMath.ApplyWheel(currentValue: 100, wheelDeltaY: -100, fine: true);

        Assert.True(fine - 100 < normal - 100);
    }

    [Fact]
    public void ApplyWheel_ClampsAtZeroAndMax()
    {
        Assert.Equal((byte)0, EncoderMath.ApplyWheel(currentValue: 1, wheelDeltaY: 100, fine: false));
        Assert.Equal((byte)255, EncoderMath.ApplyWheel(currentValue: 254, wheelDeltaY: -100, fine: false));
    }
}

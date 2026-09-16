namespace DmxConsole.Web.Services;

/// <summary>
/// Pure, browser-free math for the rotary encoder's drag/wheel interaction - kept separate from
/// EncoderKnob.razor so the actual value-computation formulas are unit-testable without a
/// browser. The encoder behaves as an infinite rotary (Ableton/DAW-knob style): dragging tracks
/// vertical pixel delta only, never a literal circular path around the knob.
/// </summary>
public static class EncoderMath
{
    /// <summary>Pixels of vertical drag needed to sweep the full 0..255 range at normal
    /// sensitivity.</summary>
    private const double PixelsPerFullRange = 300;

    /// <summary>Fine mode (Shift held) needs this many times more pixels per unit - finer
    /// control, same drag distance.</summary>
    private const double FineModeMultiplier = 8;

    private const int WheelStepNormal = 2;
    private const int WheelStepFine = 1;

    /// <summary>Dragging up (deltaPixelsUp &gt; 0) increases the value - the standard DAW/console
    /// knob convention.</summary>
    public static byte ApplyDrag(byte startValue, double deltaPixelsUp, bool fine)
    {
        double range = fine ? PixelsPerFullRange * FineModeMultiplier : PixelsPerFullRange;
        double result = startValue + (deltaPixelsUp / range) * 255.0;
        return (byte)Math.Clamp(Math.Round(result), 0, 255);
    }

    /// <summary>Wheel up (negative deltaY, the browser convention) increases the value.</summary>
    public static byte ApplyWheel(byte currentValue, double wheelDeltaY, bool fine)
    {
        int step = fine ? WheelStepFine : WheelStepNormal;
        int direction = wheelDeltaY < 0 ? 1 : -1;
        return (byte)Math.Clamp(currentValue + direction * step, 0, 255);
    }
}

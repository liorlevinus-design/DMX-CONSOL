namespace DmxConsole.Application.Commands.Programmer;

/// <summary>How a numeric value in an adjustment command combines with the current value.</summary>
public enum AdjustOperation
{
    /// <summary>Sets the value outright, ignoring whatever it was before.</summary>
    Absolute,

    /// <summary>Adds percentage points to the current value (e.g. 70% + 20 = 90%), clamped to 0-100.</summary>
    Relative,
}

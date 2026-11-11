namespace Overseer.Services;

/// <summary>
/// Rounding to a number of decimal digits that treats a decimal midpoint as a midpoint: the value is
/// scaled by 10^digits, rounded to an integer and scaled back, so 2.55 rounds to 2.6 away from zero
/// even though the nearest double to 2.55 lies just below it. Results match .NET 10's
/// <see cref="Math.Round(double, int, MidpointRounding)"/> bit for bit; from .NET 11 that method
/// rounds the exact binary value instead (2.55 → 2.5). Use this, not <c>Math.Round</c> with digits,
/// for every <see cref="double"/> in Overseer; <c>DecimalIntentRoundingTests</c> enforces it.
/// </summary>
public static class DecimalIntentRounding
{
    private const int MaxDigits = 15;

    // At or above this magnitude a double has no fractional digits left to round.
    private const double RoundLimit = 1e16;

    private static readonly double[] Power10 =
    [
        1E0, 1E1, 1E2, 1E3, 1E4, 1E5, 1E6, 1E7,
        1E8, 1E9, 1E10, 1E11, 1E12, 1E13, 1E14, 1E15,
    ];

    /// <summary><paramref name="value"/> rounded to <paramref name="digits"/> decimals, midpoints to even.</summary>
    public static double Round(double value, int digits) => Round(value, digits, MidpointRounding.ToEven);

    /// <summary><paramref name="value"/> rounded to <paramref name="digits"/> decimals with <paramref name="mode"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="digits"/> is below 0 or above 15.</exception>
    /// <exception cref="ArgumentException"><paramref name="mode"/> is not a <see cref="MidpointRounding"/> value.</exception>
    public static double Round(double value, int digits, MidpointRounding mode)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(digits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, MaxDigits);
        if (mode < MidpointRounding.ToEven || mode > MidpointRounding.ToPositiveInfinity)
            throw new ArgumentException($"{mode} is not a valid {nameof(MidpointRounding)}.", nameof(mode));

        if (Math.Abs(value) < RoundLimit)
        {
            double power10 = Power10[digits];
            value = Math.Round(value * power10, mode) / power10;
        }

        return value;
    }
}

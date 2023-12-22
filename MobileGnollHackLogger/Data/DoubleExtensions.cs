using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Globalization;

namespace MobileGnollHackLogger.Data
{
    public static class DoubleExtensions
    {
        public static string ToPercentageString(this double value, int significantDigits = 3)
        {
            return ToPercentageString(value, CultureInfo.InvariantCulture, false, significantDigits);
        }

        public static string ToPercentageString(this double value, IFormatProvider formatProvider, int significantDigits = 3)
        {
            return ToPercentageString(value, formatProvider, false, significantDigits);
        }

        public static string ToPercentageString(this double value, bool nonBreakingSpace, int significantDigits = 3)
        {
            return ToPercentageString(value, CultureInfo.InvariantCulture, nonBreakingSpace, significantDigits);
        }

        public static string ToPercentageString(this double value, IFormatProvider formatProvider, bool nonBreakingSpace, int significantDigits = 3)
        {
            if(significantDigits < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(significantDigits), "significantDigits must be positive.");
            }

            return (value * 100d).ToString("G" + significantDigits, formatProvider) + (nonBreakingSpace ? "\u00A0" : " ") + "%";
        }
    }
}

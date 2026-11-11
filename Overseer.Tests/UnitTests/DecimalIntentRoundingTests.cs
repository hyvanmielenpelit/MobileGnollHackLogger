namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overseer.Services;
using Xunit;

/// <summary>
/// <see cref="DecimalIntentRounding"/> rounds decimal midpoints as .NET 10's <c>Math.Round(double, int,
/// MidpointRounding)</c> did, and Overseer source rounds every <see cref="double"/> to digits through it.
/// </summary>
public class DecimalIntentRoundingTests
{
    // Math.Round(decimal, int, MidpointRounding) is exact and stays; these are the decimal calls.
    private static readonly (string File, string Call)[] DecimalCalls =
    [
        ("Services/Benchmarking/BenchmarkChatConsistencyReportFacts.cs", "Math.Round(value, 4, MidpointRounding.AwayFromZero)"),
    ];

    [Theory]
    [InlineData(2.55, 1, MidpointRounding.AwayFromZero, 2.6)]
    [InlineData(4.55, 1, MidpointRounding.AwayFromZero, 4.6)]
    [InlineData(3.05, 1, MidpointRounding.AwayFromZero, 3.1)]
    [InlineData(0.15, 1, MidpointRounding.AwayFromZero, 0.2)]
    [InlineData(-0.25, 1, MidpointRounding.AwayFromZero, -0.3)]
    [InlineData(-2.55, 1, MidpointRounding.AwayFromZero, -2.6)]
    [InlineData(0.125, 2, MidpointRounding.ToEven, 0.12)]
    [InlineData(0.375, 2, MidpointRounding.ToEven, 0.38)]
    [InlineData(2.59, 1, MidpointRounding.ToZero, 2.5)]
    [InlineData(-2.51, 1, MidpointRounding.ToNegativeInfinity, -2.6)]
    [InlineData(2.51, 1, MidpointRounding.ToPositiveInfinity, 2.6)]
    [InlineData(2.5, 0, MidpointRounding.AwayFromZero, 3.0)]
    public void ADecimalMidpoint_RoundsAsTheDecimalReads(double value, int digits, MidpointRounding mode, double expected)
    {
        Assert.Equal(expected, DecimalIntentRounding.Round(value, digits, mode));
    }

    [Fact]
    public void TheTwoArgumentOverload_RoundsMidpointsToEven()
    {
        Assert.Equal(0.12, DecimalIntentRounding.Round(0.125, 2));
        Assert.Equal(DecimalIntentRounding.Round(2.55, 1, MidpointRounding.ToEven), DecimalIntentRounding.Round(2.55, 1));
    }

    [Theory]
    [InlineData(1e16 + 2)]
    [InlineData(-1e17)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void AValueWithNoFractionalDigitsLeft_IsReturnedUnchanged(double value)
    {
        Assert.Equal(value, DecimalIntentRounding.Round(value, 3, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void NaN_StaysNaN()
    {
        Assert.True(double.IsNaN(DecimalIntentRounding.Round(double.NaN, 2, MidpointRounding.AwayFromZero)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void DigitsOutsideZeroToFifteen_Throw(int digits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DecimalIntentRounding.Round(1.0, digits, MidpointRounding.ToEven));
    }

    [Fact]
    public void AnUndefinedMode_Throws()
    {
        Assert.Throws<ArgumentException>(() => DecimalIntentRounding.Round(1.0, 1, (MidpointRounding)99));
    }

    [Fact]
    public void OverseerSource_RoundsNoDoubleToDigitsWithMathRound()
    {
        string overseer = Path.Combine(RepositoryRoot(), "Overseer");
        var offenders = new List<string>();
        var decimalCallsSeen = new HashSet<(string, string)>();

        foreach (string file in Directory.GetFiles(overseer, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(overseer, file).Replace('\\', '/');
            if (relative.Split('/').Any(part => part is "bin" or "obj" or "ClientApp" or "node_modules")
                || relative == "Services/DecimalIntentRounding.cs")
            {
                continue;
            }

            string[] lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (string call in DigitsCalls(lines[i]))
                {
                    var known = DecimalCalls.FirstOrDefault(d => d.File == relative && d.Call == call);
                    if (known.File != null)
                    {
                        decimalCallsSeen.Add(known);
                        continue;
                    }

                    offenders.Add($"{relative}:{i + 1}: {call}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Round a double to digits with DecimalIntentRounding.Round, not Math.Round (a decimal operand goes in DecimalCalls):"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        Assert.Equal(DecimalCalls.Length, decimalCallsSeen.Count);
    }

    /// <summary>
    /// The <c>Math.Round(…)</c> calls on one line whose second argument is not a
    /// <see cref="MidpointRounding"/> member, which is the digits overload.
    /// </summary>
    private static IEnumerable<string> DigitsCalls(string line)
    {
        const string Head = "Math.Round(";
        for (int start = line.IndexOf(Head, StringComparison.Ordinal); start >= 0; start = line.IndexOf(Head, start + Head.Length, StringComparison.Ordinal))
        {
            int depth = 0;
            int end = -1;
            var commas = new List<int>();
            for (int c = start + Head.Length - 1; c < line.Length && end < 0; c++)
            {
                switch (line[c])
                {
                    case '(' or '[' or '{': depth++; break;
                    case ')' or ']' or '}': if (--depth == 0) end = c; break;
                    case ',' when depth == 1: commas.Add(c); break;
                }
            }

            if (end < 0 || commas.Count == 0)
            {
                continue;
            }

            int secondEnd = commas.Count > 1 ? commas[1] : end;
            string second = line[(commas[0] + 1)..secondEnd].Trim();
            if (!second.StartsWith("MidpointRounding.", StringComparison.Ordinal))
            {
                yield return line[start..(end + 1)];
            }
        }
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MobileGnollHackLogger.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}

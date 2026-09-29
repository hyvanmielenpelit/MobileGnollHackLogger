using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Overseer.Services.Benchmarking;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>The identity of a comparison's entry set, which stored report documents are listed by.</summary>
public class BenchmarkReportComparisonKeyTests
{
    [Fact]
    public void From_HashesTheCanonicalText_InLowerCaseHex()
    {
        string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("runs=3,12;groups=4"))).ToLowerInvariant();

        string key = BenchmarkReportComparisonKey.From(new long[] { 12, 3 }, new long[] { 4 });

        Assert.Equal(expected, key);
        Assert.Equal(64, key.Length);
        Assert.True(key.All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f')));
    }

    [Fact]
    public void From_IgnoresOrderAndDuplicates_ButNotWhichListAnIdIsIn()
    {
        string key = BenchmarkReportComparisonKey.From(new long[] { 1, 2 }, new long[] { 9 });

        Assert.Equal(key, BenchmarkReportComparisonKey.From(new long[] { 2, 1, 2 }, new long[] { 9, 9 }));
        Assert.NotEqual(key, BenchmarkReportComparisonKey.From(new long[] { 1, 2, 9 }, Array.Empty<long>()));
        Assert.NotEqual(key, BenchmarkReportComparisonKey.From(new long[] { 1 }, new long[] { 2, 9 }));
    }

    [Fact]
    public void From_OfEmptyLists_HashesTheEmptyCanonicalText()
    {
        string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("runs=;groups="))).ToLowerInvariant();

        Assert.Equal(expected, BenchmarkReportComparisonKey.From(Array.Empty<long>(), Array.Empty<long>()));
    }

    [Fact]
    public void TryFromEntryKeys_MatchesFrom_WhateverTheOrderAndSpacing()
    {
        Assert.True(BenchmarkReportComparisonKey.TryFromEntryKeys(new[] { "group:4", " run:12", "run:3 " }, out var key));

        Assert.Equal(BenchmarkReportComparisonKey.From(new long[] { 3, 12 }, new long[] { 4 }), key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("run:")]
    [InlineData("run:abc")]
    [InlineData("run:-3")]
    [InlineData("run:0")]
    [InlineData("Run:3")]
    [InlineData("suite:3")]
    [InlineData("run:3;group:4")]
    public void TryFromEntryKeys_RefusesAnyOtherForm(string entry)
    {
        Assert.False(BenchmarkReportComparisonKey.TryFromEntryKeys(new[] { "run:1", entry }, out var key));
        Assert.Equal(string.Empty, key);
    }

    [Fact]
    public void TryFromEntryKeys_RefusesAnEmptyList()
    {
        Assert.False(BenchmarkReportComparisonKey.TryFromEntryKeys(Array.Empty<string>(), out _));
    }
}

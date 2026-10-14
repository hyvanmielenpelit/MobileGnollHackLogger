namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Overseer.Services.ChatConsistency;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The server against the JSON fixtures the Angular client's specs read too: the Laney p′ limits of
/// <c>cc-laney-limits.fixture.json</c> and the published protocols of <c>cc-protocol.fixture.json</c>.
/// </summary>
public class ChatConsistencyClientParityTests
{
    private const string LaneyFixture = "cc-laney-limits.fixture.json";

    private const string ProtocolFixture = "cc-protocol.fixture.json";

    public static TheoryData<string> LaneyCaseNames()
    {
        using var fixture = Read(LaneyFixture);
        var names = new TheoryData<string>();
        foreach (var c in fixture.RootElement.GetProperty("cases").EnumerateArray()) names.Add(c.GetProperty("name").GetString()!);
        return names;
    }

    [Theory]
    [MemberData(nameof(LaneyCaseNames))]
    public void LaneyPPrimeLimits_FollowsTheSharedFixture(string name)
    {
        using var fixture = Read(LaneyFixture);
        double tolerance = fixture.RootElement.GetProperty("tolerance").GetDouble();
        var c = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(e => e.GetProperty("name").GetString() == name);

        var limits = ChatConsistencyStatistics.LaneyPPrimeLimits(Baseline(c), c.GetProperty("nI").GetInt32());

        var expected = c.GetProperty("expected");
        if (expected.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(limits);
            return;
        }

        Assert.NotNull(limits);
        Assert.Equal(expected.GetProperty("center").GetDouble(), limits!.Center, tolerance);
        Assert.Equal(expected.GetProperty("lower").GetDouble(), limits.Lower, tolerance);
        Assert.Equal(expected.GetProperty("upper").GetDouble(), limits.Upper, tolerance);
        Assert.Equal(expected.GetProperty("sigmaZ").GetDouble(), limits.SigmaZ, tolerance);
    }

    /// <summary>The fixture's divergent and excluded cases, which the server refuses and the client does not.</summary>
    [Fact]
    public void LaneyPPrimeLimits_DivergentCases_Throw()
    {
        using var fixture = Read(LaneyFixture);
        var cases = fixture.RootElement.GetProperty("divergent").EnumerateArray()
            .Concat(fixture.RootElement.GetProperty("excluded").EnumerateArray())
            .ToList();

        Assert.NotEmpty(cases);
        Assert.All(cases, c => Assert.Throws<ArgumentOutOfRangeException>(
            () => ChatConsistencyStatistics.LaneyPPrimeLimits(Baseline(c), c.GetProperty("nI").GetInt32())));
    }

    [Theory]
    [InlineData("V1")]
    [InlineData("V2")]
    public void PublishedProtocol_MatchesTheClientsCopy(string version)
    {
        using var fixture = Read(ProtocolFixture);
        double tolerance = fixture.RootElement.GetProperty("tolerance").GetDouble();
        var copy = fixture.RootElement.GetProperty("protocols").GetProperty(version);
        var sample = copy.GetProperty("sample");
        var endpoints = copy.GetProperty("endpoints").EnumerateArray().ToList();
        var protocol = ChatConsistencyProtocol.ForVersion(version);

        Assert.Equal(endpoints.Select(e => e.GetProperty("id").GetString()), protocol.Endpoints.Select(e => e.Id));
        foreach (var (endpoint, expected) in protocol.Endpoints.Zip(endpoints))
        {
            bool logRatio = endpoint.Scale == ChatConsistencyEffectScale.LogRatio;
            Assert.Equal(expected.GetProperty("name").GetString(), endpoint.Name);
            Assert.Equal(expected.GetProperty("stratified").GetBoolean(), endpoint.Stratified);
            Assert.Equal(expected.GetProperty("axis").GetString(), JsonNamingPolicy.CamelCase.ConvertName(endpoint.Axis.ToString()));
            Assert.Equal(expected.GetProperty("unit").GetString(), endpoint.Unit == "log ratio" ? "%" : endpoint.Unit);
            Assert.Equal(expected.GetProperty("margin").GetDouble(), logRatio ? 100.0 * (Math.Exp(endpoint.Margin) - 1.0) : endpoint.Margin, tolerance);
        }

        Assert.Equal(sample.GetProperty("alpha").GetDouble(), protocol.Alpha, tolerance);
        Assert.Equal(sample.GetProperty("minimumRunsPerPeriod").GetInt32(), protocol.MinimumRunsPerPeriod);
        Assert.Equal(sample.GetProperty("minimumDaysPerPeriod").GetInt32(), protocol.MinimumDaysPerPeriod);
        Assert.Equal(sample.GetProperty("minimumPairedItems").GetInt32(), protocol.MinimumPairedItems);
        Assert.Equal(sample.GetProperty("minimumSpeedRunsPerStratum").GetInt32(), protocol.MinimumSpeedRunsPerStratum);
        int? attempts = sample.TryGetProperty("minimumAttemptsPerStratum", out var a) ? a.GetInt32() : null;
        Assert.Equal(attempts, protocol.MinimumAttemptsPerStratum);
    }

    private static JsonDocument Read(string fileName)
        => JsonDocument.Parse(File.ReadAllText(ClientAppFixtures.ChatConsistencyFixture(fileName)));

    private static IReadOnlyList<(int k, int n)> Baseline(JsonElement c)
        => c.GetProperty("baseline").EnumerateArray().Select(u => (u.GetProperty("k").GetInt32(), u.GetProperty("n").GetInt32())).ToList();
}

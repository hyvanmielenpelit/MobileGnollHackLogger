using System;
using System.Collections.Generic;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;
using Overseer.Services.Providers;
using Overseer.Services.Telemetry;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ModelCallTelemetryWriterTests
{
    private static ModelCallRecord Record(string? served = "gpt-6.1-sol-2026-09-01") => new()
    {
        CallIndex = 2,
        StartedAtUtc = new DateTime(2026, 10, 7, 9, 30, 0, DateTimeKind.Utc),
        Provider = "OpenAI",
        RequestedModelId = "gpt-6.1-sol",
        ThinkingLevelSent = "medium",
        ServiceTierRequested = "default",
        MaxOutputTokensSent = 128000,
        EndpointKind = "official",
        ServedModelId = served,
        ResponseId = "resp_1",
        RequestId = "req_1",
        ServedServiceTier = "default",
        FinishReason = "completed",
        HttpVersion = "2.0",
        PermitWaitMs = 1200,
        BackoffWaitMs = 4000,
        FailedAttemptMs = 300,
        AttemptCount = 3,
        Http429Count = 2,
        FinalHttpStatus = 200,
        HeadersMs = 410,
        ServerProcessingMs = 380,
        FirstEventMs = 420,
        FirstReasoningMs = 450,
        FirstOutputMs = 2100,
        LastDeltaMs = 6100,
        CompletedMs = 6150,
        StreamEndMs = 6160,
        OutputDeltaCount = 80,
        VisibleOutputChars = 1600,
        Last80DecodeSpanMs = 3200,
        Last80VisibleChars = 1280,
        Usage = new TokenUsageReport { TotalPromptTokens = 900, CacheReadTokens = 400, OutputTokens = 700, ReasoningTokens = 300 },
        RateLimitJson = "{\"x-ratelimit-remaining-requests\":\"499\"}",
    };

    [Fact]
    public void Map_CopiesEveryField_AndLinksACandidateRowWithoutAGraderRole()
    {
        var row = ModelCallTelemetryWriter.Map(
            Record(), ModelCallSource.BenchmarkCandidate,
            new ModelCallLinks(BenchmarkRunAnswerId: 5, SystemAiApiConfigurationId: 31, GraderRole: ModelCallGraderRole.Assessor));

        Assert.Equal(ModelCallSource.BenchmarkCandidate, row.Source);
        Assert.Null(row.GraderRole);
        Assert.Equal(5, row.BenchmarkRunAnswerId);
        Assert.Equal(31, row.SystemAiApiConfigurationId);
        Assert.Equal("gpt-6.1-sol-2026-09-01", row.ServedModelId);
        Assert.Equal(1200, row.PermitWaitMs);
        Assert.Equal(4000, row.BackoffWaitMs);
        Assert.Equal((byte)3, row.AttemptCount);
        Assert.Equal((byte)2, row.Http429Count);
        Assert.Equal(2100, row.FirstOutputMs);
        Assert.Equal(1280, row.Last80VisibleChars);
        Assert.Equal(900, row.InputTokens);
        Assert.Equal(400, row.CachedInputTokens);
        Assert.Equal(300, row.ReasoningTokens);
        Assert.Equal("2.0", row.HttpVersion);
    }

    [Fact]
    public void Map_KeepsTheGraderRole_ForAGraderRow()
    {
        var row = ModelCallTelemetryWriter.Map(
            Record(), ModelCallSource.BenchmarkGrader, new ModelCallLinks(GraderRole: ModelCallGraderRole.ClaimVerifier));

        Assert.Equal(ModelCallGraderRole.ClaimVerifier, row.GraderRole);
    }

    [Fact]
    public void Map_CutsStringsToTheirColumns_AndCountsToAByte()
    {
        var record = Record(served: new string('m', 300));
        record.AttemptCount = 900;
        record.ErrorKind = new string('e', 100);
        record.RateLimitJson = new string('r', 600);

        var row = ModelCallTelemetryWriter.Map(record, ModelCallSource.BenchmarkCandidate, default);

        Assert.Equal(160, row.ServedModelId!.Length);
        Assert.Equal(64, row.ErrorKind!.Length);
        Assert.Equal(byte.MaxValue, row.AttemptCount);
        Assert.Null(row.RateLimitJson);
    }

    [Fact]
    public void Map_LeavesTokensNull_WhenTheCallReportedNoUsage()
    {
        var record = Record();
        record.Usage = null;

        var row = ModelCallTelemetryWriter.Map(record, ModelCallSource.BenchmarkCandidate, default);

        Assert.Null(row.InputTokens);
        Assert.Null(row.OutputTokens);
    }

    [Fact]
    public void AttachTo_AddsOneRowPerCall_InCallOrder()
    {
        var result = new AgentRunResult();
        var first = Record();
        first.CallIndex = 0;
        var second = Record();
        second.CallIndex = 1;
        result.ModelCalls.Add(first);
        result.ModelCalls.Add(second);
        var answer = new BenchmarkRunAnswer();

        int added = ModelCallTelemetryWriter.AttachTo(answer, result, ModelCallSource.BenchmarkCandidate, new ModelCallLinks(BenchmarkRunAnswerId: 9));

        Assert.Equal(2, added);
        Assert.Equal(new[] { 0, 1 }, answer.ModelCalls.ConvertAll(r => r.CallIndex));
        Assert.All(answer.ModelCalls, r => Assert.Null(r.BenchmarkRunAnswerId));
    }

    [Theory]
    [InlineData(new[] { "a", "a" }, "a")]
    [InlineData(new[] { "a", "b" }, null)]
    [InlineData(new string[0], null)]
    public void ConsensusServedModelId_IsTheAgreedIdOrNull(string[] ids, string? expected)
    {
        var records = new List<ModelCallRecord>();
        foreach (var id in ids)
        {
            records.Add(new ModelCallRecord { ServedModelId = id });
        }
        records.Add(new ModelCallRecord { ServedModelId = null });

        Assert.Equal(expected, ModelCallTelemetryWriter.ConsensusServedModelId(records));
    }
}

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

    private static ModelCallRecord WithAttempts(ModelCallRecord record)
    {
        var start = new DateTime(2026, 10, 7, 9, 30, 0, DateTimeKind.Utc);
        record.Attempts.Add(new ProviderAttemptRecord
        {
            AttemptIndex = 0, StartedAtUtc = start, DurationMs = 900, Outcome = ProviderAttemptOutcome.ProviderOverloaded,
            HttpStatus = 529, ProviderErrorCode = "overloaded_error", EndpointKind = BenchmarkEndpointKind.Official,
            Retried = true, RetryAfterMs = 2000
        });
        record.Attempts.Add(new ProviderAttemptRecord
        {
            AttemptIndex = 1, StartedAtUtc = start.AddSeconds(3), DurationMs = 5200, Outcome = ProviderAttemptOutcome.Ok,
            HttpStatus = 200, EndpointKind = BenchmarkEndpointKind.Official
        });
        return record;
    }

    [Fact]
    public void MapAttempts_WritesOneLedgerRowPerAttempt_InAttemptOrder_WithItsLinks()
    {
        var rows = ModelCallTelemetryWriter.MapAttempts(
            WithAttempts(Record()), callIndex: 4, ModelCallSource.BenchmarkCandidate,
            new ProviderAttemptLinks(
                BenchmarkRunId: 70, BenchmarkRunAnswerId: 5, QuestionId: 11, SystemAiApiConfigurationId: 31,
                GraderRole: ModelCallGraderRole.Assessor, Pass: 2));

        Assert.Equal(2, rows.Count);
        Assert.Equal(new byte[] { 0, 1 }, rows.ConvertAll(r => r.AttemptIndex));
        Assert.All(rows, r =>
        {
            Assert.Equal(70L, r.BenchmarkRunId);
            Assert.Equal(5L, r.BenchmarkRunAnswerId);
            Assert.Equal(11L, r.QuestionId);
            Assert.Equal(31L, r.SystemAiApiConfigurationId);
            Assert.Equal(ModelCallSource.BenchmarkCandidate, r.Source);
            Assert.Null(r.GraderRole);
            Assert.Equal("OpenAI", r.Provider);
            Assert.Equal("gpt-6.1-sol", r.RequestedModelId);
            Assert.Equal(BenchmarkEndpointKind.Official, r.EndpointKind);
            Assert.Equal(2, r.Pass);
            Assert.Equal(4, r.CallIndex);
            Assert.Null(r.BenchmarkRun);
        });

        var failed = rows[0];
        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, failed.Outcome);
        Assert.Equal((short)529, failed.HttpStatus);
        Assert.Equal("overloaded_error", failed.ProviderErrorCode);
        Assert.True(failed.Retried);
        Assert.Equal(2000, failed.RetryAfterMs);
        Assert.Equal(900, failed.DurationMs);

        var ok = rows[1];
        Assert.Equal(ProviderAttemptOutcome.Ok, ok.Outcome);
        Assert.False(ok.Retried);
        Assert.Null(ok.ProviderErrorCode);
        Assert.Equal(new DateTime(2026, 10, 7, 9, 30, 3, DateTimeKind.Utc), ok.StartedAtUtc);
    }

    [Fact]
    public void MapAttempts_KeepsTheGraderRole_ForAGraderRow()
    {
        var rows = ModelCallTelemetryWriter.MapAttempts(
            WithAttempts(Record()), callIndex: 0, ModelCallSource.BenchmarkGrader,
            new ProviderAttemptLinks(BenchmarkRunId: 70, GraderRole: ModelCallGraderRole.Synthesis));

        Assert.All(rows, r =>
        {
            Assert.Equal(ModelCallGraderRole.Synthesis, r.GraderRole);
            Assert.Null(r.BenchmarkRunAnswerId);
            Assert.Null(r.QuestionId);
            Assert.Equal(0, r.Pass);
        });
    }

    [Fact]
    public void MapAttempts_CutsStringsToTheirColumns_AndClampsOutOfRangeValues()
    {
        var record = Record();
        record.Provider = new string('p', 40);
        record.RequestedModelId = new string('m', 200);
        record.Attempts.Add(new ProviderAttemptRecord
        {
            AttemptIndex = 300, DurationMs = -5, HttpStatus = 70000, Outcome = ProviderAttemptOutcome.TransportUnattributed,
            ProviderErrorCode = "bad code!\n<script>" + new string('x', 100), RetryAfterMs = -1
        });

        var row = Assert.Single(ModelCallTelemetryWriter.MapAttempts(record, 0, ModelCallSource.BenchmarkCandidate, new ProviderAttemptLinks(1)));

        Assert.Equal(32, row.Provider.Length);
        Assert.Equal(128, row.RequestedModelId.Length);
        Assert.Equal(byte.MaxValue, row.AttemptIndex);
        Assert.Equal(0, row.DurationMs);
        Assert.Null(row.HttpStatus);
        Assert.Equal(0, row.RetryAfterMs);
        Assert.NotNull(row.ProviderErrorCode);
        Assert.Equal(64, row.ProviderErrorCode!.Length);
        Assert.StartsWith("badcodescript", row.ProviderErrorCode);
        Assert.Matches("^[A-Za-z0-9_.-]+$", row.ProviderErrorCode);
    }

    [Fact]
    public void MapAllAttempts_PlacesEachCallsAttemptsAtItsOwnCallIndex()
    {
        var result = new AgentRunResult();
        var first = WithAttempts(Record());
        first.CallIndex = 0;
        var second = Record();
        second.CallIndex = 1;
        second.Attempts.Add(new ProviderAttemptRecord { Outcome = ProviderAttemptOutcome.OurThrottle });
        var third = Record();
        third.CallIndex = 2;
        result.ModelCalls.AddRange(new[] { first, second, third });

        var rows = ModelCallTelemetryWriter.MapAllAttempts(result, ModelCallSource.BenchmarkCandidate, new ProviderAttemptLinks(70));

        Assert.Equal(new[] { 0, 0, 1 }, rows.ConvertAll(r => r.CallIndex));
        Assert.Equal(ProviderAttemptOutcome.OurThrottle, rows[2].Outcome);
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

namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// Whether an Executive Summary may be written from a stored Root Cause Analysis: nothing stored is missing, the newest one
/// written from the same facts is usable, a changed fact or an unreadable sheet is outdated, an older matching one is never
/// read, and the messages the API answers with and the client shows verbatim.
/// </summary>
public class BenchmarkRootCauseReadinessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const BenchmarkReportDocumentOrigin RunCompletion = BenchmarkReportDocumentOrigin.RunCompletion;

    [Fact]
    public async Task NothingStored_IsMissing_WithTheRunMessageAndCode()
    {
        await using var h = await Harness.CreateAsync();

        var readiness = await h.ReadinessAsync();

        Assert.Equal("missing", readiness.State);
        Assert.Null(readiness.RootCauseDocumentId);
        Assert.Equal("rootCauseMissing", readiness.Code);
        Assert.Equal(
            "The Executive Summary is written from the Root Cause Analysis, and this run has none. Write both: check Root Cause Analysis too.",
            readiness.Message);
    }

    [Fact]
    public async Task TheNewestWrittenFromTheSameFacts_IsUsable_WithItsDigest()
    {
        await using var h = await Harness.CreateAsync();
        long id = await h.StoreRootCauseAsync(BenchmarkReportJson.Serialize(h.Prep.Sheet));

        var (readiness, digest) = await BenchmarkRootCauseReadiness.EvaluateAsync(
            h.Db, RunCompletion, BenchmarkReportScope.Model, h.SubjectKey, h.Prep, Ct);

        Assert.Equal("usable", readiness.State);
        Assert.Equal(id, readiness.RootCauseDocumentId);
        Assert.Null(readiness.Message);
        Assert.Null(readiness.Code);
        Assert.NotNull(digest);
        Assert.Equal(id, digest!.DocumentId);
        Assert.Contains(digest.Facts, f => f.Key == BenchmarkRootCauseDigest.KeyFindingKey);
    }

    [Fact]
    public async Task AChangedFact_IsOutdated_NamingTheStoredDocument()
    {
        await using var h = await Harness.CreateAsync();
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(BenchmarkReportJson.Serialize(h.Prep.Sheet));
        var fact = sheet.Facts.First(f => !BenchmarkRootCauseDigest.IsDigestKey(f.Key));
        fact.Display += " (changed)";
        long id = await h.StoreRootCauseAsync(BenchmarkReportJson.Serialize(sheet));

        var readiness = await h.ReadinessAsync();

        Assert.Equal("outdated", readiness.State);
        Assert.Equal(id, readiness.RootCauseDocumentId);
        Assert.Equal("rootCauseOutdated", readiness.Code);
        Assert.Equal(
            $"The stored Root Cause Analysis (#{id}) was written from different data than this run has now, so the Executive Summary "
            + "cannot be written from it. Rewrite the Root Cause Analysis with it: check Root Cause Analysis — rewrite.",
            readiness.Message);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("null")]
    [InlineData("")]
    public async Task AnUnreadableFactSheet_IsOutdated(string factsJson)
    {
        await using var h = await Harness.CreateAsync();
        long id = await h.StoreRootCauseAsync(factsJson);

        var readiness = await h.ReadinessAsync();

        Assert.Equal("outdated", readiness.State);
        Assert.Equal(id, readiness.RootCauseDocumentId);
        Assert.Equal("rootCauseOutdated", readiness.Code);
    }

    [Fact]
    public async Task OnlyTheNewest_IsConsidered_WhenAnOlderOneMatches()
    {
        await using var h = await Harness.CreateAsync();
        long older = await h.StoreRootCauseAsync(BenchmarkReportJson.Serialize(h.Prep.Sheet));
        long newer = await h.StoreRootCauseAsync(BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.RootCauseAnalysis).FactsJson);
        Assert.True(newer > older);

        var (readiness, digest) = await BenchmarkRootCauseReadiness.EvaluateAsync(
            h.Db, RunCompletion, BenchmarkReportScope.Model, h.SubjectKey, h.Prep, Ct);

        Assert.Equal("outdated", readiness.State);
        Assert.Equal(newer, readiness.RootCauseDocumentId);
        Assert.Null(digest);
    }

    [Fact]
    public async Task AnotherOriginScopeSubjectOrComparison_IsNotConsidered()
    {
        await using var h = await Harness.CreateAsync();
        string facts = BenchmarkReportJson.Serialize(h.Prep.Sheet);
        await h.StoreRootCauseAsync(facts, origin: BenchmarkReportDocumentOrigin.ReportPack);
        await h.StoreRootCauseAsync(facts, scope: BenchmarkReportScope.Comparison);
        await h.StoreRootCauseAsync(facts, subjectKey: "run:987654");

        Assert.Equal("missing", (await h.ReadinessAsync()).State);

        long inComparison = await h.StoreRootCauseAsync(facts, origin: BenchmarkReportDocumentOrigin.ReportPack, comparisonKey: new string('a', 64));
        Assert.Null(await BenchmarkRootCauseReadiness.NewestAsync(
            h.Db, BenchmarkReportDocumentOrigin.ReportPack, BenchmarkReportScope.Model, h.SubjectKey, Ct, comparisonKey: new string('b', 64)));
        Assert.Equal(inComparison, (await BenchmarkRootCauseReadiness.NewestAsync(
            h.Db, BenchmarkReportDocumentOrigin.ReportPack, BenchmarkReportScope.Model, h.SubjectKey, Ct, comparisonKey: new string('a', 64)))!.Id);
    }

    [Fact]
    public async Task AReportPackSubject_IsNamedByTheModelInItsMessages()
    {
        await using var h = await Harness.CreateAsync();
        string label = h.Prep.Subject.Label;

        var missing = await BenchmarkRootCauseReadiness.ForSubjectAsync(
            h.Db, BenchmarkReportDocumentOrigin.ReportPack, BenchmarkReportScope.Model, h.SubjectKey, h.Prep, Ct);
        Assert.Equal(
            $"The Executive Summary for {label} is written from the Root Cause Analysis, and there is none for {label} in this comparison. "
            + "Write both: check Write on the Root Cause Analysis too.",
            missing.Message);

        long id = await h.StoreRootCauseAsync(BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.RootCauseAnalysis).FactsJson,
            origin: BenchmarkReportDocumentOrigin.ReportPack);
        var outdated = await BenchmarkRootCauseReadiness.ForSubjectAsync(
            h.Db, BenchmarkReportDocumentOrigin.ReportPack, BenchmarkReportScope.Model, h.SubjectKey, h.Prep, Ct);
        Assert.Equal(
            $"The stored Root Cause Analysis (#{id}) for {label} was written from different data than this comparison has now, so the "
            + "Executive Summary cannot be written from it. Rewrite the Root Cause Analysis with it: check Rewrite on the Root Cause Analysis.",
            outdated.Message);
        Assert.Equal("rootCauseOutdated", outdated.Code);
    }

    [Fact]
    public void TheMessages_NameTheBatteryRun_AndTheCoveredModels()
    {
        Assert.Equal(
            "The Executive Summary is written from the Root Cause Analysis, and this battery run has none. Write both: check Root Cause Analysis too.",
            BenchmarkRootCauseReadiness.MissingFor(BenchmarkReportDocumentOrigin.BatteryCompletion, label: null).Message);
        Assert.Equal(
            "The stored Root Cause Analysis (#12) was written from different data than this battery run has now, so the Executive Summary "
            + "cannot be written from it. Rewrite the Root Cause Analysis with it: check Root Cause Analysis — rewrite.",
            BenchmarkRootCauseReadiness.OutdatedMessage(BenchmarkRootCauseReadiness.SubjectNounOf(BenchmarkReportDocumentOrigin.BatteryCompletion), 12));
        Assert.Equal(
            "The Executive Summary for these models is written from the Root Cause Analysis, and there is none for these models in this comparison. "
            + "Write both: check Write on the Root Cause Analysis too.",
            BenchmarkRootCauseReadiness.MissingFor(BenchmarkReportDocumentOrigin.ReportPack, BenchmarkRootCauseReadiness.CoveredModelsLabel).Message);
        Assert.Equal("run", BenchmarkRootCauseReadiness.SubjectNounOf(BenchmarkReportDocumentOrigin.RunCompletion));
    }

    [Fact]
    public void ASubjectThatCannotBePrepared_IsOutdatedWithTheRefusal_AndNoCode()
    {
        var readiness = BenchmarkRootCauseReadiness.Unprepared(42, "The run has not finished.");

        Assert.Equal("outdated", readiness.State);
        Assert.Equal(42, readiness.RootCauseDocumentId);
        Assert.Equal("The run has not finished.", readiness.Message);
        Assert.Null(readiness.Code);
    }

    [Fact]
    public void AnExecutiveSummaryAlone_IsOneWithoutItsRootCauseAnalysis()
    {
        Assert.True(BenchmarkRootCauseReadiness.AsksSummaryAlone(new[] { BenchmarkReportAudience.ExecutiveSummary }));
        Assert.False(BenchmarkRootCauseReadiness.AsksSummaryAlone(new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.RootCauseAnalysis }));
        Assert.False(BenchmarkRootCauseReadiness.AsksSummaryAlone(new[] { BenchmarkReportAudience.RootCauseAnalysis }));
        Assert.False(BenchmarkRootCauseReadiness.AsksSummaryAlone(Array.Empty<BenchmarkReportAudience>()));
    }

    [Fact]
    public void TheDto_SerializesInTheApisCamelCase()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(
            BenchmarkRootCauseReadiness.MissingFor(RunCompletion, label: null),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.StartsWith("{\"state\":\"missing\",\"rootCauseDocumentId\":null,\"message\":", json, StringComparison.Ordinal);
        Assert.EndsWith(",\"code\":\"rootCauseMissing\"}", json, StringComparison.Ordinal);
    }

    /// <summary>The first seeded run alone, prepared as its run-completion documents are, over an in-memory database.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; private init; } = default!;
        public BenchmarkReportPackPreparation Prep { get; private init; } = default!;
        public string SubjectKey { get; private init; } = string.Empty;

        public static async Task<Harness> CreateAsync()
        {
            var options = BenchmarkRunExamTests.InMemoryOptions();
            var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
            var db = new ApplicationDbContext(options);
            string subjectKey = "run:" + seeded.RunIds[0];
            var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareAsync(
                db, new BenchmarkModelComparisonService(db),
                new BenchmarkReportPackRequest
                {
                    RunIds = new List<long> { seeded.RunIds[0] },
                    SubjectKey = subjectKey,
                    Audiences = new List<BenchmarkReportAudience> { BenchmarkReportAudience.ExecutiveSummary }
                },
                BenchmarkReportPackPreparation.DefaultAnswerExcerptChars, Ct);
            Assert.True(prep != null, refusal);
            Assert.NotNull(prep!.Sheet.RootCause);
            return new Harness { Db = db, Prep = prep, SubjectKey = subjectKey };
        }

        public Task<BenchmarkRootCauseReadinessDto> ReadinessAsync()
            => BenchmarkRootCauseReadiness.ForSubjectAsync(Db, RunCompletion, BenchmarkReportScope.Model, SubjectKey, Prep, Ct);

        /// <summary>Stores a Root Cause Analysis with <paramref name="factsJson"/> as its fact sheet; returns its id.</summary>
        public async Task<long> StoreRootCauseAsync(
            string factsJson,
            BenchmarkReportDocumentOrigin origin = BenchmarkReportDocumentOrigin.RunCompletion,
            BenchmarkReportScope scope = BenchmarkReportScope.Model,
            string? subjectKey = null,
            string? comparisonKey = null)
        {
            var document = BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.RootCauseAnalysis);
            document.Id = 0;
            document.Origin = origin;
            document.Scope = scope;
            document.SubjectKey = subjectKey ?? SubjectKey;
            document.FactsJson = factsJson;
            if (comparisonKey != null) document.ComparisonKey = comparisonKey;
            document.Runs = new List<BenchmarkReportDocumentRun>();
            Db.BenchmarkReportDocuments.Add(document);
            await Db.SaveChangesAsync(Ct);
            return document.Id;
        }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }
}

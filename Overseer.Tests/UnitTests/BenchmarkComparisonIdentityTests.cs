namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// Comparison identity: one numbered comparison per entry set, whatever the order it was selected in,
/// named once from its entries' labels, renamable, and backfilled onto the Report Pack documents
/// written before comparisons had a number.
/// </summary>
public class BenchmarkComparisonIdentityTests
{
    private const string SuiteName = "Comparison Suite";
    private const string UserId = "user-1";

    private static readonly DateTime T0 = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);

    private static readonly (string Provider, string DisplayName)[] FourModels =
    {
        ("OpenAI", "GPT-5.6 Luna"),
        ("Google", "Gemini 3.8 Flash"),
        ("Anthropic", "Claude Opus 5.5"),
        ("xAI", "Grok 5")
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- Ensure ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ensure_TheSameEntrySet_GivesTheSameComparison_WhateverTheOrder()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        long[] runIds = await SeedRunsAsync(options, FourModels.Take(2).ToArray());

        await using var db = new ApplicationDbContext(options);
        var service = new BenchmarkComparisonIdentityService(db);

        var (first, firstError) = await service.EnsureAsync(new[] { runIds[1], runIds[0] }, null, null, UserId, Ct);
        var (second, secondError) = await service.EnsureAsync(new[] { runIds[0], runIds[1], runIds[0] }, Array.Empty<long>(), Array.Empty<long>(), "user-2", Ct);

        Assert.Null(firstError);
        Assert.Null(secondError);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(1, await db.BenchmarkComparisons.CountAsync(Ct));

        Assert.Equal(BenchmarkReportComparisonKey.From(runIds, Array.Empty<long>()), first.ComparisonKey);
        Assert.Equal($"[\"run:{runIds[0]}\",\"run:{runIds[1]}\"]", first.EntryKeysJson);
        Assert.Equal(2, first.EntryCount);
        Assert.Equal(BenchmarkComparisonSubjectKind.Runs, first.SubjectKind);
        Assert.Equal(UserId, first.CreatedByUserId);
        Assert.Null(first.Name);

        var (other, _) = await service.EnsureAsync(new[] { runIds[0] }, null, null, UserId, Ct);
        Assert.True(other!.Id > first.Id);
    }

    [Fact]
    public async Task Ensure_BatteryResults_AreABatteryComparison()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var definition = BenchmarkBatteryTestData.Definition();
        long first = await BenchmarkBatteryTestData.SeedAsync(db, definition,
            (BenchmarkBatteryTestData.Run(9101, BenchmarkBatteryTestData.SuiteA, new[] { 70, 80 }, new[] { 50, 50 }, modelId: "gpt-5.6-luna"), 0, 1),
            (BenchmarkBatteryTestData.Run(9102, BenchmarkBatteryTestData.SuiteB, new[] { 70, 80 }, new[] { 50, 50 }, modelId: "gpt-5.6-luna"), 1, 1));
        long second = await BenchmarkBatteryTestData.SeedAsync(db, definition,
            (BenchmarkBatteryTestData.Run(9201, BenchmarkBatteryTestData.SuiteA, new[] { 60, 70 }, new[] { 50, 50 }, modelId: "gemini-3.8-flash"), 0, 1),
            (BenchmarkBatteryTestData.Run(9202, BenchmarkBatteryTestData.SuiteB, new[] { 60, 70 }, new[] { 50, 50 }, modelId: "gemini-3.8-flash"), 1, 1));
        db.ChangeTracker.Clear();

        var service = new BenchmarkComparisonIdentityService(db);
        var (comparison, error) = await service.EnsureAsync(null, null, new[] { second, first }, UserId, Ct);

        Assert.Null(error);
        Assert.Equal(BenchmarkComparisonSubjectKind.Batteries, comparison!.SubjectKind);
        Assert.Equal(BenchmarkReportComparisonKey.From(Array.Empty<long>(), Array.Empty<long>(), new[] { first, second }), comparison.ComparisonKey);
        Assert.Equal($"[\"battery:{first}\",\"battery:{second}\"]", comparison.EntryKeysJson);
        Assert.Equal(2, comparison.EntryCount);
        Assert.Equal("gpt-5.6-luna vs gemini-3.8-flash", comparison.DefaultName);

        var (again, _) = await service.EnsureAsync(Array.Empty<long>(), Array.Empty<long>(), new[] { first, second }, UserId, Ct);
        Assert.Equal(comparison.Id, again!.Id);
    }

    [Fact]
    public async Task Ensure_AMixedOrEmptySelection_IsRefused_AndStoresNothing()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        long[] runIds = await SeedRunsAsync(options, FourModels.Take(1).ToArray());

        await using var db = new ApplicationDbContext(options);
        var service = new BenchmarkComparisonIdentityService(db);

        var (mixed, mixedError) = await service.EnsureAsync(runIds, null, new long[] { 7 }, UserId, Ct);
        var (empty, emptyError) = await service.EnsureAsync(null, Array.Empty<long>(), null, UserId, Ct);

        Assert.Null(mixed);
        Assert.Equal(BenchmarkBatteryModelComparison.MixedSourcesError, mixedError);
        Assert.Null(empty);
        Assert.Equal(BenchmarkComparisonIdentityService.EmptySelectionError, emptyError);
        Assert.Equal(0, await db.BenchmarkComparisons.CountAsync(Ct));
    }

    [Fact]
    public async Task Ensure_AnUnknownRun_IsRefusedWithTheComparisonsError()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var service = new BenchmarkComparisonIdentityService(db);

        var (comparison, error) = await service.EnsureAsync(new long[] { 404 }, null, null, UserId, Ct);

        Assert.Null(comparison);
        Assert.Contains("not found", error);
        Assert.Equal(0, await db.BenchmarkComparisons.CountAsync(Ct));
    }

    // --- Default names --------------------------------------------------------------------------------

    [Fact]
    public async Task DefaultName_NamesUpToThreeModels_AndCountsFour()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        long[] runIds = await SeedRunsAsync(options, FourModels);

        await using var db = new ApplicationDbContext(options);
        var service = new BenchmarkComparisonIdentityService(db);

        var (two, _) = await service.EnsureAsync(new[] { runIds[1], runIds[0] }, null, null, UserId, Ct);
        var (three, _) = await service.EnsureAsync(new[] { runIds[2], runIds[0], runIds[1] }, null, null, UserId, Ct);
        var (four, _) = await service.EnsureAsync(Enumerable.Reverse(runIds), null, null, UserId, Ct);

        // Canonical order, by run id, whatever the order of selection.
        Assert.Equal("GPT-5.6 Luna vs Gemini 3.8 Flash", two!.DefaultName);
        Assert.Equal("GPT-5.6 Luna vs Gemini 3.8 Flash vs Claude Opus 5.5", three!.DefaultName);
        Assert.Equal("4 models · " + SuiteName, four!.DefaultName);
        Assert.Equal(four.DefaultName, four.DisplayName);
    }

    [Fact]
    public void DefaultName_OfFourBatteryResults_NamesTheirBattery_AndIsCappedAt160Characters()
    {
        var batteries = new BenchmarkModelComparisonDto
        {
            SubjectKind = BenchmarkModelComparisonSubjectKinds.Batteries,
            Entries = Enumerable.Range(1, 4)
                .Select(i => new BenchmarkModelComparisonEntryDto
                {
                    Key = "battery:" + i, SourceKind = "Battery", SourceId = i, Label = "Model " + i, BatteryName = "Core knowledge"
                })
                .ToList()
        };
        Assert.Equal("4 models · Core knowledge", BenchmarkComparisonIdentityService.DefaultName(batteries));

        var longLabels = new BenchmarkModelComparisonDto
        {
            Entries = Enumerable.Range(1, 2)
                .Select(i => new BenchmarkModelComparisonEntryDto { Key = "run:" + i, SourceKind = "Run", SourceId = i, Label = new string('x', 100) })
                .ToList()
        };
        string name = BenchmarkComparisonIdentityService.DefaultName(longLabels);
        Assert.Equal(BenchmarkComparisonIdentityService.MaxNameLength, name.Length);
        Assert.EndsWith("…", name);
    }

    // --- Rename and list ------------------------------------------------------------------------------

    [Fact]
    public async Task Rename_TrimsTheName_AndAnEmptyNameResetsToTheDefault()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        long[] runIds = await SeedRunsAsync(options, FourModels.Take(2).ToArray());

        await using var db = new ApplicationDbContext(options);
        var service = new BenchmarkComparisonIdentityService(db);
        var (comparison, _) = await service.EnsureAsync(runIds, null, null, UserId, Ct);

        var (renamed, renameError) = await service.RenameAsync(comparison!.Id, "  Frontier models  ", Ct);
        Assert.Null(renameError);
        Assert.Equal("Frontier models", renamed!.Name);
        Assert.Equal("Frontier models", renamed.DisplayName);
        Assert.NotNull(renamed.RenamedAtUtc);

        var (reset, resetError) = await service.RenameAsync(comparison.Id, "   ", Ct);
        Assert.Null(resetError);
        Assert.Null(reset!.Name);
        Assert.Equal(reset.DefaultName, reset.DisplayName);

        var (longest, longestError) = await service.RenameAsync(comparison.Id, new string('n', BenchmarkComparisonIdentityService.MaxNameLength), Ct);
        Assert.Null(longestError);
        Assert.Equal(BenchmarkComparisonIdentityService.MaxNameLength, longest!.Name!.Length);

        var (_, tooLong) = await service.RenameAsync(comparison.Id, new string('n', BenchmarkComparisonIdentityService.MaxNameLength + 1), Ct);
        Assert.Equal(BenchmarkComparisonIdentityService.NameTooLongError, tooLong);

        var (_, missing) = await service.RenameAsync(comparison.Id + 1000, "Anything", Ct);
        Assert.Equal(BenchmarkComparisonIdentityService.NotFoundError, missing);
    }

    [Fact]
    public async Task List_CountsEachComparisonsDocuments_NewestComparisonFirst()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        long[] runIds = await SeedRunsAsync(options, FourModels.Take(2).ToArray());

        await using var db = new ApplicationDbContext(options);
        var service = new BenchmarkComparisonIdentityService(db);
        var (documented, _) = await service.EnsureAsync(runIds, null, null, UserId, Ct);
        var (bare, _) = await service.EnsureAsync(new[] { runIds[0] }, null, null, UserId, Ct);

        var later = BenchmarkReportPackFixture.CreatedAt.AddDays(1);
        db.BenchmarkReportDocuments.Add(Document($"run:{runIds[0]}", RequestJson(runIds), documented!.Id, BenchmarkReportPackFixture.CreatedAt));
        db.BenchmarkReportDocuments.Add(Document($"run:{runIds[1]}", RequestJson(runIds), documented.Id, later));
        await db.SaveChangesAsync(Ct);

        var list = await service.ListAsync(Ct);

        Assert.Equal(new[] { bare!.Id, documented.Id }, list.Select(c => c.Id));
        Assert.Equal(0, list[0].DocumentCount);
        Assert.Null(list[0].LastDocumentAtUtc);
        Assert.Equal(2, list[1].DocumentCount);
        Assert.Equal(later, list[1].LastDocumentAtUtc);
        Assert.Equal(documented.DefaultName, list[1].DisplayName);
        Assert.Equal(2, list[1].EntryCount);
        Assert.Equal(BenchmarkComparisonSubjectKind.Runs, list[1].SubjectKind);
    }

    // --- Backfill -------------------------------------------------------------------------------------

    [Fact]
    public async Task Backfill_LinksReportPackDocumentsToOneComparison_LeavesCompletionDocumentsAlone_AndIsIdempotent()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        long[] runIds = await SeedRunsAsync(options, FourModels.Take(2).ToArray());
        string key = BenchmarkReportComparisonKey.From(runIds, Array.Empty<long>());

        long firstId, secondId, completionId;
        await using (var seed = new ApplicationDbContext(options))
        {
            var first = Document($"run:{runIds[0]}", RequestJson(runIds), null, BenchmarkReportPackFixture.CreatedAt);
            var second = Document($"run:{runIds[1]}", RequestJson(Enumerable.Reverse(runIds).ToArray()), null, BenchmarkReportPackFixture.CreatedAt);
            var completion = Document($"run:{runIds[0]}", RequestJson(new[] { runIds[0] }), null, BenchmarkReportPackFixture.CreatedAt);
            completion.Origin = BenchmarkReportDocumentOrigin.RunCompletion;
            completion.ComparisonKey = BenchmarkReportComparisonKey.From(new[] { runIds[0] }, Array.Empty<long>());
            seed.BenchmarkReportDocuments.AddRange(first, second, completion);
            await seed.SaveChangesAsync(Ct);
            (firstId, secondId, completionId) = (first.Id, second.Id, completion.Id);
        }

        await using (var db = new ApplicationDbContext(options))
        {
            Assert.Equal(2, await BenchmarkReportDocumentBackfill.BackfillComparisonsAsync(db, NullLogger.Instance, Ct));
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var comparison = Assert.Single(await db.BenchmarkComparisons.ToListAsync(Ct));
            Assert.Equal(key, comparison.ComparisonKey);
            Assert.Equal("GPT-5.6 Luna vs Gemini 3.8 Flash", comparison.DefaultName);
            Assert.Equal(UserId, comparison.CreatedByUserId);

            var first = await db.BenchmarkReportDocuments.IgnoreAutoIncludes().SingleAsync(d => d.Id == firstId, Ct);
            var second = await db.BenchmarkReportDocuments.IgnoreAutoIncludes().SingleAsync(d => d.Id == secondId, Ct);
            var completion = await db.BenchmarkReportDocuments.IgnoreAutoIncludes().SingleAsync(d => d.Id == completionId, Ct);

            Assert.Equal(comparison.Id, first.ComparisonId);
            Assert.Equal(comparison.Id, second.ComparisonId);
            Assert.Equal($"[\"run:{runIds[0]}\"]", first.CoveredEntryKeysJson);
            Assert.Equal(BenchmarkReportComparisonKey.ForCoveredSet(new[] { $"run:{runIds[0]}" }), first.CoveredSetKey);
            Assert.Equal($"[\"run:{runIds[1]}\"]", second.CoveredEntryKeysJson);
            Assert.Equal(BenchmarkReportComparisonKey.ForCoveredSet(new[] { $"run:{runIds[1]}" }), second.CoveredSetKey);
            Assert.Equal(BenchmarkReportScope.Model, first.Scope);

            Assert.Null(completion.ComparisonId);
            Assert.Null(completion.CoveredEntryKeysJson);
            Assert.Null(completion.CoveredSetKey);
        }

        await using (var db = new ApplicationDbContext(options))
        {
            Assert.Equal(0, await BenchmarkReportDocumentBackfill.BackfillComparisonsAsync(db, NullLogger.Instance, Ct));
            Assert.Equal(1, await db.BenchmarkComparisons.CountAsync(Ct));
        }
    }

    [Fact]
    public async Task Backfill_ARowWithoutARequest_KeepsANullComparison_ButGetsItsCoveredEntries()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();

        long id;
        await using (var seed = new ApplicationDbContext(options))
        {
            var document = Document("run:12", string.Empty, null, BenchmarkReportPackFixture.CreatedAt);
            document.ComparisonKey = BenchmarkReportComparisonKey.From(new long[] { 12, 13 }, Array.Empty<long>());
            seed.BenchmarkReportDocuments.Add(document);
            await seed.SaveChangesAsync(Ct);
            id = document.Id;
        }

        await using (var db = new ApplicationDbContext(options))
        {
            Assert.Equal(1, await BenchmarkReportDocumentBackfill.BackfillComparisonsAsync(db, NullLogger.Instance, Ct));
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var document = await db.BenchmarkReportDocuments.IgnoreAutoIncludes().SingleAsync(d => d.Id == id, Ct);
            Assert.Null(document.ComparisonId);
            Assert.Equal("[\"run:12\"]", document.CoveredEntryKeysJson);
            Assert.Equal(BenchmarkReportComparisonKey.ForCoveredSet(new[] { "run:12" }), document.CoveredSetKey);
            Assert.Equal(0, await db.BenchmarkComparisons.CountAsync(Ct));

            Assert.Equal(0, await BenchmarkReportDocumentBackfill.BackfillComparisonsAsync(db, NullLogger.Instance, Ct));
        }
    }

    [Fact]
    public async Task Backfill_ARequestWhoseRunsWereDeleted_IsStillNumbered_AndNamedByItsEntryKeys()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();

        await using (var seed = new ApplicationDbContext(options))
        {
            var document = Document("run:901", RequestJson(new long[] { 902, 901 }), null, BenchmarkReportPackFixture.CreatedAt);
            document.ComparisonKey = BenchmarkReportComparisonKey.From(new long[] { 901, 902 }, Array.Empty<long>());
            seed.BenchmarkReportDocuments.Add(document);
            await seed.SaveChangesAsync(Ct);
        }

        await using (var db = new ApplicationDbContext(options))
        {
            Assert.Equal(1, await BenchmarkReportDocumentBackfill.BackfillComparisonsAsync(db, NullLogger.Instance, Ct));
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var comparison = Assert.Single(await db.BenchmarkComparisons.ToListAsync(Ct));
            Assert.Equal("run:901 vs run:902", comparison.DefaultName);
            var document = await db.BenchmarkReportDocuments.IgnoreAutoIncludes().SingleAsync(Ct);
            Assert.Equal(comparison.Id, document.ComparisonId);
        }
    }

    // --- Covered-set key ------------------------------------------------------------------------------

    [Fact]
    public void ForCoveredSet_OfEveryEntry_IsTheComparisonKey_AndRefusesAMixedSet()
    {
        Assert.Equal(
            BenchmarkReportComparisonKey.From(new long[] { 3, 12 }, new long[] { 4 }),
            BenchmarkReportComparisonKey.ForCoveredSet(new[] { "group:4", "run:12", "run:3" }));
        Assert.Equal(
            BenchmarkReportComparisonKey.ForCoveredSet(new[] { "run:3", "run:12" }),
            BenchmarkReportComparisonKey.ForCoveredSet(new[] { "run:12", "run:3" }));

        Assert.Throws<ArgumentException>(() => BenchmarkReportComparisonKey.ForCoveredSet(new[] { "battery:2", "run:1" }));
        Assert.Throws<ArgumentException>(() => BenchmarkReportComparisonKey.ForCoveredSet(Array.Empty<string>()));
    }

    [Fact]
    public void CanonicalEntryKeys_AreRunsThenGroupsThenBatteries_EachByAscendingId()
    {
        Assert.Equal(
            new[] { "run:3", "run:12", "group:4" },
            BenchmarkReportComparisonKey.CanonicalEntryKeys(new long[] { 12, 3, 12 }, new long[] { 4 }));
        Assert.Equal(
            new[] { "battery:2", "battery:7" },
            BenchmarkReportComparisonKey.CanonicalEntryKeys(Array.Empty<long>(), Array.Empty<long>(), new long[] { 7, 2 }));
    }

    // --- Fixtures -------------------------------------------------------------------------------------

    /// <summary>
    /// A three-question suite and one completed run per model, every run at the same thinking level so
    /// that no label carries it; returns the run ids in the order of <paramref name="models"/>, ascending.
    /// </summary>
    private static async Task<long[]> SeedRunsAsync(
        DbContextOptions<ApplicationDbContext> options, params (string Provider, string DisplayName)[] models)
    {
        await using var db = new ApplicationDbContext(options);

        var suite = new BenchmarkSuite { Name = SuiteName };
        for (int i = 1; i <= 3; i++)
        {
            suite.Questions.Add(new BenchmarkQuestion
            {
                QuestionText = $"Q{i}",
                ExpectedPoints = $"- point {i}",
                OrderIndex = i,
                ItemRevision = 1,
                AssessedDifficulty = 50,
                Difficulty = BenchmarkDifficulty.Intermediate
            });
        }
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync();

        var questions = suite.Questions.OrderBy(q => q.OrderIndex).ToList();
        var ids = new List<long>();

        for (int m = 0; m < models.Length; m++)
        {
            var (provider, displayName) = models[m];
            var run = new BenchmarkRun
            {
                BenchmarkSuiteId = suite.Id,
                BenchmarkSuiteIdUsed = suite.Id,
                SuiteName = suite.Name,
                Status = BenchmarkRunStatus.Completed,
                StartedAtUtc = T0.AddHours(m),
                CompletedAtUtc = T0.AddHours(m).AddMinutes(30),
                SpeedIndex = 100,
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(
                    provider: provider,
                    modelId: displayName.ToLowerInvariant().Replace(' ', '-'),
                    displayName: displayName,
                    thinkingLevel: "high"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-pro"),
                CandidatePromptOptionsJson = "{\"verboseMode\":false,\"spoilerFreeMode\":false,\"overseerMode\":0}",
                CandidateSystemPromptSha256 = "e9b3e9a7c4d1b8f0a2e6c9d3b7f1a4e8",
                ToolGuidesSha256 = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7",
                KnowledgeBaseHeadSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8",
                HarnessVersion = "12",
                ScoringMethodVersion = 9,
                MaxParallelQuestionsUsed = 1,
                TotalQuestionCount = 3
            };

            for (int i = 0; i < questions.Count; i++)
            {
                run.Answers.Add(new BenchmarkRunAnswer
                {
                    BenchmarkQuestionId = questions[i].Id,
                    BenchmarkQuestionIdUsed = questions[i].Id,
                    ItemRevisionUsed = 1,
                    ExpectedPointsUsed = questions[i].ExpectedPoints,
                    ExpectedPointsRecorded = true,
                    OrderIndex = questions[i].OrderIndex,
                    QuestionText = questions[i].QuestionText,
                    AnswerText = "An answer.",
                    Difficulty = questions[i].Difficulty,
                    AssessedDifficulty = 50,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = 60 + 10 * i,
                    AccuracyScore = 60 + 10 * i,
                    SpeedScore = 100,
                    DurationMs = 30000,
                    ToolTimeMs = 0,
                    TimeToFirstTokenMs = 900
                });
            }

            db.BenchmarkRuns.Add(run);
            await db.SaveChangesAsync();
            ids.Add(run.Id);
        }

        return ids.ToArray();
    }

    private static string RequestJson(long[] runIds)
        => "{\"runIds\":[" + string.Join(",", runIds) + "],\"groupIds\":[],\"pricingBasis\":1}";

    /// <summary>A stored Report Pack document of the fixture about <paramref name="subjectKey"/>, keyed by its request.</summary>
    private static BenchmarkReportDocument Document(string subjectKey, string requestJson, int? comparisonId, DateTime createdAtUtc)
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        document.Id = 0;
        document.Origin = BenchmarkReportDocumentOrigin.ReportPack;
        document.SubjectKey = subjectKey;
        document.ComparisonRequestJson = requestJson;
        document.ComparisonKey = BenchmarkReportComparisonKey.TryFromEntryKeys(
            ExtractRunKeys(requestJson), out var key) ? key : null;
        document.ComparisonId = comparisonId;
        document.CreatedAtUtc = createdAtUtc;
        document.CreatedByUserId = UserId;
        document.Runs = new List<BenchmarkReportDocumentRun>();
        return document;
    }

    private static IEnumerable<string> ExtractRunKeys(string requestJson)
    {
        if (string.IsNullOrEmpty(requestJson)) return Array.Empty<string>();
        var request = BenchmarkReportJson.Deserialize<BenchmarkModelComparisonRequest>(requestJson);
        return request.RunIds.Select(id => "run:" + id);
    }
}

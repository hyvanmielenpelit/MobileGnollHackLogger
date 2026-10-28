using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Feedback;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The admin GnollBench feedback API: grouping self-reports by fingerprint, the derived status, the accepted scope,
/// every filter, search, the sorts, facets, ids, counts, occurrences, triage and the Overseer cross-link. Every test
/// runs on SQLite (<see cref="SqliteSchemaTemplate"/>), so a query EF cannot translate fails here as it would on SQL
/// Server instead of being evaluated in memory.
/// </summary>
public sealed class AdminGnollBenchFeedbackControllerTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string AdminId = "gnollbench-feedback-admin";
    private const string AdminName = "triager";
    private const string MissingInformation = "missing_information";
    private const string ToolProblem = "tool_problem";
    private const string AblationStamp = "asker-search-words-v1+race-role-tool-v1:abcdef123456";
    private const string AblationName = "asker-search-words-v1+race-role-tool-v1";
    private const string ScarceCorpus = "scarce:no-items";

    private static readonly DateTime T0 = new(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = SqliteSchemaTemplate.OpenCopy();
    private readonly ApplicationDbContext _db;
    private readonly AdminGnollBenchFeedbackController _controller;
    private readonly SystemAiConfigurationSnapshot _modelA = BenchmarkModelSnapshots.Model("OpenAI", "model-a", displayName: "Model A");
    private readonly SystemAiConfigurationSnapshot _modelB = BenchmarkModelSnapshots.Model("Anthropic", "model-b");
    private readonly SystemAiConfigurationSnapshot _assessor = BenchmarkModelSnapshots.Model("Google", "grader");

    public AdminGnollBenchFeedbackControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        _db = new ApplicationDbContext(options);
        _db.Users.Add(new ApplicationUser { Id = AdminId, UserName = AdminName, NormalizedUserName = AdminName.ToUpperInvariant() });
        _db.SaveChanges();

        _controller = new AdminGnollBenchFeedbackController(_db, new GnollBenchFeedbackQuery(_db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, AdminId) }, "Test"))
                }
            }
        };
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // --- Seeding ---

    private static string Fp(int n) => n.ToString("x64");

    private async Task<BenchmarkRun> AddRunAsync(SystemAiConfigurationSnapshot? model = null, string suiteName = "Suite One", long? suiteId = 1,
        BenchmarkRunSeries? series = null, string? condition = null, string? variant = null, string? corpus = null, string? harness = "60")
    {
        var run = new BenchmarkRun
        {
            SuiteName = suiteName,
            BenchmarkSuiteIdUsed = suiteId,
            RunSeries = series,
            TestedModelSnapshot = model ?? _modelA,
            AssessorModelSnapshot = _assessor,
            CandidatePromptOptionsJson = new BenchmarkCandidatePromptOptions { InformationCondition = condition }.ToCanonicalJson(),
            InstructionVariant = variant,
            CorpusCondition = corpus,
            HarnessVersion = harness,
            StartedAtUtc = T0
        };
        _db.BenchmarkRuns.Add(run);
        await _db.SaveChangesAsync(Ct);
        return run;
    }

    private async Task<BenchmarkRunAnswer> AddAnswerAsync(BenchmarkRun run, DateTime? started, long? questionId = 11,
        string question = "How fast is a gnoll lord?")
    {
        var answer = new BenchmarkRunAnswer
        {
            BenchmarkRun = run,
            QuestionText = question,
            AnswerText = "An answer.",
            BenchmarkQuestionIdUsed = questionId,
            StartedAtUtc = started
        };
        _db.BenchmarkRunAnswers.Add(answer);
        await _db.SaveChangesAsync(Ct);
        return answer;
    }

    private async Task<BenchmarkRunAnswerSelfReport> AddReportAsync(BenchmarkRunAnswer answer, string fingerprint, string subject = "Gnoll lord speed missing",
        string category = MissingInformation, bool accepted = true, string summary = "The wiki has no speed for it.", string? details = null,
        string? error = null)
    {
        var report = new BenchmarkRunAnswerSelfReport
        {
            BenchmarkRunAnswer = answer,
            Fingerprint = fingerprint,
            Category = category,
            Subject = subject,
            Summary = summary,
            Details = details,
            Accepted = accepted,
            Error = error,
            RelatedToolsJson = "[\"wiki_search\"]"
        };
        _db.BenchmarkRunAnswerSelfReports.Add(report);
        await _db.SaveChangesAsync(Ct);
        return report;
    }

    /// <summary>One run, one answer at <paramref name="started"/>, one self-report.</summary>
    private async Task<BenchmarkRunAnswerSelfReport> AddSimpleReportAsync(string fingerprint, DateTime started, string subject = "Gnoll lord speed missing",
        bool accepted = true, SystemAiConfigurationSnapshot? model = null)
    {
        var run = await AddRunAsync(model);
        var answer = await AddAnswerAsync(run, started);
        return await AddReportAsync(answer, fingerprint, subject, accepted: accepted);
    }

    private async Task AddTriageAsync(string fingerprint, BenchmarkFeedbackTriageStatus status, DateTime? resolvedUtc = null,
        string? resolvedVia = null)
    {
        _db.BenchmarkFeedbackTriages.Add(new BenchmarkFeedbackTriage
        {
            Fingerprint = fingerprint, Status = status, ResolvedUtc = resolvedUtc, ResolvedVia = resolvedVia
        });
        await _db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// Four runs with one issue each. A: model A, suite 1, production, live, harness 60, question 11. B: model B, suite 2,
    /// source code only, two experimental changes, scarce corpus, harness 61, question 22, in series S. C: model A, suite 1,
    /// closed book, race-role-tool-v1, live, harness 9, question 11, in group G and battery run R. D: model B, suite 2,
    /// production, live, harness 60, question 22, in nothing. Batch M names run A, series S and battery run R.
    /// </summary>
    private sealed record FilterFixture(BenchmarkRun A, BenchmarkRun B, BenchmarkRun C, BenchmarkRun D,
        long SeriesId, long GroupId, long BatteryRunId, long BatchId)
    {
        public static string FpA => Fp(0xA);
        public static string FpB => Fp(0xB);
        public static string FpC => Fp(0xC);
        public static string FpD => Fp(0xD);
    }

    private async Task<FilterFixture> SeedFilterFixtureAsync()
    {
        var series = new BenchmarkRunSeries { SuiteName = "Suite Two", StartRequestJson = "{}" };
        _db.BenchmarkRunSeries.Add(series);
        await _db.SaveChangesAsync(Ct);

        var a = await AddRunAsync(_modelA, "Suite One", 1, harness: "60");
        var b = await AddRunAsync(_modelB, "Suite Two", 2, series, BenchmarkInformationConditions.SourceOnlyWire, AblationStamp, ScarceCorpus, "61");
        var c = await AddRunAsync(_modelA, "Suite One", 1, null, BenchmarkInformationConditions.ClosedBookWire, "race-role-tool-v1:0123456789ab", null, "9");
        var d = await AddRunAsync(_modelB, "Suite Two", 2, harness: "60");

        await AddReportAsync(await AddAnswerAsync(a, T0.AddHours(1), 11, "Question eleven about gnolls"), FilterFixture.FpA, "Issue A", ToolProblem);
        await AddReportAsync(await AddAnswerAsync(b, T0.AddHours(2), 22, "Question twenty-two"), FilterFixture.FpB, "Issue B");
        await AddReportAsync(await AddAnswerAsync(c, T0.AddHours(3), 11, "Question eleven about gnolls"), FilterFixture.FpC, "Issue C");
        await AddReportAsync(await AddAnswerAsync(d, T0.AddHours(4), 22, "Question twenty-two"), FilterFixture.FpD, "Issue D");

        var group = new BenchmarkRunGroup { Name = "Group G" };
        group.Members.Add(new BenchmarkRunGroupMember { BenchmarkRun = c });
        _db.BenchmarkRunGroups.Add(group);

        var battery = new BenchmarkBatteryRun { BatteryName = "Battery R", DefinitionJson = "{}", DefinitionSha256 = new string('0', 64), StartRequestJson = "{}" };
        battery.Members.Add(new BenchmarkBatteryRunMember { BenchmarkRun = c, SuiteIndex = 0, Round = 1 });
        _db.BenchmarkBatteryRuns.Add(battery);
        await _db.SaveChangesAsync(Ct);

        var batch = new BenchmarkModelBatchRun { StartRequestJson = "{}", TargetName = "Mixed" };
        batch.Members.Add(new BenchmarkModelBatchMember { OrderIndex = 0, TestedModelConfigurationId = 1, BenchmarkRun = a });
        batch.Members.Add(new BenchmarkModelBatchMember { OrderIndex = 1, TestedModelConfigurationId = 2, BenchmarkRunSeries = series });
        batch.Members.Add(new BenchmarkModelBatchMember { OrderIndex = 2, TestedModelConfigurationId = 3, BenchmarkBatteryRun = battery });
        _db.BenchmarkModelBatchRuns.Add(batch);
        await _db.SaveChangesAsync(Ct);

        return new FilterFixture(a, b, c, d, series.Id, group.Id, battery.Id, batch.Id);
    }

    // --- Calls ---

    private static T OkValue<T>(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value);
    }

    private async Task<GnollBenchFeedbackIssuePageDto> ListAsync(GnollBenchFeedbackFilterQuery? filter = null, int skip = 0, int take = 100)
        => OkValue<GnollBenchFeedbackIssuePageDto>(await _controller.ListIssues(filter ?? new GnollBenchFeedbackFilterQuery(), skip, take, Ct));

    private async Task<string[]> MatchingAsync(GnollBenchFeedbackFilterQuery filter)
        => (await ListAsync(filter)).Rows.Select(r => r.Fingerprint).OrderBy(f => f, StringComparer.Ordinal).ToArray();

    private static string[] Sorted(params string[] fingerprints) => fingerprints.OrderBy(f => f, StringComparer.Ordinal).ToArray();

    // --- Grouping ---

    [Fact]
    public async Task ListIssues_GroupsSelfReportsByFingerprint_WithAggregatesOverEveryRun()
    {
        var shared = Fp(1);
        var other = Fp(2);
        var runA = await AddRunAsync(_modelA, "Suite One", 1);
        var runB = await AddRunAsync(_modelB, "Suite Two", 2);
        await AddReportAsync(await AddAnswerAsync(runA, T0.AddHours(1)), shared, "Old subject");
        await AddReportAsync(await AddAnswerAsync(runA, T0.AddHours(2)), shared, "Old subject");
        await AddReportAsync(await AddAnswerAsync(runB, T0.AddHours(5)), shared, "Newest subject");
        await AddReportAsync(await AddAnswerAsync(runB, T0.AddHours(3)), other, "Other issue", ToolProblem);

        var page = await ListAsync();

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { shared, other }, page.Rows.Select(r => r.Fingerprint));
        var issue = page.Rows[0];
        Assert.Equal(3, issue.SelfReportCount);
        Assert.Equal(2, issue.RunCount);
        Assert.Equal(2, issue.ModelCount);
        Assert.Equal(new[] { "Model A", "model-b" }, issue.Models);
        Assert.Equal(new[] { "Anthropic", "OpenAI" }, issue.Providers);
        Assert.Equal(new[] { "Suite One", "Suite Two" }, issue.Suites);
        Assert.Equal("Newest subject", issue.Subject);
        Assert.Equal(MissingInformation, issue.Category);
        Assert.Equal(T0.AddHours(1), issue.FirstSeenUtc);
        Assert.Equal(T0.AddHours(5), issue.LastSeenUtc);
        Assert.Equal(GnollBenchFeedbackStatuses.New, issue.Status);
        Assert.Null(issue.AlsoInOverseer);
        Assert.Equal(ToolProblem, page.Rows[1].Category);
    }

    [Fact]
    public async Task ListIssues_TimeFallsBackToTheRunStart_WhenTheAnswerHasNone()
    {
        await AddSimpleReportAsync(Fp(1), started: T0.AddDays(3));
        var run = await AddRunAsync();
        await AddReportAsync(await AddAnswerAsync(run, started: null), Fp(2));

        var page = await ListAsync();

        Assert.Equal(new[] { Fp(1), Fp(2) }, page.Rows.Select(r => r.Fingerprint));
        Assert.Equal(T0, page.Rows[1].LastSeenUtc);
    }

    [Fact]
    public async Task ListIssues_IgnoresSelfReportsWithoutAFingerprint()
    {
        var run = await AddRunAsync();
        var answer = await AddAnswerAsync(run, T0);
        _db.BenchmarkRunAnswerSelfReports.Add(new BenchmarkRunAnswerSelfReport { BenchmarkRunAnswer = answer, Accepted = false, Error = "Arguments did not parse." });
        await _db.SaveChangesAsync(Ct);

        Assert.Equal(0, (await ListAsync(new GnollBenchFeedbackFilterQuery { Accepted = "all" })).TotalCount);
    }

    // --- Derived status ---

    [Fact]
    public async Task Status_IsNewWithoutARow_AndRegressedWhenResolvedAndReportedAgain()
    {
        await AddSimpleReportAsync(Fp(1), T0.AddDays(2));
        await AddTriageAsync(Fp(1), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(1));
        await AddSimpleReportAsync(Fp(2), T0);
        await AddTriageAsync(Fp(2), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(1));
        await AddSimpleReportAsync(Fp(3), T0);
        await AddSimpleReportAsync(Fp(4), T0);
        await AddTriageAsync(Fp(4), BenchmarkFeedbackTriageStatus.Dismissed);

        var page = await ListAsync();
        var byFp = page.Rows.ToDictionary(r => r.Fingerprint, r => r.Status);

        Assert.Equal(GnollBenchFeedbackStatuses.Regressed, byFp[Fp(1)]);
        Assert.Equal(GnollBenchFeedbackStatuses.Resolved, byFp[Fp(2)]);
        Assert.Equal(GnollBenchFeedbackStatuses.New, byFp[Fp(3)]);
        Assert.Equal(GnollBenchFeedbackStatuses.Dismissed, byFp[Fp(4)]);

        Assert.Equal(new[] { Fp(1) }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Status = "regressed" }));
        Assert.Equal(Sorted(Fp(2), Fp(3)), await MatchingAsync(new GnollBenchFeedbackFilterQuery { Status = "resolved,new" }));
        Assert.IsType<BadRequestObjectResult>(await _controller.ListIssues(new GnollBenchFeedbackFilterQuery { Status = "fixed" }, ct: Ct));

        var counts = OkValue<GnollBenchFeedbackCountsDto>(await _controller.GetCounts(Ct));
        Assert.Equal(1, counts.New);
        Assert.Equal(0, counts.Acknowledged);
        Assert.Equal(1, counts.Resolved);
        Assert.Equal(1, counts.Regressed);
        Assert.Equal(1, counts.Dismissed);
    }

    [Fact]
    public async Task Status_RegressionReadsAcceptedReportsOnly()
    {
        var run = await AddRunAsync();
        await AddReportAsync(await AddAnswerAsync(run, T0), Fp(1));
        await AddReportAsync(await AddAnswerAsync(run, T0.AddDays(5)), Fp(1), accepted: false, error: "Refused.");
        await AddTriageAsync(Fp(1), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(1));

        var all = await ListAsync(new GnollBenchFeedbackFilterQuery { Accepted = "all" });

        Assert.Equal(GnollBenchFeedbackStatuses.Resolved, Assert.Single(all.Rows).Status);
    }

    // --- Accepted scope ---

    [Fact]
    public async Task Accepted_DefaultsToAccepted_AndAllOrRefusedWidenIt()
    {
        await AddSimpleReportAsync(Fp(1), T0);
        await AddSimpleReportAsync(Fp(2), T0, accepted: false);
        var run = await AddRunAsync();
        await AddReportAsync(await AddAnswerAsync(run, T0), Fp(3));
        await AddReportAsync(await AddAnswerAsync(run, T0.AddHours(1)), Fp(3), accepted: false);

        var accepted = await ListAsync();
        Assert.Equal(Sorted(Fp(1), Fp(3)), accepted.Rows.Select(r => r.Fingerprint).OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(1, accepted.Rows.Single(r => r.Fingerprint == Fp(3)).SelfReportCount);

        var all = await ListAsync(new GnollBenchFeedbackFilterQuery { Accepted = "all" });
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(2, all.Rows.Single(r => r.Fingerprint == Fp(3)).SelfReportCount);

        var refused = await ListAsync(new GnollBenchFeedbackFilterQuery { Accepted = "refused" });
        Assert.Equal(Sorted(Fp(2), Fp(3)), refused.Rows.Select(r => r.Fingerprint).OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(1, refused.Rows.Single(r => r.Fingerprint == Fp(3)).SelfReportCount);

        Assert.IsType<BadRequestObjectResult>(await _controller.ListIssues(new GnollBenchFeedbackFilterQuery { Accepted = "maybe" }, ct: Ct));
    }

    // --- Filters ---

    [Fact]
    public async Task Filters_RunSeriesGroupBatteryAndBatch()
    {
        var f = await SeedFilterFixtureAsync();

        Assert.Equal(new[] { FilterFixture.FpA }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Run = f.A.Id.ToString() }));
        Assert.Equal(Sorted(FilterFixture.FpA, FilterFixture.FpB), await MatchingAsync(new GnollBenchFeedbackFilterQuery { Run = $"{f.A.Id},{f.B.Id}" }));
        Assert.Equal(new[] { FilterFixture.FpB }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Series = f.SeriesId.ToString() }));
        Assert.Equal(new[] { FilterFixture.FpC }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Group = f.GroupId.ToString() }));
        Assert.Equal(new[] { FilterFixture.FpC }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Battery = f.BatteryRunId.ToString() }));
        Assert.Equal(Sorted(FilterFixture.FpA, FilterFixture.FpB, FilterFixture.FpC),
            await MatchingAsync(new GnollBenchFeedbackFilterQuery { Batch = f.BatchId.ToString() }));
        Assert.Empty(await MatchingAsync(new GnollBenchFeedbackFilterQuery { Batch = f.BatchId.ToString(), Run = f.D.Id.ToString() }));
        Assert.IsType<BadRequestObjectResult>(await _controller.ListIssues(new GnollBenchFeedbackFilterQuery { Run = "seven" }, ct: Ct));
    }

    [Fact]
    public async Task Filters_ModelProviderSuiteQuestionAndCategory()
    {
        await SeedFilterFixtureAsync();

        Assert.Equal(Sorted(FilterFixture.FpB, FilterFixture.FpD), await MatchingAsync(new GnollBenchFeedbackFilterQuery { Model = "model-b" }));
        Assert.Equal(Sorted(FilterFixture.FpB, FilterFixture.FpD), await MatchingAsync(new GnollBenchFeedbackFilterQuery { Provider = "Anthropic" }));
        Assert.Equal(Sorted(FilterFixture.FpB, FilterFixture.FpD), await MatchingAsync(new GnollBenchFeedbackFilterQuery { Suite = "2" }));
        Assert.Equal(Sorted(FilterFixture.FpA, FilterFixture.FpC), await MatchingAsync(new GnollBenchFeedbackFilterQuery { Question = "11" }));
        Assert.Equal(new[] { FilterFixture.FpA }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Category = ToolProblem }));
        Assert.Equal(new[] { FilterFixture.FpC }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Model = "model-a", Question = "11", Category = MissingInformation }));
        Assert.IsType<BadRequestObjectResult>(await _controller.ListIssues(new GnollBenchFeedbackFilterQuery { Category = "complaint" }, ct: Ct));
    }

    [Fact]
    public async Task Filters_ConditionCorpusVariantAndHarness()
    {
        await SeedFilterFixtureAsync();

        Assert.Equal(new[] { FilterFixture.FpB }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Condition = "sourceOnly" }));
        Assert.Equal(new[] { FilterFixture.FpC }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Condition = "closedBook" }));
        Assert.Equal(Sorted(FilterFixture.FpA, FilterFixture.FpD), await MatchingAsync(new GnollBenchFeedbackFilterQuery { Condition = "production" }));
        Assert.IsType<BadRequestObjectResult>(await _controller.ListIssues(new GnollBenchFeedbackFilterQuery { Condition = "openBook" }, ct: Ct));

        Assert.Equal(Sorted(FilterFixture.FpA, FilterFixture.FpC, FilterFixture.FpD), await MatchingAsync(new GnollBenchFeedbackFilterQuery { Corpus = "live" }));
        Assert.Equal(new[] { FilterFixture.FpB }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Corpus = ScarceCorpus }));

        Assert.Equal(Sorted(FilterFixture.FpA, FilterFixture.FpD), await MatchingAsync(new GnollBenchFeedbackFilterQuery { Variant = "none" }));
        Assert.Equal(new[] { FilterFixture.FpB }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Variant = AblationName }));
        // A query string decodes '+' to a space, and the ids may come in any order.
        Assert.Equal(new[] { FilterFixture.FpB }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Variant = "race-role-tool-v1 asker-search-words-v1" }));
        Assert.Equal(new[] { FilterFixture.FpC }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Variant = "race-role-tool-v1" }));
        Assert.Empty(await MatchingAsync(new GnollBenchFeedbackFilterQuery { Variant = "asker-character-procedure-v1" }));
        Assert.Empty(await MatchingAsync(new GnollBenchFeedbackFilterQuery { Variant = AblationStamp }));

        Assert.Equal(Sorted(FilterFixture.FpA, FilterFixture.FpD), await MatchingAsync(new GnollBenchFeedbackFilterQuery { Harness = "60" }));
        Assert.Equal(Sorted(FilterFixture.FpB, FilterFixture.FpC), await MatchingAsync(new GnollBenchFeedbackFilterQuery { Harness = "61,9" }));

        Assert.Empty(await MatchingAsync(new GnollBenchFeedbackFilterQuery { Condition = "sourceOnly", Variant = "none" }));
    }

    [Fact]
    public async Task Filters_OneSelfReportMustMatchEveryOccurrenceFilter()
    {
        var runA = await AddRunAsync(_modelA, "Suite One", 1);
        var runB = await AddRunAsync(_modelB, "Suite Two", 2);
        await AddReportAsync(await AddAnswerAsync(runA, T0, questionId: 11), Fp(1));
        await AddReportAsync(await AddAnswerAsync(runB, T0, questionId: 22), Fp(1));

        Assert.Empty(await MatchingAsync(new GnollBenchFeedbackFilterQuery { Model = "model-a", Question = "22" }));
        var page = await ListAsync(new GnollBenchFeedbackFilterQuery { Model = "model-a", Question = "11" });
        Assert.Equal(2, Assert.Single(page.Rows).SelfReportCount);
    }

    [Fact]
    public async Task Filters_FingerprintsAndDateRange()
    {
        await AddSimpleReportAsync(Fp(1), T0);
        await AddSimpleReportAsync(Fp(2), T0.AddDays(10));
        var run = await AddRunAsync();
        await AddReportAsync(await AddAnswerAsync(run, T0), Fp(3));
        await AddReportAsync(await AddAnswerAsync(run, T0.AddDays(20)), Fp(3));

        Assert.Equal(Sorted(Fp(1), Fp(2)), await MatchingAsync(new GnollBenchFeedbackFilterQuery { Fingerprints = $"{Fp(1)},{Fp(2).ToUpperInvariant()}" }));

        // The issue's active span overlaps the range: Fp(3) spans day 0 to day 20.
        var window = new GnollBenchFeedbackFilterQuery { StartDate = T0.AddDays(5).Date, EndDate = T0.AddDays(12).Date };
        Assert.Equal(Sorted(Fp(2), Fp(3)), await MatchingAsync(window));
        Assert.Equal(Sorted(Fp(1), Fp(3)), await MatchingAsync(new GnollBenchFeedbackFilterQuery { EndDate = T0.Date }));

        var tooMany = string.Join(",", Enumerable.Range(1, GnollBenchFeedbackFilter.MaxFingerprints + 1).Select(Fp));
        Assert.IsType<BadRequestObjectResult>(await _controller.ListIssues(new GnollBenchFeedbackFilterQuery { Fingerprints = tooMany }, ct: Ct));
    }

    [Fact]
    public async Task Search_CoversSubjectSummaryDetailsAndQuestionText_IgnoringCase()
    {
        var run = await AddRunAsync();
        await AddReportAsync(await AddAnswerAsync(run, T0, question: "Where is the Oracle?"), Fp(1), "Subject one", summary: "Plain.");
        await AddReportAsync(await AddAnswerAsync(run, T0), Fp(2), "Subject two", summary: "Mentions MITHRIL coats.");
        await AddReportAsync(await AddAnswerAsync(run, T0), Fp(3), "Subject three", summary: "Plain.", details: "The engraving table was empty.");
        await AddReportAsync(await AddAnswerAsync(run, T0), Fp(4), "Gnome lord stats", summary: "Plain.");

        Assert.Equal(new[] { Fp(1) }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Search = "oracle" }));
        Assert.Equal(new[] { Fp(2) }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Search = "mithril" }));
        Assert.Equal(new[] { Fp(3) }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Search = "ENGRAVING" }));
        Assert.Equal(new[] { Fp(4) }, await MatchingAsync(new GnollBenchFeedbackFilterQuery { Search = "gnome LORD" }));
        Assert.IsType<BadRequestObjectResult>(await _controller.ListIssues(
            new GnollBenchFeedbackFilterQuery { Search = new string('x', GnollBenchFeedbackFilter.MaxSearchLength + 1) }, ct: Ct));
    }

    // --- Sorts, paging and ids ---

    [Fact]
    public async Task Sorts_OrderTheIssues()
    {
        // Fp(1): 1 report, newest. Fp(2): 3 reports in 1 run, oldest first seen. Fp(3): 2 reports in 2 runs of 2 models.
        await AddSimpleReportAsync(Fp(1), T0.AddDays(9), "Bravo");
        var run = await AddRunAsync();
        await AddReportAsync(await AddAnswerAsync(run, T0), Fp(2), "charlie");
        await AddReportAsync(await AddAnswerAsync(run, T0.AddDays(1)), Fp(2), "charlie");
        await AddReportAsync(await AddAnswerAsync(run, T0.AddDays(2)), Fp(2), "charlie");
        await AddSimpleReportAsync(Fp(3), T0.AddDays(3), "alpha", model: _modelA);
        await AddSimpleReportAsync(Fp(3), T0.AddDays(4), "alpha", model: _modelB);

        async Task<string[]> Order(string? sort) => (await ListAsync(new GnollBenchFeedbackFilterQuery { Sort = sort })).Rows.Select(r => r.Fingerprint).ToArray();

        Assert.Equal(new[] { Fp(1), Fp(3), Fp(2) }, await Order(null));
        Assert.Equal(new[] { Fp(1), Fp(3), Fp(2) }, await Order("lastSeen"));
        Assert.Equal(new[] { Fp(1), Fp(3), Fp(2) }, await Order("firstSeen"));
        Assert.Equal(new[] { Fp(2), Fp(3), Fp(1) }, await Order("count"));
        Assert.Equal(new[] { Fp(3), Fp(1), Fp(2) }, await Order("runs"));
        Assert.Equal(new[] { Fp(3), Fp(1), Fp(2) }, await Order("models"));
        Assert.Equal(new[] { Fp(3), Fp(1), Fp(2) }, await Order("subject"));
        Assert.IsType<BadRequestObjectResult>(await _controller.ListIssues(new GnollBenchFeedbackFilterQuery { Sort = "users" }, ct: Ct));

        var page = await ListAsync(new GnollBenchFeedbackFilterQuery { Sort = "count" }, skip: 1, take: 1);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(Fp(3), Assert.Single(page.Rows).Fingerprint);

        var ids = OkValue<GnollBenchFeedbackIdsDto>(await _controller.ListIssueIds(new GnollBenchFeedbackFilterQuery { Sort = "count" }, Ct));
        Assert.Equal(new[] { Fp(2), Fp(3), Fp(1) }, ids.Fingerprints);
        Assert.Equal(3, ids.TotalCount);
        Assert.False(ids.Truncated);
    }

    // --- Occurrences ---

    [Fact]
    public async Task Occurrences_AreNewestFirst_FilteredUnlessAll_WithTheRunFacts()
    {
        var f = await SeedFilterFixtureAsync();
        await AddReportAsync(await AddAnswerAsync(f.D, T0.AddHours(9), 22, "Question twenty-two"), FilterFixture.FpB, "Issue B again");

        var filtered = OkValue<GnollBenchFeedbackOccurrencePageDto>(await _controller.ListOccurrences(FilterFixture.FpB,
            new GnollBenchFeedbackFilterQuery { Run = f.B.Id.ToString() }, ct: Ct));
        var row = Assert.Single(filtered.Rows);
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal(f.B.Id, row.RunId);
        Assert.Equal(f.SeriesId, row.SeriesId);
        Assert.Equal(2, row.SuiteId);
        Assert.Equal("Suite Two", row.SuiteName);
        Assert.Equal(22, row.QuestionId);
        Assert.Equal("Question twenty-two", row.QuestionText);
        Assert.Equal("model-b", row.ModelId);
        Assert.Equal("model-b", row.ModelLabel);
        Assert.Equal("Anthropic", row.Provider);
        Assert.Equal(BenchmarkInformationConditions.SourceOnlyWire, row.Condition);
        Assert.Equal(AblationStamp, row.Variant);
        Assert.Equal(AblationName, row.VariantName);
        Assert.Equal(ScarceCorpus, row.Corpus);
        Assert.Equal("61", row.Harness);
        Assert.True(row.Accepted);
        Assert.Equal(new[] { "wiki_search" }, row.RelatedTools);
        Assert.Equal(T0.AddHours(2), row.OccurredUtc);

        var all = OkValue<GnollBenchFeedbackOccurrencePageDto>(await _controller.ListOccurrences(FilterFixture.FpB,
            new GnollBenchFeedbackFilterQuery { Run = f.B.Id.ToString() }, all: true, ct: Ct));
        Assert.Equal(2, all.TotalCount);
        Assert.Equal(new[] { f.D.Id, f.B.Id }, all.Rows.Select(r => r.RunId));
        Assert.Equal(BenchmarkInformationConditions.ProductionWire, all.Rows[0].Condition);
        Assert.Equal("live", all.Rows[0].Corpus);
        Assert.Null(all.Rows[0].Variant);

        Assert.IsType<NotFoundObjectResult>(await _controller.ListOccurrences(Fp(0xEE), null, ct: Ct));
        Assert.IsType<NotFoundObjectResult>(await _controller.ListOccurrences("not-a-fingerprint", null, ct: Ct));
    }

    // --- Facets ---

    [Fact]
    public async Task Facets_ListTheValuesPresent_WithIssueCounts()
    {
        var f = await SeedFilterFixtureAsync();

        var facets = OkValue<GnollBenchFeedbackFacetsDto>(await _controller.GetFacets(ct: Ct));

        Assert.Equal(new[] { MissingInformation, ToolProblem }, facets.Categories.Select(c => c.Value));
        Assert.Equal(3, facets.Categories[0].Count);
        Assert.Equal(new[] { ("model-a", "Model A", 2), ("model-b", "model-b", 2) }, facets.Models.Select(m => (m.Value, m.Label, m.Count)));
        Assert.Equal(new[] { "Anthropic", "OpenAI" }, facets.Providers.Select(p => p.Value));
        Assert.Equal(new[] { f.D.Id, f.C.Id, f.B.Id, f.A.Id }, facets.Runs.Select(r => r.Id));
        Assert.Equal(f.SeriesId, Assert.Single(facets.Series).Id);
        Assert.Equal(("Group G", 1), (Assert.Single(facets.Groups).Name, facets.Groups[0].Count));
        Assert.Equal(f.BatteryRunId, Assert.Single(facets.Batteries).Id);
        var batch = Assert.Single(facets.Batches);
        Assert.Equal((f.BatchId, 3), (batch.Id, batch.Count));
        Assert.Equal(new[] { ("Suite One", 2), ("Suite Two", 2) }, facets.Suites.Select(s => (s.Name, s.Count)));
        Assert.Equal(new[] { (11L, "Question eleven about gnolls", 2), (22L, "Question twenty-two", 2) },
            facets.Questions.Select(q => (q.Id, q.Name, q.Count)));
        Assert.Equal(new[] { "production", "sourceOnly", "closedBook" }, facets.Conditions.Select(c => c.Value));
        Assert.Equal(new[] { "live", ScarceCorpus }, facets.Corpora.Select(c => c.Value));
        Assert.Equal(new[] { "none", AblationName, "race-role-tool-v1" }, facets.Variants.Select(v => v.Value));
        Assert.Equal(new[] { "9", "60", "61" }, facets.Harnesses.Select(h => h.Value));
        Assert.Equal(2, facets.Harnesses[1].Count);
    }

    [Fact]
    public async Task ResolvedVia_FiltersByTheTriageRow_AndAnIssueWithoutOneReadsAsNone()
    {
        await AddSimpleReportAsync(Fp(1), T0);
        await AddSimpleReportAsync(Fp(2), T0);
        await AddSimpleReportAsync(Fp(3), T0);
        await AddSimpleReportAsync(Fp(4), T0);
        await AddTriageAsync(Fp(1), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(1), "wiki");
        await AddTriageAsync(Fp(2), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(1), "wiki,knowledge_base");
        await AddTriageAsync(Fp(3), BenchmarkFeedbackTriageStatus.Acknowledged);

        Assert.Equal(Sorted(Fp(1), Fp(2)), await MatchingAsync(new GnollBenchFeedbackFilterQuery { ResolvedVia = "wiki" }));
        Assert.Equal(Sorted(Fp(2)), await MatchingAsync(new GnollBenchFeedbackFilterQuery { ResolvedVia = "knowledge_base,other" }));
        Assert.Equal(Sorted(Fp(3), Fp(4)), await MatchingAsync(new GnollBenchFeedbackFilterQuery { ResolvedVia = "none" }));
        Assert.Equal(Sorted(Fp(2), Fp(3), Fp(4)),
            await MatchingAsync(new GnollBenchFeedbackFilterQuery { ResolvedVia = "knowledge_base,none" }));

        var bad = Assert.IsType<BadRequestObjectResult>(await _controller.ListIssues(
            new GnollBenchFeedbackFilterQuery { ResolvedVia = "wiki,bogus" }, 0, 100, Ct));
        string message = System.Text.Json.JsonSerializer.Serialize(bad.Value);
        Assert.Contains("Unknown resolved-via value", message);
        Assert.Contains("bogus", message);
    }

    [Fact]
    public async Task Facets_CountResolvedViaPerIssueInOrder_WithNoneLast()
    {
        await AddSimpleReportAsync(Fp(1), T0);
        await AddSimpleReportAsync(Fp(1), T0.AddHours(1));
        await AddSimpleReportAsync(Fp(2), T0);
        await AddSimpleReportAsync(Fp(3), T0);
        await AddSimpleReportAsync(Fp(4), T0);
        await AddSimpleReportAsync(Fp(5), T0.AddDays(20));
        await AddTriageAsync(Fp(1), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(1), "server_configuration,other");
        await AddTriageAsync(Fp(2), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(1), "other");
        await AddTriageAsync(Fp(3), BenchmarkFeedbackTriageStatus.Dismissed);
        await AddTriageAsync(Fp(5), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(21), "wiki");

        var facets = OkValue<GnollBenchFeedbackFacetsDto>(await _controller.GetFacets(T0.Date, T0.AddDays(5).Date, ct: Ct));

        Assert.Equal(new[] { ("server_configuration", 1), ("other", 2), ("none", 2) },
            facets.ResolvedVia.Select(o => (o.Value, o.Count)));
    }

    [Fact]
    public async Task Facets_AreRestrictedToTheRange()
    {
        await AddSimpleReportAsync(Fp(1), T0, model: _modelA);
        await AddSimpleReportAsync(Fp(2), T0.AddDays(10), model: _modelB);

        var facets = OkValue<GnollBenchFeedbackFacetsDto>(await _controller.GetFacets(T0.AddDays(5).Date, T0.AddDays(12).Date, ct: Ct));

        Assert.Equal("model-b", Assert.Single(facets.Models).Value);
        Assert.IsType<BadRequestObjectResult>(await _controller.GetFacets(accepted: "sometimes", ct: Ct));
    }

    // --- Triage ---

    [Fact]
    public async Task SetStatus_CreatesTheRow_AndRecordsTheTriager()
    {
        await AddSimpleReportAsync(Fp(1), T0);

        var dto = OkValue<GnollBenchFeedbackIssueDto>(await _controller.SetStatus(Fp(1),
            new GnollBenchFeedbackStatusUpdateDto { Status = "acknowledged", Note = "  Looking into it.  ", Resolution = "Added the speed.", FixedInVersion = "1.4.0" }));

        Assert.Equal(GnollBenchFeedbackStatuses.Acknowledged, dto.Status);
        Assert.Equal("Looking into it.", dto.TriageNote);
        Assert.Equal("Added the speed.", dto.Resolution);
        Assert.Equal("1.4.0", dto.FixedInVersion);
        Assert.Equal(AdminName, dto.TriagedByUserName);
        Assert.NotNull(dto.TriagedUtc);
        Assert.Null(dto.ResolvedUtc);

        var row = await _db.BenchmarkFeedbackTriages.AsNoTracking().SingleAsync(t => t.Fingerprint == Fp(1), Ct);
        Assert.Equal(BenchmarkFeedbackTriageStatus.Acknowledged, row.Status);
        Assert.Equal(AdminId, row.TriagedByUserId);
    }

    [Fact]
    public async Task SetStatus_AbsentFieldsKeep_AndEmptyOnesClear()
    {
        await AddSimpleReportAsync(Fp(1), T0);
        await _controller.SetStatus(Fp(1), new GnollBenchFeedbackStatusUpdateDto { Status = "acknowledged", Note = "n1", Resolution = "r1", FixedInVersion = "1.4.0" });

        var kept = OkValue<GnollBenchFeedbackIssueDto>(await _controller.SetStatus(Fp(1), new GnollBenchFeedbackStatusUpdateDto { Status = "dismissed" }));
        Assert.Equal(GnollBenchFeedbackStatuses.Dismissed, kept.Status);
        Assert.Equal(("n1", "r1", "1.4.0"), (kept.TriageNote, kept.Resolution, kept.FixedInVersion));

        var cleared = OkValue<GnollBenchFeedbackIssueDto>(await _controller.SetStatus(Fp(1),
            new GnollBenchFeedbackStatusUpdateDto { Status = "dismissed", Note = "", FixedInVersion = "  " }));
        Assert.Null(cleared.TriageNote);
        Assert.Equal("r1", cleared.Resolution);
        Assert.Null(cleared.FixedInVersion);
    }

    [Fact]
    public async Task SetStatus_ResolvedVia_IsKeptWhenAbsent_ClearedWhenEmpty_AndUnknownIdsAreRefused()
    {
        await AddSimpleReportAsync(Fp(1), T0);

        var set = OkValue<GnollBenchFeedbackIssueDto>(await _controller.SetStatus(Fp(1),
            new GnollBenchFeedbackStatusUpdateDto { Status = "resolved", ResolvedVia = new List<string> { "other", "wiki" } }));
        Assert.Equal(new[] { "wiki", "other" }, set.ResolvedVia);
        Assert.Equal("wiki,other", (await _db.BenchmarkFeedbackTriages.SingleAsync(Ct)).ResolvedVia);

        var kept = OkValue<GnollBenchFeedbackIssueDto>(await _controller.SetStatus(Fp(1), new GnollBenchFeedbackStatusUpdateDto { Status = "resolved" }));
        Assert.Equal(new[] { "wiki", "other" }, kept.ResolvedVia);

        Assert.IsType<BadRequestObjectResult>(await _controller.SetStatus(Fp(1),
            new GnollBenchFeedbackStatusUpdateDto { Status = "resolved", ResolvedVia = new List<string> { "Wiki" } }));

        var cleared = OkValue<GnollBenchFeedbackIssueDto>(await _controller.SetStatus(Fp(1),
            new GnollBenchFeedbackStatusUpdateDto { Status = "acknowledged", ResolvedVia = new List<string>() }));
        Assert.Empty(cleared.ResolvedVia);
        Assert.Null((await _db.BenchmarkFeedbackTriages.AsNoTracking().SingleAsync(Ct)).ResolvedVia);
    }

    [Fact]
    public async Task SetStatus_ResolvedSetsResolvedUtc_AndOtherStatusesClearIt()
    {
        await AddSimpleReportAsync(Fp(1), T0);
        DateTime before = DateTime.UtcNow.AddSeconds(-1);

        var resolved = OkValue<GnollBenchFeedbackIssueDto>(await _controller.SetStatus(Fp(1), new GnollBenchFeedbackStatusUpdateDto { Status = "resolved" }));
        Assert.Equal(GnollBenchFeedbackStatuses.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedUtc);
        Assert.True(resolved.ResolvedUtc >= before);

        var reopened = OkValue<GnollBenchFeedbackIssueDto>(await _controller.SetStatus(Fp(1), new GnollBenchFeedbackStatusUpdateDto { Status = "new" }));
        Assert.Equal(GnollBenchFeedbackStatuses.New, reopened.Status);
        Assert.Null(reopened.ResolvedUtc);
    }

    [Fact]
    public async Task SetStatus_ResolvingARegressedIssue_MovesResolvedUtcForward()
    {
        await AddSimpleReportAsync(Fp(1), T0.AddDays(2));
        await AddTriageAsync(Fp(1), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(1));
        Assert.Equal(GnollBenchFeedbackStatuses.Regressed, Assert.Single((await ListAsync()).Rows).Status);

        var dto = OkValue<GnollBenchFeedbackIssueDto>(await _controller.SetStatus(Fp(1), new GnollBenchFeedbackStatusUpdateDto { Status = "resolved" }));

        Assert.Equal(GnollBenchFeedbackStatuses.Resolved, dto.Status);
        Assert.True(dto.ResolvedUtc > T0.AddDays(2));
    }

    [Fact]
    public async Task SetStatus_RefusesRegressed_InvalidInput_AndUnknownFingerprints()
    {
        await AddSimpleReportAsync(Fp(1), T0);

        Assert.IsType<BadRequestObjectResult>(await _controller.SetStatus(Fp(1), new GnollBenchFeedbackStatusUpdateDto { Status = "regressed" }));
        Assert.IsType<BadRequestObjectResult>(await _controller.SetStatus(Fp(1), new GnollBenchFeedbackStatusUpdateDto { Status = "fixed" }));
        Assert.IsType<BadRequestObjectResult>(await _controller.SetStatus(Fp(1), null));
        Assert.IsType<BadRequestObjectResult>(await _controller.SetStatus(Fp(1),
            new GnollBenchFeedbackStatusUpdateDto { Status = "acknowledged", Note = new string('n', GnollBenchFeedbackQuery.MaxTriageNoteLength + 1) }));
        Assert.IsType<BadRequestObjectResult>(await _controller.SetStatus(Fp(1),
            new GnollBenchFeedbackStatusUpdateDto { Status = "resolved", Resolution = new string('r', GnollBenchFeedbackQuery.MaxResolutionLength + 1) }));
        Assert.IsType<BadRequestObjectResult>(await _controller.SetStatus(Fp(1),
            new GnollBenchFeedbackStatusUpdateDto { Status = "resolved", FixedInVersion = "1.4 beta" }));
        Assert.False(await _db.BenchmarkFeedbackTriages.AnyAsync(Ct));

        Assert.IsType<NotFoundObjectResult>(await _controller.SetStatus(Fp(0xEE), new GnollBenchFeedbackStatusUpdateDto { Status = "resolved" }));
        Assert.IsType<NotFoundObjectResult>(await _controller.SetStatus("abc", new GnollBenchFeedbackStatusUpdateDto { Status = "resolved" }));
    }

    [Fact]
    public async Task SetStatus_WorksForAnIssueWithOnlyRefusedReports()
    {
        await AddSimpleReportAsync(Fp(1), T0, accepted: false);

        var dto = OkValue<GnollBenchFeedbackIssueDto>(await _controller.SetStatus(Fp(1), new GnollBenchFeedbackStatusUpdateDto { Status = "dismissed" }));

        Assert.Equal(GnollBenchFeedbackStatuses.Dismissed, dto.Status);
        Assert.Equal(1, dto.SelfReportCount);
    }

    // --- Cross-link ---

    [Fact]
    public async Task AlsoInOverseer_NamesTheChatIssueWithTheSameFingerprint()
    {
        await AddSimpleReportAsync(Fp(1), T0);
        await AddSimpleReportAsync(Fp(2), T0);
        var chatIssue = new OverseerFeedbackIssue
        {
            Fingerprint = Fp(1),
            Category = MissingInformation,
            Subject = "Gnoll lord speed missing",
            Summary = "The wiki has no speed for it.",
            FirstSeenUtc = T0,
            LastSeenUtc = T0,
            OccurrenceCount = 1,
            Status = OverseerFeedbackStatus.Acknowledged
        };
        _db.OverseerFeedbackIssues.Add(chatIssue);
        await _db.SaveChangesAsync(Ct);

        var rows = (await ListAsync()).Rows.ToDictionary(r => r.Fingerprint);

        Assert.NotNull(rows[Fp(1)].AlsoInOverseer);
        Assert.Equal(chatIssue.Id, rows[Fp(1)].AlsoInOverseer!.IssueId);
        Assert.Equal(GnollBenchFeedbackStatuses.Acknowledged, rows[Fp(1)].AlsoInOverseer!.Status);
        Assert.Null(rows[Fp(2)].AlsoInOverseer);
    }

    // --- Service members the export and import use ---

    [Fact]
    public async Task Service_LoadsIssuesWithNewestOccurrences_TriageStates_AndCountsSince()
    {
        var run = await AddRunAsync();
        await AddReportAsync(await AddAnswerAsync(run, T0), Fp(1), "First");
        await AddReportAsync(await AddAnswerAsync(run, T0.AddDays(1)), Fp(1), "Second");
        await AddReportAsync(await AddAnswerAsync(run, T0.AddDays(2)), Fp(1), "Third");
        await AddTriageAsync(Fp(1), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(1).AddHours(12));
        var query = new GnollBenchFeedbackQuery(_db);

        var issue = Assert.Single(await query.LoadIssuesAsync(new[] { Fp(1), Fp(0xEE) }, newestOccurrences: 2, ct: Ct));
        Assert.Equal(new[] { "Third", "Second" }, issue.Occurrences.Select(o => o.Subject));
        Assert.Equal(GnollBenchFeedbackStatus.Regressed, issue.Status);

        var states = await query.GetTriageStatesAsync(new[] { Fp(1), Fp(0xEE) }, Ct);
        Assert.True(states[Fp(1)].Exists);
        Assert.Equal(GnollBenchFeedbackStatus.Regressed, states[Fp(1)].Status);
        Assert.Equal(BenchmarkFeedbackTriageStatus.Resolved, states[Fp(1)].StoredStatus);
        Assert.Equal(T0.AddDays(2), states[Fp(1)].LastSeenUtc);
        Assert.Equal(3, states[Fp(1)].AcceptedCount);
        Assert.False(states[Fp(0xEE)].Exists);
        Assert.Equal(GnollBenchFeedbackStatus.New, states[Fp(0xEE)].Status);

        var since = await query.CountAcceptedSinceAsync(new[] { Fp(1) }, T0.AddHours(12), Ct);
        Assert.Equal(2, since[Fp(1)]);
        Assert.Equal(new[] { "Third", "Second" }, (await query.ListAcceptedSinceAsync(Fp(1), T0.AddHours(12), ct: Ct)).Select(o => o.Subject));
    }
}

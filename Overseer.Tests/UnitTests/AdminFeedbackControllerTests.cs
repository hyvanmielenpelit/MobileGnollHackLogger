using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Feedback;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The admin Feedback API on the in-memory provider: the switch, the issue list with its filters (the
/// occurrence-level ones matched together on one occurrence), sorts, date overlap rule and per-issue aggregates, the
/// id list, the facets, occurrences, counts, triage with its keep and clear rules, the GnollBench link, and deletion.
/// </summary>
public sealed class AdminFeedbackControllerTests : IDisposable
{
    private const string AdminId = "feedback-admin";
    private const string AdminName = "triager";
    private const string MissingInformation = "missing_information";
    private const string ToolProblem = "tool_problem";
    private const string OwnMistake = "own_mistake";

    private static readonly DateTime T0 = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ServiceProvider _provider;
    private readonly MemoryCache _cache;
    private readonly ApplicationDbContext _db;
    private readonly AdminFeedbackController _controller;
    private int _fingerprintSeed;

    public AdminFeedbackControllerTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        _provider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "Feedback:Enabled", "true" } })
            .Build();
        _cache = new MemoryCache(new MemoryCacheOptions());
        var feedbackSwitch = new FeedbackSwitchService(_provider.GetRequiredService<IServiceScopeFactory>(), _cache, config);

        _db = new ApplicationDbContext(dbOptions);
        _controller = new AdminFeedbackController(_db, feedbackSwitch, new OverseerFeedbackQuery(_db))
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
        _cache.Dispose();
        _provider.Dispose();
    }

    // --- Seeding ---

    private async Task<OverseerFeedbackIssue> AddIssueAsync(string subject, string category = MissingInformation,
        OverseerFeedbackStatus status = OverseerFeedbackStatus.New, DateTime? firstSeen = null, DateTime? lastSeen = null,
        int occurrenceCount = 1, string summary = "Something was missing.", string? triagedBy = null,
        DateTime? triagedUtc = null, int regressionCount = 0, string? resolution = null)
    {
        var issue = new OverseerFeedbackIssue
        {
            Fingerprint = (++_fingerprintSeed).ToString("x64"),
            Category = category,
            Subject = subject,
            Summary = summary,
            FirstSeenUtc = firstSeen ?? T0,
            LastSeenUtc = lastSeen ?? firstSeen ?? T0,
            OccurrenceCount = occurrenceCount,
            RegressionCount = regressionCount,
            Status = status,
            TriagedByUserId = triagedBy,
            TriagedUtc = triagedUtc ?? (triagedBy == null ? null : (DateTime?)T0),
            Resolution = resolution
        };
        _db.OverseerFeedbackIssues.Add(issue);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return issue;
    }

    private async Task<OverseerFeedbackReport> AddReportAsync(OverseerFeedbackIssue issue, long sessionId, DateTime createdUtc,
        string? userId = "user-1", string? modelId = "model-a", string? expectedLocation = null, string? relatedToolsJson = null,
        string? suspectedCause = null, string? appVersion = null, string? provider = "TestProvider", string? modelSource = null,
        long? configurationId = null, string? details = null, string? searchedFor = null)
    {
        var report = new OverseerFeedbackReport
        {
            IssueId = issue.Id,
            Fingerprint = issue.Fingerprint,
            CreatedUtc = createdUtc,
            Category = issue.Category,
            Subject = issue.Subject,
            Summary = issue.Summary,
            ChatSessionId = sessionId,
            AspNetUserId = userId,
            ModelId = modelId,
            Provider = provider,
            ExpectedLocation = expectedLocation,
            RelatedToolsJson = relatedToolsJson,
            SuspectedCause = suspectedCause,
            AppVersion = appVersion,
            ModelSource = modelSource,
            SystemAiConfigurationId = configurationId,
            Details = details,
            SearchedFor = searchedFor
        };
        _db.OverseerFeedbackReports.Add(report);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return report;
    }

    private async Task AddUserAsync(string id, string userName)
    {
        _db.Users.Add(new ApplicationUser { Id = id, UserName = userName, Email = userName + "@example.com" });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Group> AddGroupAsync(string name, params string[] userIds)
    {
        var group = new Group { DisplayName = name, CreatedAtUtc = T0 };
        _db.Groups.Add(group);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        foreach (var userId in userIds)
            _db.UserGroups.Add(new UserGroup { AspNetUserId = userId, GroupId = group.Id });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return group;
    }

    private async Task<SystemAiApiConfiguration> AddConfigurationAsync(string displayName)
    {
        var configuration = new SystemAiApiConfiguration { DisplayName = displayName, Provider = "OpenAI", ModelId = "test-model" };
        _db.SystemAiApiConfigurations.Add(configuration);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return configuration;
    }

    /// <summary>One run with one answer and one self-report on it.</summary>
    private async Task AddSelfReportAsync(string fingerprint, bool accepted = true, DateTime? answerStartedUtc = null, DateTime? runStartedUtc = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun { SuiteName = "Suite", StartedAtUtc = runStartedUtc ?? T0 });
        var answer = new BenchmarkRunAnswer { QuestionText = "Q1", AnswerText = "A1", OrderIndex = 1, StartedAtUtc = answerStartedUtc };
        run.Answers.Add(answer);
        _db.BenchmarkRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        _db.BenchmarkRunAnswerSelfReports.Add(new BenchmarkRunAnswerSelfReport
        {
            BenchmarkRunAnswerId = answer.Id,
            Accepted = accepted,
            Error = accepted ? null : "Feedback limit for this turn reached (3).",
            Fingerprint = fingerprint,
            Category = MissingInformation,
            Subject = "Subject",
            Summary = "Summary."
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task AddTriageAsync(string fingerprint, BenchmarkFeedbackTriageStatus status, DateTime? resolvedUtc = null)
    {
        _db.BenchmarkFeedbackTriages.Add(new BenchmarkFeedbackTriage { Fingerprint = fingerprint, Status = status, ResolvedUtc = resolvedUtc });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static T OkValue<T>(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value);
    }

    private async Task<AdminFeedbackIssuePageDto> ListAsync(AdminFeedbackFilterQuery? filter = null, string? sort = null,
        int skip = 0, int take = AdminFeedbackController.DefaultIssueTake)
        => OkValue<AdminFeedbackIssuePageDto>(await _controller.ListIssues(filter, sort, skip, take, TestContext.Current.CancellationToken));

    private async Task<string[]> SubjectsAsync(AdminFeedbackFilterQuery filter)
        => (await ListAsync(filter)).Rows.Select(r => r.Subject).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    private async Task<AdminFeedbackOccurrencePageDto> OccurrencesAsync(long issueId, AdminFeedbackFilterQuery? filter = null,
        bool all = false, int skip = 0, int take = AdminFeedbackController.DefaultOccurrenceTake)
        => OkValue<AdminFeedbackOccurrencePageDto>(await _controller.ListOccurrences(issueId, filter, all, skip, take, TestContext.Current.CancellationToken));

    private async Task<OverseerFeedbackIssue> StoredAsync(long id)
        => await _db.OverseerFeedbackIssues.AsNoTracking().SingleAsync(i => i.Id == id, TestContext.Current.CancellationToken);

    private static void AssertBadRequest(IActionResult result, string? messagePart = null)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        if (messagePart != null) Assert.Contains(messagePart, System.Text.Json.JsonSerializer.Serialize(bad.Value));
    }

    // --- Issues: paging and the date range ---

    [Fact]
    public async Task IssuesArePagedWithTheTotalAndTakeIsClamped()
    {
        for (int i = 0; i < 30; i++)
            await AddIssueAsync($"Subject {i}", lastSeen: T0.AddHours(i));

        var first = await ListAsync();
        Assert.Equal(30, first.TotalCount);
        Assert.Equal(25, first.Rows.Count);
        Assert.Equal("Subject 29", first.Rows[0].Subject);

        var second = await ListAsync(skip: 25);
        Assert.Equal(30, second.TotalCount);
        Assert.Equal(5, second.Rows.Count);
        Assert.Equal("Subject 4", second.Rows[0].Subject);

        Assert.Single((await ListAsync(take: 0)).Rows);
        Assert.Equal(30, (await ListAsync(take: 500)).Rows.Count);
        Assert.Equal(25, (await ListAsync(skip: -5)).Rows.Count);
    }

    [Fact]
    public async Task AnIssueIsInTheRangeWhenItsActiveSpanOverlapsIt()
    {
        var start = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

        await AddIssueAsync("Spans the range", firstSeen: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), lastSeen: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddIssueAsync("Wholly before", firstSeen: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), lastSeen: new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc));
        await AddIssueAsync("Wholly after", firstSeen: new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc), lastSeen: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddIssueAsync("Ends inside", firstSeen: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), lastSeen: new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc));
        await AddIssueAsync("Starts on the last day", firstSeen: new DateTime(2026, 8, 20, 23, 0, 0, DateTimeKind.Utc), lastSeen: new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc));

        var page = await ListAsync(new AdminFeedbackFilterQuery { StartDate = start, EndDate = end });

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(
            new[] { "Ends inside", "Spans the range", "Starts on the last day" },
            page.Rows.Select(r => r.Subject).OrderBy(s => s, StringComparer.Ordinal));

        Assert.Equal(5, (await ListAsync()).TotalCount);
        Assert.Equal(4, (await ListAsync(new AdminFeedbackFilterQuery { StartDate = start })).TotalCount);
        Assert.Equal(4, (await ListAsync(new AdminFeedbackFilterQuery { EndDate = end })).TotalCount);
    }

    // --- Issues: filters ---

    [Fact]
    public async Task TheStatusListCategoryAndModelFiltersCombine()
    {
        var newIssue = await AddIssueAsync("New one", MissingInformation, OverseerFeedbackStatus.New);
        var regressed = await AddIssueAsync("Regressed one", ToolProblem, OverseerFeedbackStatus.Regressed);
        await AddIssueAsync("Resolved one", MissingInformation, OverseerFeedbackStatus.Resolved);
        await AddIssueAsync("Dismissed one", OwnMistake, OverseerFeedbackStatus.Dismissed);
        await AddReportAsync(newIssue, 1, T0, modelId: "model-a");
        await AddReportAsync(regressed, 2, T0, modelId: "model-b");

        var open = await ListAsync(new AdminFeedbackFilterQuery { Status = "new, regressed" });
        Assert.Equal(new[] { "New one", "Regressed one" }, open.Rows.Select(r => r.Subject).OrderBy(s => s, StringComparer.Ordinal));
        Assert.Contains(open.Rows, r => r.Status == "regressed");

        Assert.Equal(new[] { "New one", "Resolved one" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Category = MissingInformation }));
        Assert.Equal("Regressed one", Assert.Single((await ListAsync(new AdminFeedbackFilterQuery { ModelId = "model-b" })).Rows).Subject);

        Assert.Empty((await ListAsync(new AdminFeedbackFilterQuery { Status = "new", ModelId = "model-b" })).Rows);
        Assert.Equal("Dismissed one", Assert.Single((await ListAsync(new AdminFeedbackFilterQuery { Status = "dismissed", Category = OwnMistake })).Rows).Subject);
    }

    [Fact]
    public async Task CategoryAndModelAcceptCommaLists()
    {
        var a = await AddIssueAsync("A", MissingInformation);
        var b = await AddIssueAsync("B", ToolProblem);
        var c = await AddIssueAsync("C", OwnMistake);
        await AddReportAsync(a, 1, T0, modelId: "model-a");
        await AddReportAsync(b, 2, T0, modelId: "model-b");
        await AddReportAsync(c, 3, T0, modelId: "model-c");

        Assert.Equal(new[] { "A", "B" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Category = $"{MissingInformation},{ToolProblem}" }));
        Assert.Equal(new[] { "A", "C" }, await SubjectsAsync(new AdminFeedbackFilterQuery { ModelId = "model-a, model-c" }));
        Assert.Equal(new[] { "A" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Category = $"{MissingInformation},{ToolProblem}", ModelId = "model-a,model-c" }));
    }

    [Fact]
    public async Task OccurrenceFiltersMatchAnIssueOnlyWhenOneOccurrenceMatchesThemAll()
    {
        // A has model-a on one occurrence and provider P2 on another; B has both on one.
        var a = await AddIssueAsync("A");
        var b = await AddIssueAsync("B");
        await AddReportAsync(a, 1, T0, modelId: "model-a", provider: "P1", appVersion: "1.0.0");
        await AddReportAsync(a, 2, T0, modelId: "model-b", provider: "P2", appVersion: "1.1.0");
        await AddReportAsync(b, 3, T0, modelId: "model-a", provider: "P2", appVersion: "1.0.0");

        Assert.Equal(new[] { "A", "B" }, await SubjectsAsync(new AdminFeedbackFilterQuery { ModelId = "model-a" }));
        Assert.Equal(new[] { "A", "B" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Provider = "P2" }));
        Assert.Equal(new[] { "B" }, await SubjectsAsync(new AdminFeedbackFilterQuery { ModelId = "model-a", Provider = "P2" }));
        Assert.Equal(new[] { "A" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Provider = "P2", Version = "1.1.0" }));
        Assert.Empty(await SubjectsAsync(new AdminFeedbackFilterQuery { ModelId = "model-b", Version = "1.0.0" }));
    }

    [Fact]
    public async Task TheUserVersionToolSourceAndConfigurationFiltersMatchTheirOccurrences()
    {
        var system = await AddIssueAsync("System");
        var own = await AddIssueAsync("Own model");
        var unknown = await AddIssueAsync("Unknown");
        await AddReportAsync(system, 1, T0, userId: "user-1", modelSource: "system", configurationId: 7, appVersion: "1.4.0",
            relatedToolsJson: "[\"wiki_search\",\"WIKI_VIEW\"]");
        await AddReportAsync(own, 2, T0, userId: "user-2", modelSource: "user", appVersion: "1.4.1", relatedToolsJson: "[\"source_code_search\"]");
        await AddReportAsync(unknown, 3, T0, userId: "user-2", modelSource: null, appVersion: "1.4.1");

        Assert.Equal(new[] { "System" }, await SubjectsAsync(new AdminFeedbackFilterQuery { User = "user-1" }));
        Assert.Equal(new[] { "Own model", "Unknown" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Version = "1.4.1" }));

        Assert.Equal(new[] { "System" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Tool = "wiki_view" }));
        Assert.Equal(new[] { "Own model", "System" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Tool = "wiki_search,source_code_search" }));
        Assert.Empty(await SubjectsAsync(new AdminFeedbackFilterQuery { Tool = "wiki" }));

        Assert.Equal(new[] { "System" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Source = "system" }));
        Assert.Equal(new[] { "Own model" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Source = "USER" }));
        Assert.Equal(new[] { "Unknown" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Source = "unknown" }));
        Assert.Equal(new[] { "System", "Unknown" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Source = "system,unknown" }));

        Assert.Equal(new[] { "System" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Config = "7" }));
        Assert.Empty(await SubjectsAsync(new AdminFeedbackFilterQuery { Config = "8" }));
    }

    [Fact]
    public async Task TheGroupFilterMatchesMembersAndNoneMatchesUsersInNoGroup()
    {
        await AddUserAsync("user-1", "alice");
        await AddUserAsync("user-2", "bob");
        await AddUserAsync("user-3", "carol");
        var testers = await AddGroupAsync("Testers", "user-1");
        var admins = await AddGroupAsync("Admins", "user-2");

        var a = await AddIssueAsync("A");
        var b = await AddIssueAsync("B");
        var c = await AddIssueAsync("C");
        await AddReportAsync(a, 1, T0, userId: "user-1");
        await AddReportAsync(b, 2, T0, userId: "user-2");
        await AddReportAsync(c, 3, T0, userId: "user-3");

        Assert.Equal(new[] { "A" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Group = testers.Id.ToString() }));
        Assert.Equal(new[] { "A", "B" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Group = $"{testers.Id},{admins.Id}" }));
        Assert.Equal(new[] { "C" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Group = "none" }));
        Assert.Equal(new[] { "B", "C" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Group = $"{admins.Id},NONE" }));
        Assert.Equal(new[] { "B" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Group = $"{admins.Id},none", User = "user-2" }));
    }

    [Fact]
    public async Task TheResolutionAndIdsFiltersAreIssueLevel()
    {
        var a = await AddIssueAsync("A", resolution: "Wiki page added.");
        var b = await AddIssueAsync("B", resolution: "");
        var c = await AddIssueAsync("C");

        Assert.Equal(new[] { "A" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Resolution = "with" }));
        Assert.Equal(new[] { "B", "C" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Resolution = "Without" }));
        Assert.Equal(new[] { "A", "C" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Ids = $"{a.Id}, {c.Id}, 99999" }));
        Assert.Equal(new[] { "C" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Ids = $"{a.Id},{c.Id}", Resolution = "without" }));
        Assert.Equal(new[] { "B" }, await SubjectsAsync(new AdminFeedbackFilterQuery { Ids = b.Id.ToString(), Resolution = "without", Status = "new" }));
    }

    [Fact]
    public async Task SearchMatchesSubjectSummaryAndOccurrenceTextIgnoringCase()
    {
        await AddIssueAsync("Dilithium crystal", summary: "Price not found.");
        await AddIssueAsync("Elbereth", summary: "The engraving rules for DILITHIUM were missing.");
        await AddIssueAsync("Cockatrice corpse", summary: "Weight missing.");
        var details = await AddIssueAsync("In details");
        var searched = await AddIssueAsync("In searched for");
        var location = await AddIssueAsync("In expected location");
        await AddReportAsync(details, 1, T0, details: "The user asked about Dilithium prices.");
        await AddReportAsync(searched, 2, T0, searchedFor: "wiki_search 'dilithium'");
        await AddReportAsync(location, 3, T0, expectedLocation: "wiki page DiLiThIuM crystal");
        await AddReportAsync(location, 4, T0, details: null, searchedFor: null, expectedLocation: null);

        var page = await ListAsync(new AdminFeedbackFilterQuery { Search = "  dilithium " });

        Assert.Equal(new[] { "Dilithium crystal", "Elbereth", "In details", "In expected location", "In searched for" },
            page.Rows.Select(r => r.Subject).OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public async Task InvalidIssueQueriesAreRefused()
    {
        var ct = TestContext.Current.CancellationToken;

        AssertBadRequest(await _controller.ListIssues(new AdminFeedbackFilterQuery { Status = "new,bogus" }, ct: ct), "bogus");
        AssertBadRequest(await _controller.ListIssues(new AdminFeedbackFilterQuery { Category = $"{MissingInformation},bogus" }, ct: ct), "bogus");
        AssertBadRequest(await _controller.ListIssues(sort: "bogus", ct: ct), "bogus");
        AssertBadRequest(await _controller.ListIssues(new AdminFeedbackFilterQuery { Search = new string('x', AdminFeedbackController.MaxSearchLength + 1) }, ct: ct));
        AssertBadRequest(await _controller.ListIssues(new AdminFeedbackFilterQuery { Source = "system,robot" }, ct: ct), "robot");
        AssertBadRequest(await _controller.ListIssues(new AdminFeedbackFilterQuery { Group = "testers" }, ct: ct), "testers");
        AssertBadRequest(await _controller.ListIssues(new AdminFeedbackFilterQuery { Config = "7,x" }, ct: ct));
        AssertBadRequest(await _controller.ListIssues(new AdminFeedbackFilterQuery { Resolution = "maybe" }, ct: ct), "maybe");
        AssertBadRequest(await _controller.ListIssues(new AdminFeedbackFilterQuery { Ids = "1,-2" }, ct: ct));
        AssertBadRequest(await _controller.ListIssues(new AdminFeedbackFilterQuery
        {
            Ids = string.Join(",", Enumerable.Range(1, OverseerFeedbackFilter.MaxIssueIds + 1))
        }, ct: ct));
        AssertBadRequest(await _controller.ListIssueIds(new AdminFeedbackFilterQuery { Status = "bogus" }, ct: ct));
        AssertBadRequest(await _controller.ListIssueIds(sort: "bogus", ct: ct));

        // The largest id list is accepted.
        OkValue<AdminFeedbackIssuePageDto>(await _controller.ListIssues(new AdminFeedbackFilterQuery
        {
            Ids = string.Join(",", Enumerable.Range(1, OverseerFeedbackFilter.MaxIssueIds))
        }, ct: ct));
    }

    // --- Issues: sorts ---

    [Fact]
    public async Task TheThreeOriginalSortsOrderDescending()
    {
        await AddIssueAsync("A", firstSeen: T0, lastSeen: T0.AddDays(5), occurrenceCount: 2);
        await AddIssueAsync("B", firstSeen: T0.AddDays(2), lastSeen: T0.AddDays(3), occurrenceCount: 9);
        await AddIssueAsync("C", firstSeen: T0.AddDays(1), lastSeen: T0.AddDays(4), occurrenceCount: 5);

        Assert.Equal(new[] { "A", "C", "B" }, (await ListAsync()).Rows.Select(r => r.Subject));
        Assert.Equal(new[] { "A", "C", "B" }, (await ListAsync(sort: "lastSeen")).Rows.Select(r => r.Subject));
        Assert.Equal(new[] { "B", "C", "A" }, (await ListAsync(sort: "firstSeen")).Rows.Select(r => r.Subject));
        Assert.Equal(new[] { "B", "C", "A" }, (await ListAsync(sort: "COUNT")).Rows.Select(r => r.Subject));
    }

    [Fact]
    public async Task TheUsersRegressionsTriagedAndSubjectSorts()
    {
        var bravo = await AddIssueAsync("Bravo", lastSeen: T0.AddDays(3), regressionCount: 0, triagedUtc: T0.AddDays(1));
        var alpha = await AddIssueAsync("Alpha", lastSeen: T0.AddDays(2), regressionCount: 4);
        var charlie = await AddIssueAsync("Charlie", lastSeen: T0.AddDays(1), regressionCount: 1, triagedUtc: T0.AddDays(2));

        // Distinct retained users: Alpha 3, Charlie 2, Bravo 1 (repeats and missing users do not count).
        await AddReportAsync(alpha, 1, T0, userId: "user-1");
        await AddReportAsync(alpha, 2, T0, userId: "user-2");
        await AddReportAsync(alpha, 3, T0, userId: "user-3");
        await AddReportAsync(charlie, 4, T0, userId: "user-1");
        await AddReportAsync(charlie, 5, T0, userId: "user-2");
        await AddReportAsync(charlie, 6, T0, userId: "user-2");
        await AddReportAsync(bravo, 7, T0, userId: "user-1");
        await AddReportAsync(bravo, 8, T0, userId: null);
        await AddReportAsync(bravo, 9, T0, userId: null);

        Assert.Equal(new[] { "Alpha", "Charlie", "Bravo" }, (await ListAsync(sort: "users")).Rows.Select(r => r.Subject));
        Assert.Equal(new[] { "Alpha", "Charlie", "Bravo" }, (await ListAsync(sort: "regressions")).Rows.Select(r => r.Subject));
        Assert.Equal(new[] { "Charlie", "Bravo", "Alpha" }, (await ListAsync(sort: "triaged")).Rows.Select(r => r.Subject));
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, (await ListAsync(sort: "subject")).Rows.Select(r => r.Subject));
    }

    // --- Issue ids ---

    [Fact]
    public async Task TheIdListFollowsTheFiltersAndTheSort()
    {
        var ct = TestContext.Current.CancellationToken;
        var older = await AddIssueAsync("Older", lastSeen: T0);
        var newer = await AddIssueAsync("Newer", lastSeen: T0.AddDays(1));
        await AddIssueAsync("Dismissed", status: OverseerFeedbackStatus.Dismissed, lastSeen: T0.AddDays(2));

        var ids = OkValue<AdminFeedbackIssueIdsDto>(await _controller.ListIssueIds(new AdminFeedbackFilterQuery { Status = "new" }, ct: ct));
        Assert.Equal(new[] { newer.Id, older.Id }, ids.Ids);
        Assert.Equal(2, ids.TotalCount);
        Assert.False(ids.Truncated);

        var bySubject = OkValue<AdminFeedbackIssueIdsDto>(await _controller.ListIssueIds(new AdminFeedbackFilterQuery { Status = "new" }, "subject", ct));
        Assert.Equal(new[] { newer.Id, older.Id }, bySubject.Ids);
    }

    [Fact]
    public async Task TheIdListIsCappedAndSaysSo()
    {
        var ct = TestContext.Current.CancellationToken;
        for (int i = 0; i < AdminFeedbackController.MaxIssueIdsReturned + 1; i++)
        {
            _db.OverseerFeedbackIssues.Add(new OverseerFeedbackIssue
            {
                Fingerprint = (++_fingerprintSeed).ToString("x64"), Category = MissingInformation, Subject = $"S{i}", Summary = "S.",
                FirstSeenUtc = T0, LastSeenUtc = T0.AddMinutes(i), OccurrenceCount = 1
            });
        }
        await _db.SaveChangesAsync(ct);

        var ids = OkValue<AdminFeedbackIssueIdsDto>(await _controller.ListIssueIds(ct: ct));

        Assert.Equal(AdminFeedbackController.MaxIssueIdsReturned, ids.Ids.Count);
        Assert.Equal(AdminFeedbackController.MaxIssueIdsReturned + 1, ids.TotalCount);
        Assert.True(ids.Truncated);
    }

    // --- Facets ---

    [Fact]
    public async Task TheFacetsListTheOptionsOfTheIssuesInTheRangeWithIssueCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddUserAsync("user-1", "alice");
        await AddUserAsync("user-2", "bob");
        await AddUserAsync("user-3", "carol");
        await AddUserAsync("user-9", "zed");
        var testers = await AddGroupAsync("Testers", "user-1", "user-2");
        var admins = await AddGroupAsync("Admins", "user-2");
        var fast = await AddConfigurationAsync("Fast chat");

        var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc);
        var in1 = await AddIssueAsync("In 1", MissingInformation, firstSeen: T0);
        var in2 = await AddIssueAsync("In 2", ToolProblem, firstSeen: T0);
        var outside = await AddIssueAsync("Outside", OwnMistake, firstSeen: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            lastSeen: new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc));

        await AddReportAsync(in1, 1, T0, userId: "user-1", modelId: "model-a", provider: "ProvA", modelSource: "system",
            configurationId: fast.Id, appVersion: "1.2.0", relatedToolsJson: "[\"wiki_search\"]");
        await AddReportAsync(in1, 2, T0, userId: "user-2", modelId: "model-b", provider: "ProvB", modelSource: "user",
            appVersion: "1.10.0", relatedToolsJson: "[\"WIKI_VIEW\",\"wiki_search\"]");
        await AddReportAsync(in2, 3, T0, userId: "user-1", modelId: "model-a", provider: "ProvA", modelSource: null,
            configurationId: 999, appVersion: "1.2.0");
        await AddReportAsync(in2, 4, T0, userId: "user-3", modelId: "model-a", provider: "ProvA", appVersion: "1.2.0");
        await AddReportAsync(outside, 5, T0, userId: "user-9", modelId: "model-z", provider: "ProvZ", appVersion: "0.9.0");

        var facets = OkValue<AdminFeedbackFacetsDto>(await _controller.GetFacets(start, end, ct));

        static string[] Values(List<AdminFeedbackFacetValueDto> options) => options.Select(o => $"{o.Value}:{o.Count}").ToArray();
        static string[] Entities(List<AdminFeedbackFacetEntityDto> options) => options.Select(o => $"{o.Id}|{o.Name}:{o.Count}").ToArray();

        Assert.Equal(new[] { "missing_information:1", "tool_problem:1" }, Values(facets.Categories));
        Assert.Equal(new[] { "model-a:2", "model-b:1" }, Values(facets.Models));
        Assert.Equal(new[] { "ProvA:2", "ProvB:1" }, Values(facets.Providers));
        Assert.Equal(new[] { "user-1|alice:2", "user-2|bob:1", "user-3|carol:1" }, Entities(facets.Users));
        Assert.Equal(new[] { $"{admins.Id}|Admins:1", $"{testers.Id}|Testers:2", "none|No group:1" }, Entities(facets.Groups));
        Assert.Equal(new[] { "system:1", "user:1", "unknown:1" }, Values(facets.Sources));
        Assert.Equal(new[] { "999|Configuration #999 (deleted):1", $"{fast.Id}|Fast chat:1" }, Entities(facets.Configs));
        Assert.Equal(new[] { "1.10.0:1", "1.2.0:2" }, Values(facets.Versions));
        Assert.Equal(new[] { "wiki_search:1", "wiki_view:1" }, Values(facets.Tools));

        var allTime = OkValue<AdminFeedbackFacetsDto>(await _controller.GetFacets(ct: ct));
        Assert.Equal(new[] { "missing_information:1", "tool_problem:1", "own_mistake:1" }, Values(allTime.Categories));
        Assert.Contains("model-z:1", Values(allTime.Models));
        Assert.Contains("none|No group:2", Entities(allTime.Groups));
    }

    // --- Switch ---

    [Fact]
    public async Task TheSwitchStartsSeededAndASetIsAppendedAndAttributed()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddUserAsync(AdminId, AdminName);

        var seeded = OkValue<AdminFeedbackSwitchesDto>(await _controller.GetSwitch(ct));
        Assert.True(seeded.Chat.Enabled);
        Assert.True(seeded.Chat.Seeded);
        Assert.Null(seeded.Chat.SetAtUtc);
        Assert.Null(seeded.Chat.SetByUserName);

        var set = OkValue<AdminFeedbackSwitchesDto>(await _controller.SetSwitch("chat", new AdminFeedbackSwitchUpdateDto { Enabled = false }));
        Assert.False(set.Chat.Enabled);
        Assert.False(set.Chat.Seeded);
        Assert.NotNull(set.Chat.SetAtUtc);
        Assert.Equal(AdminName, set.Chat.SetByUserName);

        var again = OkValue<AdminFeedbackSwitchesDto>(await _controller.GetSwitch(ct));
        Assert.False(again.Chat.Enabled);
        Assert.False(again.Chat.Seeded);
        Assert.Equal(set.Chat.SetAtUtc, again.Chat.SetAtUtc);
        Assert.Equal(AdminName, again.Chat.SetByUserName);

        await _controller.SetSwitch("Chat", new AdminFeedbackSwitchUpdateDto { Enabled = false });
        Assert.Equal(2, await _db.OverseerFeedbackSettings.CountAsync(ct));
        Assert.All(await _db.OverseerFeedbackSettings.ToListAsync(ct), s =>
        {
            Assert.Equal(AdminId, s.SetByUserId);
            Assert.Equal(OverseerFeedbackSwitchTarget.Chat, s.Target);
        });

        AssertBadRequest(await _controller.SetSwitch("chat", null));
    }

    [Theory]
    [InlineData("gnollbench")]
    [InlineData("other")]
    [InlineData("")]
    [InlineData("1")]
    public async Task AnUnknownSwitch_IsRejected(string target)
    {
        var result = await _controller.SetSwitch(target, new AdminFeedbackSwitchUpdateDto { Enabled = false });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Unknown switch.", System.Text.Json.JsonSerializer.Serialize(bad.Value));
        Assert.Equal(0, await _db.OverseerFeedbackSettings.CountAsync(TestContext.Current.CancellationToken));
    }

    // --- Aggregates ---

    [Fact]
    public async Task EachIssueCarriesTheAggregatesOfItsRetainedOccurrences()
    {
        await AddUserAsync(AdminId, AdminName);
        var issue = await AddIssueAsync("Dilithium crystal", occurrenceCount: 12, triagedBy: AdminId);
        var gone = await AddIssueAsync("Gone triager", triagedBy: "deleted-admin");
        var empty = await AddIssueAsync("No occurrences left", occurrenceCount: 4);

        string[] models = { "model-g", "model-a", "model-c", "model-b", "model-e", "model-d", "model-f" };
        for (int i = 0; i < models.Length; i++)
        {
            await AddReportAsync(issue, sessionId: 100 + i, createdUtc: T0.AddHours(i), userId: $"user-{i % 3}", modelId: models[i],
                expectedLocation: $"Location {i}", relatedToolsJson: $"[\"tool_{i}\",\"wiki_search\"]",
                suspectedCause: i == models.Length - 1 ? "did_not_verify" : "other", appVersion: $"1.0.{i}");
        }
        await AddReportAsync(gone, sessionId: 200, createdUtc: T0, userId: null, modelId: null, relatedToolsJson: "not json");

        var rows = (await ListAsync()).Rows;
        var row = rows.Single(r => r.Id == issue.Id);

        Assert.Equal(12, row.OccurrenceCount);
        Assert.Equal(7, row.RetainedCount);
        Assert.Equal(3, row.DistinctUserCount);
        Assert.Equal(new[] { "model-a", "model-b", "model-c", "model-d", "model-e", "+2" }, row.ModelIds);
        Assert.Equal("Location 6", row.LatestExpectedLocation);
        Assert.Equal(new[] { "tool_6", "wiki_search" }, row.LatestRelatedTools);
        Assert.Equal("did_not_verify", row.LatestSuspectedCause);
        Assert.Equal("1.0.6", row.LatestAppVersion);
        Assert.Equal("new", row.Status);
        Assert.Equal(AdminName, row.TriagedByUserName);

        var goneRow = rows.Single(r => r.Id == gone.Id);
        Assert.Equal(1, goneRow.RetainedCount);
        Assert.Equal(0, goneRow.DistinctUserCount);
        Assert.Empty(goneRow.ModelIds);
        Assert.Empty(goneRow.LatestRelatedTools);
        Assert.Null(goneRow.TriagedByUserName);
        Assert.NotNull(goneRow.TriagedUtc);

        var emptyRow = rows.Single(r => r.Id == empty.Id);
        Assert.Equal(4, emptyRow.OccurrenceCount);
        Assert.Equal(0, emptyRow.RetainedCount);
        Assert.Null(emptyRow.LatestExpectedLocation);
        Assert.Empty(emptyRow.Providers);
        Assert.Equal(0, emptyRow.ModelSources.System + emptyRow.ModelSources.User + emptyRow.ModelSources.Unknown);
        Assert.Null(emptyRow.AlsoInGnollBench);
    }

    [Fact]
    public async Task EachIssueCarriesItsProvidersModelSourcesAndResolution()
    {
        var issue = await AddIssueAsync("Dilithium crystal", resolution: "Wiki page added.");
        issue.FixedInVersion = "1.4.1";
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await AddReportAsync(issue, 1, T0, provider: "OpenAI", modelSource: "system", configurationId: 3);
        await AddReportAsync(issue, 2, T0, provider: "Anthropic", modelSource: "system", configurationId: 3);
        await AddReportAsync(issue, 3, T0, provider: "OpenAI", modelSource: "user");
        await AddReportAsync(issue, 4, T0, provider: null, modelSource: null);

        var row = Assert.Single((await ListAsync()).Rows);

        Assert.Equal(new[] { "Anthropic", "OpenAI" }, row.Providers);
        Assert.Equal(2, row.ModelSources.System);
        Assert.Equal(1, row.ModelSources.User);
        Assert.Equal(1, row.ModelSources.Unknown);
        Assert.Equal("Wiki page added.", row.Resolution);
        Assert.Equal("1.4.1", row.FixedInVersion);
    }

    // --- The GnollBench link ---

    [Fact]
    public async Task AnAcceptedSelfReportWithTheSameFingerprintLinksTheIssueToGnollBench()
    {
        var none = await AddIssueAsync("No self-report");
        var fresh = await AddIssueAsync("Untriaged in GnollBench");
        var refused = await AddIssueAsync("Only refused");
        var acknowledged = await AddIssueAsync("Acknowledged in GnollBench");
        var regressed = await AddIssueAsync("Reported after the fix");
        var resolved = await AddIssueAsync("Reported before the fix");
        var runTimed = await AddIssueAsync("Timed by its run");

        await AddSelfReportAsync(fresh.Fingerprint);
        await AddSelfReportAsync(fresh.Fingerprint);
        await AddSelfReportAsync(fresh.Fingerprint, accepted: false);
        await AddSelfReportAsync(refused.Fingerprint, accepted: false);
        await AddSelfReportAsync(acknowledged.Fingerprint);
        await AddTriageAsync(acknowledged.Fingerprint, BenchmarkFeedbackTriageStatus.Acknowledged);
        await AddSelfReportAsync(regressed.Fingerprint, answerStartedUtc: T0.AddDays(2), runStartedUtc: T0);
        await AddTriageAsync(regressed.Fingerprint, BenchmarkFeedbackTriageStatus.Resolved, resolvedUtc: T0.AddDays(1));
        await AddSelfReportAsync(resolved.Fingerprint, answerStartedUtc: T0, runStartedUtc: T0.AddDays(5));
        await AddTriageAsync(resolved.Fingerprint, BenchmarkFeedbackTriageStatus.Resolved, resolvedUtc: T0.AddDays(1));
        await AddSelfReportAsync(runTimed.Fingerprint, answerStartedUtc: null, runStartedUtc: T0.AddDays(3));
        await AddTriageAsync(runTimed.Fingerprint, BenchmarkFeedbackTriageStatus.Resolved, resolvedUtc: T0.AddDays(1));

        var rows = (await ListAsync()).Rows.ToDictionary(r => r.Id);

        Assert.Null(rows[none.Id].AlsoInGnollBench);
        Assert.Null(rows[refused.Id].AlsoInGnollBench);

        Assert.Equal(2, rows[fresh.Id].AlsoInGnollBench!.SelfReportCount);
        Assert.Equal("new", rows[fresh.Id].AlsoInGnollBench!.Status);
        Assert.Equal("acknowledged", rows[acknowledged.Id].AlsoInGnollBench!.Status);
        Assert.Equal("regressed", rows[regressed.Id].AlsoInGnollBench!.Status);
        Assert.Equal("resolved", rows[resolved.Id].AlsoInGnollBench!.Status);
        Assert.Equal("regressed", rows[runTimed.Id].AlsoInGnollBench!.Status);
    }

    // --- Occurrences ---

    [Fact]
    public async Task OccurrencesArePagedNewestFirstWithTheUserName()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddUserAsync("user-1", "reporter");
        var issue = await AddIssueAsync("Dilithium crystal", occurrenceCount: 3);
        var other = await AddIssueAsync("Other");
        await AddReportAsync(issue, 1, T0, userId: "user-1", relatedToolsJson: "[\"wiki_search\"]");
        await AddReportAsync(issue, 2, T0.AddHours(2), userId: "erased-user");
        await AddReportAsync(issue, 3, T0.AddHours(1), userId: "user-1");
        await AddReportAsync(other, 1, T0.AddHours(5));

        var first = await OccurrencesAsync(issue.Id, skip: 0, take: 2);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(new long[] { 2, 3 }, first.Rows.Select(r => r.ChatSessionId));
        Assert.Null(first.Rows[0].UserName);
        Assert.Equal("reporter", first.Rows[1].UserName);
        Assert.Equal("TestProvider", first.Rows[1].Provider);

        var second = await OccurrencesAsync(issue.Id, skip: 2, take: 2);
        var last = Assert.Single(second.Rows);
        Assert.Equal(1, last.ChatSessionId);
        Assert.Equal(new[] { "wiki_search" }, last.RelatedTools);

        Assert.IsType<NotFoundObjectResult>(await _controller.ListOccurrences(9999, ct: ct));
    }

    [Fact]
    public async Task OccurrencesTakeTheOccurrenceFiltersUnlessAllIsSet_AndCarryTheirSourceConfigurationAndGroups()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddUserAsync("user-1", "alice");
        await AddUserAsync("user-2", "bob");
        await AddGroupAsync("Testers", "user-1");
        await AddGroupAsync("Admins", "user-1");
        var fast = await AddConfigurationAsync("Fast chat");
        var issue = await AddIssueAsync("Dilithium crystal");
        await AddReportAsync(issue, 1, T0, userId: "user-1", modelId: "model-a", modelSource: "system", configurationId: fast.Id);
        await AddReportAsync(issue, 2, T0.AddHours(1), userId: "user-2", modelId: "model-b", modelSource: "user");
        await AddReportAsync(issue, 3, T0.AddHours(2), userId: "user-2", modelId: "model-a", modelSource: "system", configurationId: 999);

        var byModel = await OccurrencesAsync(issue.Id, new AdminFeedbackFilterQuery { ModelId = "model-a" });
        Assert.Equal(2, byModel.TotalCount);
        Assert.Equal(new long[] { 3, 1 }, byModel.Rows.Select(r => r.ChatSessionId));

        var together = await OccurrencesAsync(issue.Id, new AdminFeedbackFilterQuery { ModelId = "model-a", User = "user-2" });
        Assert.Equal(3, Assert.Single(together.Rows).ChatSessionId);

        // Issue-level filters do not narrow occurrences.
        Assert.Equal(3, (await OccurrencesAsync(issue.Id, new AdminFeedbackFilterQuery { Status = "dismissed" })).TotalCount);

        var everything = await OccurrencesAsync(issue.Id, new AdminFeedbackFilterQuery { ModelId = "model-a", Source = "bogus" }, all: true);
        Assert.Equal(3, everything.TotalCount);

        AssertBadRequest(await _controller.ListOccurrences(issue.Id, new AdminFeedbackFilterQuery { Source = "bogus" }, ct: ct));

        var rows = everything.Rows.ToDictionary(r => r.ChatSessionId);
        Assert.Equal("system", rows[1].ModelSource);
        Assert.Equal(fast.Id, rows[1].SystemConfigurationId);
        Assert.Equal("Fast chat", rows[1].SystemConfigurationName);
        Assert.Equal(new[] { "Admins", "Testers" }, rows[1].GroupNames);

        Assert.Equal("user", rows[2].ModelSource);
        Assert.Null(rows[2].SystemConfigurationId);
        Assert.Null(rows[2].SystemConfigurationName);
        Assert.Empty(rows[2].GroupNames);

        Assert.Equal(999L, rows[3].SystemConfigurationId);
        Assert.Equal("Configuration #999 (deleted)", rows[3].SystemConfigurationName);
    }

    // --- Counts ---

    [Fact]
    public async Task CountsCoverEveryDate()
    {
        await AddIssueAsync("Old new", status: OverseerFeedbackStatus.New, firstSeen: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddIssueAsync("Recent new", status: OverseerFeedbackStatus.New, firstSeen: T0);
        await AddIssueAsync("Regressed", status: OverseerFeedbackStatus.Regressed, firstSeen: T0);
        await AddIssueAsync("Acknowledged", status: OverseerFeedbackStatus.Acknowledged, firstSeen: T0);
        await AddIssueAsync("Resolved", status: OverseerFeedbackStatus.Resolved, firstSeen: T0);
        await AddIssueAsync("Dismissed 1", status: OverseerFeedbackStatus.Dismissed, firstSeen: T0);
        await AddIssueAsync("Dismissed 2", status: OverseerFeedbackStatus.Dismissed, firstSeen: T0);

        var counts = OkValue<AdminFeedbackCountsDto>(await _controller.GetCounts(TestContext.Current.CancellationToken));

        Assert.Equal(2, counts.New);
        Assert.Equal(1, counts.Regressed);
        Assert.Equal(1, counts.Acknowledged);
        Assert.Equal(1, counts.Resolved);
        Assert.Equal(2, counts.Dismissed);
    }

    // --- Triage ---

    [Fact]
    public async Task ASavedStatusRecordsTheTextFieldsAndTheTriager()
    {
        await AddUserAsync(AdminId, AdminName);
        var issue = await AddIssueAsync("Dilithium crystal", status: OverseerFeedbackStatus.Regressed);

        var dto = OkValue<AdminFeedbackIssueDto>(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto
        {
            Status = "Resolved",
            Note = "  Wiki page added.  ",
            Resolution = "  Added the base price to the Dilithium crystal page.  ",
            FixedInVersion = " 1.4.1+build-7 "
        }));

        Assert.Equal("resolved", dto.Status);
        Assert.Equal("Wiki page added.", dto.TriageNote);
        Assert.Equal("Added the base price to the Dilithium crystal page.", dto.Resolution);
        Assert.Equal("1.4.1+build-7", dto.FixedInVersion);
        Assert.Equal(AdminName, dto.TriagedByUserName);
        Assert.NotNull(dto.TriagedUtc);

        var stored = await StoredAsync(issue.Id);
        Assert.Equal(OverseerFeedbackStatus.Resolved, stored.Status);
        Assert.Equal("Wiki page added.", stored.TriageNote);
        Assert.Equal("Added the base price to the Dilithium crystal page.", stored.Resolution);
        Assert.Equal("1.4.1+build-7", stored.FixedInVersion);
        Assert.Equal(AdminId, stored.TriagedByUserId);
        Assert.NotNull(stored.TriagedUtc);
    }

    [Fact]
    public async Task AnAbsentTextFieldKeepsTheStoredValue_AndAnEmptyOneClearsIt()
    {
        var issue = await AddIssueAsync("Dilithium crystal");
        OkValue<AdminFeedbackIssueDto>(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto
        {
            Status = "acknowledged", Note = "Looking.", Resolution = "Planned.", FixedInVersion = "1.4.1"
        }));

        // A status change without the text fields keeps them, the note included.
        OkValue<AdminFeedbackIssueDto>(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto { Status = "resolved" }));
        var stored = await StoredAsync(issue.Id);
        Assert.Equal(OverseerFeedbackStatus.Resolved, stored.Status);
        Assert.Equal("Looking.", stored.TriageNote);
        Assert.Equal("Planned.", stored.Resolution);
        Assert.Equal("1.4.1", stored.FixedInVersion);

        // One field cleared, the others kept.
        OkValue<AdminFeedbackIssueDto>(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto { Status = "resolved", Note = "" }));
        stored = await StoredAsync(issue.Id);
        Assert.Null(stored.TriageNote);
        Assert.Equal("Planned.", stored.Resolution);
        Assert.Equal("1.4.1", stored.FixedInVersion);

        // Blank clears as empty does.
        OkValue<AdminFeedbackIssueDto>(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto
        {
            Status = "dismissed", Resolution = "   ", FixedInVersion = ""
        }));
        stored = await StoredAsync(issue.Id);
        Assert.Equal(OverseerFeedbackStatus.Dismissed, stored.Status);
        Assert.Null(stored.TriageNote);
        Assert.Null(stored.Resolution);
        Assert.Null(stored.FixedInVersion);
    }

    [Fact]
    public async Task OverLongTextAndAMalformedVersionAreRefusedAndChangeNothing()
    {
        var issue = await AddIssueAsync("Dilithium crystal", status: OverseerFeedbackStatus.Acknowledged);

        AssertBadRequest(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto
        {
            Status = "resolved", Note = new string('n', AdminFeedbackController.MaxTriageNoteLength + 1)
        }), "note");
        AssertBadRequest(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto
        {
            Status = "resolved", Resolution = new string('r', AdminFeedbackController.MaxResolutionLength + 1)
        }), "resolution");
        AssertBadRequest(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto
        {
            Status = "resolved", FixedInVersion = new string('1', AdminFeedbackController.MaxFixedInVersionLength + 1)
        }), "fixed-in version");
        AssertBadRequest(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto { Status = "resolved", FixedInVersion = "1.4 beta" }));
        AssertBadRequest(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto { Status = "resolved", FixedInVersion = "v1_4" }));

        var stored = await StoredAsync(issue.Id);
        Assert.Equal(OverseerFeedbackStatus.Acknowledged, stored.Status);
        Assert.Null(stored.TriageNote);
        Assert.Null(stored.Resolution);
        Assert.Null(stored.FixedInVersion);
        Assert.Null(stored.TriagedByUserId);

        // Exactly at the limits is accepted.
        OkValue<AdminFeedbackIssueDto>(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto
        {
            Status = "resolved",
            Note = new string('n', AdminFeedbackController.MaxTriageNoteLength),
            Resolution = new string('r', AdminFeedbackController.MaxResolutionLength),
            FixedInVersion = new string('1', AdminFeedbackController.MaxFixedInVersionLength)
        }));
        stored = await StoredAsync(issue.Id);
        Assert.Equal(AdminFeedbackController.MaxTriageNoteLength, stored.TriageNote!.Length);
        Assert.Equal(AdminFeedbackController.MaxResolutionLength, stored.Resolution!.Length);
        Assert.Equal(AdminFeedbackController.MaxFixedInVersionLength, stored.FixedInVersion!.Length);
    }

    [Fact]
    public async Task RegressedAndUnknownStatusesAreRefusedAndAnUnknownIssueIsNotFound()
    {
        var issue = await AddIssueAsync("Dilithium crystal", status: OverseerFeedbackStatus.Resolved);

        AssertBadRequest(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto { Status = "regressed" }));
        AssertBadRequest(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto { Status = "REGRESSED" }));
        AssertBadRequest(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto { Status = "closed" }));
        AssertBadRequest(await _controller.SetStatus(issue.Id, null));
        Assert.IsType<NotFoundObjectResult>(await _controller.SetStatus(9999, new AdminFeedbackStatusUpdateDto { Status = "resolved" }));

        var stored = await StoredAsync(issue.Id);
        Assert.Equal(OverseerFeedbackStatus.Resolved, stored.Status);
        Assert.Null(stored.TriagedByUserId);
    }

    // --- Delete ---

    [Fact]
    public async Task DeletingAnIssueRemovesItsOccurrencesOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var issue = await AddIssueAsync("Dilithium crystal");
        var other = await AddIssueAsync("Other");
        await AddReportAsync(issue, 1, T0);
        await AddReportAsync(issue, 2, T0);
        await AddReportAsync(other, 1, T0);

        Assert.IsType<NoContentResult>(await _controller.DeleteIssue(issue.Id));

        Assert.False(await _db.OverseerFeedbackIssues.AnyAsync(i => i.Id == issue.Id, ct));
        Assert.False(await _db.OverseerFeedbackReports.AnyAsync(r => r.IssueId == issue.Id, ct));
        Assert.True(await _db.OverseerFeedbackIssues.AnyAsync(i => i.Id == other.Id, ct));
        Assert.Equal(1, await _db.OverseerFeedbackReports.CountAsync(r => r.IssueId == other.Id, ct));

        Assert.IsType<NotFoundObjectResult>(await _controller.DeleteIssue(issue.Id));
    }
}

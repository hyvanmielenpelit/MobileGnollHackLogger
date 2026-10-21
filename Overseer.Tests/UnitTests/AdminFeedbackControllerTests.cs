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
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The admin Feedback API on the in-memory provider: the switch, the issue list with its filters, sorts, date
/// overlap rule and per-issue aggregates, occurrences, counts, triage and deletion.
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
        _controller = new AdminFeedbackController(_db, feedbackSwitch)
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
        int occurrenceCount = 1, string summary = "Something was missing.", string? triagedBy = null)
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
            Status = status,
            TriagedByUserId = triagedBy,
            TriagedUtc = triagedBy == null ? null : T0
        };
        _db.OverseerFeedbackIssues.Add(issue);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return issue;
    }

    private async Task<OverseerFeedbackReport> AddReportAsync(OverseerFeedbackIssue issue, long sessionId, DateTime createdUtc,
        string? userId = "user-1", string? modelId = "model-a", string? expectedLocation = null, string? relatedToolsJson = null,
        string? suspectedCause = null, string? appVersion = null)
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
            Provider = "TestProvider",
            ExpectedLocation = expectedLocation,
            RelatedToolsJson = relatedToolsJson,
            SuspectedCause = suspectedCause,
            AppVersion = appVersion
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

    private static T OkValue<T>(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value);
    }

    private async Task<AdminFeedbackIssuePageDto> ListAsync(DateTime? startDate = null, DateTime? endDate = null,
        string? status = null, string? category = null, string? modelId = null, string? search = null, string? sort = null,
        int skip = 0, int take = AdminFeedbackController.DefaultIssueTake)
        => OkValue<AdminFeedbackIssuePageDto>(await _controller.ListIssues(startDate, endDate, status, category, modelId,
            search, sort, skip, take, TestContext.Current.CancellationToken));

    private static void AssertBadRequest(IActionResult result) => Assert.IsType<BadRequestObjectResult>(result);

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

        var page = await ListAsync(startDate: start, endDate: end);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(
            new[] { "Ends inside", "Spans the range", "Starts on the last day" },
            page.Rows.Select(r => r.Subject).OrderBy(s => s, StringComparer.Ordinal));

        Assert.Equal(5, (await ListAsync()).TotalCount);
        Assert.Equal(4, (await ListAsync(startDate: start)).TotalCount);
        Assert.Equal(4, (await ListAsync(endDate: end)).TotalCount);
    }

    // --- Issues: filters, search and sorts ---

    [Fact]
    public async Task TheStatusListCategoryAndModelFiltersCombine()
    {
        var newIssue = await AddIssueAsync("New one", MissingInformation, OverseerFeedbackStatus.New);
        var regressed = await AddIssueAsync("Regressed one", ToolProblem, OverseerFeedbackStatus.Regressed);
        await AddIssueAsync("Resolved one", MissingInformation, OverseerFeedbackStatus.Resolved);
        await AddIssueAsync("Dismissed one", OwnMistake, OverseerFeedbackStatus.Dismissed);
        await AddReportAsync(newIssue, 1, T0, modelId: "model-a");
        await AddReportAsync(regressed, 2, T0, modelId: "model-b");

        var open = await ListAsync(status: "new, regressed");
        Assert.Equal(new[] { "New one", "Regressed one" }, open.Rows.Select(r => r.Subject).OrderBy(s => s, StringComparer.Ordinal));
        Assert.Contains(open.Rows, r => r.Status == "regressed");

        var missing = await ListAsync(category: MissingInformation);
        Assert.Equal(new[] { "New one", "Resolved one" }, missing.Rows.Select(r => r.Subject).OrderBy(s => s, StringComparer.Ordinal));

        var byModel = await ListAsync(modelId: "model-b");
        Assert.Equal("Regressed one", Assert.Single(byModel.Rows).Subject);

        Assert.Empty((await ListAsync(status: "new", modelId: "model-b")).Rows);
        Assert.Equal("Dismissed one", Assert.Single((await ListAsync(status: "dismissed", category: OwnMistake)).Rows).Subject);
    }

    [Fact]
    public async Task SearchMatchesSubjectAndSummaryIgnoringCase()
    {
        await AddIssueAsync("Dilithium crystal", summary: "Price not found.");
        await AddIssueAsync("Elbereth", summary: "The engraving rules for DILITHIUM were missing.");
        await AddIssueAsync("Cockatrice corpse", summary: "Weight missing.");

        var page = await ListAsync(search: "  dilithium ");

        Assert.Equal(new[] { "Dilithium crystal", "Elbereth" }, page.Rows.Select(r => r.Subject).OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheThreeSortsOrderDescending()
    {
        await AddIssueAsync("A", firstSeen: T0, lastSeen: T0.AddDays(5), occurrenceCount: 2);
        await AddIssueAsync("B", firstSeen: T0.AddDays(2), lastSeen: T0.AddDays(3), occurrenceCount: 9);
        await AddIssueAsync("C", firstSeen: T0.AddDays(1), lastSeen: T0.AddDays(4), occurrenceCount: 5);

        Assert.Equal(new[] { "A", "C", "B" }, (await ListAsync()).Rows.Select(r => r.Subject));
        Assert.Equal(new[] { "A", "C", "B" }, (await ListAsync(sort: "lastSeen")).Rows.Select(r => r.Subject));
        Assert.Equal(new[] { "B", "C", "A" }, (await ListAsync(sort: "firstSeen")).Rows.Select(r => r.Subject));
        Assert.Equal(new[] { "B", "C", "A" }, (await ListAsync(sort: "count")).Rows.Select(r => r.Subject));
    }

    [Fact]
    public async Task InvalidIssueQueriesAreRefused()
    {
        var ct = TestContext.Current.CancellationToken;

        AssertBadRequest(await _controller.ListIssues(status: "new,bogus", ct: ct));
        AssertBadRequest(await _controller.ListIssues(category: "bogus", ct: ct));
        AssertBadRequest(await _controller.ListIssues(sort: "subject", ct: ct));
        AssertBadRequest(await _controller.ListIssues(search: new string('x', AdminFeedbackController.MaxSearchLength + 1), ct: ct));
    }

    // --- Models ---

    [Fact]
    public async Task TheModelsEndpointListsEachModelOnce()
    {
        var a = await AddIssueAsync("A");
        var b = await AddIssueAsync("B");
        await AddReportAsync(a, 1, T0, modelId: "model-b");
        await AddReportAsync(a, 2, T0, modelId: "model-a");
        await AddReportAsync(b, 1, T0, modelId: "model-b");
        await AddReportAsync(b, 3, T0, modelId: null);

        var models = OkValue<List<string>>(await _controller.ListModels(TestContext.Current.CancellationToken));

        Assert.Equal(new[] { "model-a", "model-b" }, models);
    }

    // --- Switch ---

    [Fact]
    public async Task TheSwitchStartsSeededAndASetIsAppendedAndAttributed()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddUserAsync(AdminId, AdminName);

        var seeded = OkValue<AdminFeedbackSwitchDto>(await _controller.GetSwitch(ct));
        Assert.True(seeded.Enabled);
        Assert.True(seeded.Seeded);
        Assert.Null(seeded.SetAtUtc);
        Assert.Null(seeded.SetByUserName);

        var set = OkValue<AdminFeedbackSwitchDto>(await _controller.SetSwitch(new AdminFeedbackSwitchUpdateDto { Enabled = false }));
        Assert.False(set.Enabled);
        Assert.False(set.Seeded);
        Assert.NotNull(set.SetAtUtc);
        Assert.Equal(AdminName, set.SetByUserName);

        var again = OkValue<AdminFeedbackSwitchDto>(await _controller.GetSwitch(ct));
        Assert.False(again.Enabled);
        Assert.False(again.Seeded);
        Assert.Equal(set.SetAtUtc, again.SetAtUtc);
        Assert.Equal(AdminName, again.SetByUserName);

        await _controller.SetSwitch(new AdminFeedbackSwitchUpdateDto { Enabled = false });
        Assert.Equal(2, await _db.OverseerFeedbackSettings.CountAsync(ct));
        Assert.All(await _db.OverseerFeedbackSettings.ToListAsync(ct), s => Assert.Equal(AdminId, s.SetByUserId));

        AssertBadRequest(await _controller.SetSwitch(null));
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

        var first = OkValue<AdminFeedbackOccurrencePageDto>(await _controller.ListOccurrences(issue.Id, skip: 0, take: 2, ct));
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(new long[] { 2, 3 }, first.Rows.Select(r => r.ChatSessionId));
        Assert.Null(first.Rows[0].UserName);
        Assert.Equal("reporter", first.Rows[1].UserName);
        Assert.Equal("TestProvider", first.Rows[1].Provider);

        var second = OkValue<AdminFeedbackOccurrencePageDto>(await _controller.ListOccurrences(issue.Id, skip: 2, take: 2, ct));
        var last = Assert.Single(second.Rows);
        Assert.Equal(1, last.ChatSessionId);
        Assert.Equal(new[] { "wiki_search" }, last.RelatedTools);

        Assert.IsType<NotFoundObjectResult>(await _controller.ListOccurrences(9999, ct: ct));
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
    public async Task ASavedStatusRecordsTheNoteAndTheTriager()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddUserAsync(AdminId, AdminName);
        var issue = await AddIssueAsync("Dilithium crystal", status: OverseerFeedbackStatus.Regressed);

        var dto = OkValue<AdminFeedbackIssueDto>(await _controller.SetStatus(issue.Id,
            new AdminFeedbackStatusUpdateDto { Status = "Resolved", Note = "  Wiki page added.  " }));

        Assert.Equal("resolved", dto.Status);
        Assert.Equal("Wiki page added.", dto.TriageNote);
        Assert.Equal(AdminName, dto.TriagedByUserName);
        Assert.NotNull(dto.TriagedUtc);

        var stored = await _db.OverseerFeedbackIssues.AsNoTracking().SingleAsync(i => i.Id == issue.Id, ct);
        Assert.Equal(OverseerFeedbackStatus.Resolved, stored.Status);
        Assert.Equal("Wiki page added.", stored.TriageNote);
        Assert.Equal(AdminId, stored.TriagedByUserId);
        Assert.NotNull(stored.TriagedUtc);

        await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto { Status = "dismissed", Note = new string('n', 1500) });
        stored = await _db.OverseerFeedbackIssues.AsNoTracking().SingleAsync(i => i.Id == issue.Id, ct);
        Assert.Equal(OverseerFeedbackStatus.Dismissed, stored.Status);
        Assert.Equal(AdminFeedbackController.MaxTriageNoteLength, stored.TriageNote!.Length);

        await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto { Status = "acknowledged", Note = "   " });
        stored = await _db.OverseerFeedbackIssues.AsNoTracking().SingleAsync(i => i.Id == issue.Id, ct);
        Assert.Null(stored.TriageNote);
    }

    [Fact]
    public async Task RegressedAndUnknownStatusesAreRefusedAndAnUnknownIssueIsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var issue = await AddIssueAsync("Dilithium crystal", status: OverseerFeedbackStatus.Resolved);

        AssertBadRequest(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto { Status = "regressed" }));
        AssertBadRequest(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto { Status = "REGRESSED" }));
        AssertBadRequest(await _controller.SetStatus(issue.Id, new AdminFeedbackStatusUpdateDto { Status = "closed" }));
        AssertBadRequest(await _controller.SetStatus(issue.Id, null));
        Assert.IsType<NotFoundObjectResult>(await _controller.SetStatus(9999, new AdminFeedbackStatusUpdateDto { Status = "resolved" }));

        var stored = await _db.OverseerFeedbackIssues.AsNoTracking().SingleAsync(i => i.Id == issue.Id, ct);
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

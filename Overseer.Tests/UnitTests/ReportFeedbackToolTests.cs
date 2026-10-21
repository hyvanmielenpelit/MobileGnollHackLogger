namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;
using Overseer.Services.Feedback;
using Overseer.Services.Privacy;
using Overseer.Services.Privacy.Dlp;
using Overseer.Services.Tools;
using Overseer.Tests.Helpers;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

/// <summary>
/// <c>report_feedback</c>: the order of its refusals, the benchmark acknowledgment, grouping into
/// issues with set-based counts and the status rules on recurrence, the caps, scrubbing, the
/// storage-failure rule and the log. Every test runs on SQLite (<see cref="SqliteSchemaTemplate"/>),
/// because the handler's count updates are <c>ExecuteUpdateAsync</c>, which the in-memory provider
/// cannot run, and because the unique-index races need a relational database.
/// </summary>
public sealed class ReportFeedbackToolTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string UserId = "feedback-user";
    private const string OtherUserId = "feedback-other-user";

    private readonly SqliteConnection _connection = SqliteSchemaTemplate.OpenCopy();
    private readonly DbContextOptions<ApplicationDbContext> _plainOptions;
    private readonly RaceInterceptor _race = new();
    private readonly FailingCommandInterceptor _failing = new();
    private readonly ServiceProvider _provider;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private readonly RecordingLogger _logger = new();
    private readonly DlpScannerService _scanner = new(new ConfigurationBuilder().Build());

    public ReportFeedbackToolTests()
    {
        _plainOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        using (var db = new ApplicationDbContext(_plainOptions))
        {
            db.Users.Add(new ApplicationUser { Id = UserId, UserName = "feedbackuser", NormalizedUserName = "FEEDBACKUSER" });
            db.Users.Add(new ApplicationUser { Id = OtherUserId, UserName = "otheruser", NormalizedUserName = "OTHERUSER" });
            db.SaveChanges();
        }

        var connection = _connection;
        var race = _race;
        var failing = _failing;
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection).AddInterceptors(race, failing));
        services.AddSingleton<IToolHandler>(new NamedToolHandler("wiki_search"));
        services.AddSingleton<IToolHandler>(new NamedToolHandler("wiki_view"));
        services.AddSingleton<IToolHandler>(new NamedToolHandler("source_code_search"));
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _cache.Dispose();
        _connection.Dispose();
    }

    // -- fixtures ------------------------------------------------------------------------------------

    private sealed class NamedToolHandler : IToolHandler
    {
        public NamedToolHandler(string name) => ToolName = name;

        public string ToolName { get; }
        public string Description { get; set; } = "Named probe.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult { Success = true });
    }

    private sealed record LogEntry(LogLevel Level, string Message, string? Exception);

    private sealed class RecordingLogger : ILogger<ReportFeedbackTool>
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception?.ToString()));
        }
    }

    /// <summary>
    /// Runs <see cref="Act"/> once, at the first SaveChanges whose context <see cref="When"/> matches,
    /// before that SaveChanges writes anything: a parallel request winning the race.
    /// </summary>
    private sealed class RaceInterceptor : SaveChangesInterceptor
    {
        public Func<DbContext, bool>? When { get; set; }
        public Action? Act { get; set; }
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && When != null && Act != null && eventData.Context != null && When(eventData.Context))
            {
                Fired = true;
                Act();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SimulatedDatabaseFailure : DbException
    {
        public SimulatedDatabaseFailure() : base("Simulated database failure.")
        {
        }
    }

    /// <summary>Once armed, every command throws <see cref="SimulatedDatabaseFailure"/>.</summary>
    private sealed class FailingCommandInterceptor : DbCommandInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed) throw new SimulatedDatabaseFailure();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            if (Armed) throw new SimulatedDatabaseFailure();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed) throw new SimulatedDatabaseFailure();
            return ValueTask.FromResult(result);
        }
    }

    private static IConfiguration Configuration(int dailyCap)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Feedback:Enabled"] = "true",
            ["Feedback:MaxReportsPerUserPerDay"] = dailyCap.ToString()
        }).Build();

    private (ReportFeedbackTool Tool, FeedbackSwitchService Switch) CreateTool(int dailyCap = 20)
    {
        var configuration = Configuration(dailyCap);
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        var feedbackSwitch = new FeedbackSwitchService(scopeFactory, _cache, configuration);
        var tool = new ReportFeedbackTool(scopeFactory, configuration, feedbackSwitch, _scanner, _logger, _provider);
        return (tool, feedbackSwitch);
    }

    private static ToolExecutionContext Chat(long sessionId = 1, AgentRunBudget? budget = null, string userId = UserId) => new()
    {
        SessionId = SessionRef.Persistent(sessionId),
        UserId = userId,
        ToolCallId = "call-" + sessionId,
        AgentName = "Coordinator",
        TurnUserMessageId = 500 + sessionId,
        ActiveProvider = "Anthropic",
        ActiveModelId = "claude-test-model",
        Budget = budget ?? new AgentRunBudget()
    };

    private const string Subject = "Dilithium crystal";
    private const string Summary = "The wiki has no base price for dilithium crystals.";

    private static JsonElement Arguments(string subject = Subject, string summary = Summary, string category = "missing_information", string? details = null)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["category"] = category,
            ["subject"] = subject,
            ["summary"] = summary,
            ["details"] = details
        });

    private static JsonElement FullArguments() => JsonSerializer.SerializeToElement(new
    {
        category = "own_mistake",
        subject = "Cockatrice corpse weight",
        summary = "I gave the wrong weight for a cockatrice corpse.",
        details = "The user corrected me with the wiki value.",
        searched_for = "wiki_search 'cockatrice corpse'",
        expected_location = "wiki page Cockatrice",
        related_tools = new[] { "wiki_search", "not_a_tool", "WIKI_VIEW" },
        noticed_via = "user_correction",
        suspected_cause = "did_not_verify",
        suspected_cause_note = "I answered from memory."
    });

    private ApplicationDbContext Db() => new(_plainOptions);

    private async Task<List<OverseerFeedbackIssue>> IssuesAsync()
    {
        await using var db = Db();
        return await db.OverseerFeedbackIssues.AsNoTracking().OrderBy(i => i.Id).ToListAsync(Ct);
    }

    private async Task<List<OverseerFeedbackReport>> ReportsAsync()
    {
        await using var db = Db();
        return await db.OverseerFeedbackReports.AsNoTracking().OrderBy(r => r.Id).ToListAsync(Ct);
    }

    private async Task SetIssueAsync(long issueId, OverseerFeedbackStatus status, DateTime? seen = null)
    {
        await using var db = Db();
        var issue = await db.OverseerFeedbackIssues.SingleAsync(i => i.Id == issueId, Ct);
        issue.Status = status;
        if (seen != null)
        {
            issue.FirstSeenUtc = seen.Value;
            issue.LastSeenUtc = seen.Value;
        }
        await db.SaveChangesAsync(Ct);
    }

    private static void AssertRecorded(ToolResult result)
    {
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(ReportFeedbackTool.RecordedMessage, result.Content);
    }

    private static void AssertNotAvailable(ToolResult result)
    {
        Assert.False(result.Success);
        Assert.Equal(ReportFeedbackTool.NotAvailableMessage, result.ErrorMessage);
    }

    private static string? ExpectedAppVersion()
    {
        string? version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version)) return null;
        return version.Length <= 64 ? version : version.Substring(0, 64);
    }

    // -- the tool contract ---------------------------------------------------------------------------

    [Fact]
    public void TheHandler_IsAServerFeedbackTool_OutsideTheCallBudget()
    {
        IToolHandler tool = CreateTool().Tool;

        Assert.Equal("report_feedback", tool.ToolName);
        Assert.Equal(ToolCategory.Feedback, tool.Category);
        Assert.Equal(ToolExecutionLocation.Server, tool.ExecutionLocation);
        Assert.Equal(10, tool.TimeoutSeconds);
        Assert.False(tool.CountsTowardCallBudget);
        Assert.False(tool.IsExperimental);
        Assert.Equal(3, ReportFeedbackTool.MaxReportsPerTurn);

        var required = tool.ParameterSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "category", "subject", "summary" }, required);
    }

    // -- refusals ------------------------------------------------------------------------------------

    [Fact]
    public async Task AnEphemeralSession_IsRefused()
    {
        var (tool, _) = CreateTool();
        var context = Chat();
        context.SessionId = SessionRef.NewEphemeral();

        AssertNotAvailable(await tool.ExecuteAsync(Arguments(), context, Ct));
        Assert.Empty(await ReportsAsync());
    }

    [Fact]
    public async Task TheDefaultSessionRef_IsRefused()
    {
        var (tool, _) = CreateTool();

        // The shape AdminDevToolsController probes handlers with.
        var context = new ToolExecutionContext { UserId = UserId, MaxResultLength = 10000 };

        AssertNotAvailable(await tool.ExecuteAsync(Arguments(), context, Ct));
        Assert.Empty(await ReportsAsync());
    }

    [Fact]
    public async Task AConfidentialScope_IsRefused()
    {
        var (tool, _) = CreateTool();

        ToolResult result;
        using (ConfidentialExecutionScope.Enter(true))
        {
            result = await tool.ExecuteAsync(Arguments(), Chat(), Ct);
        }

        AssertNotAvailable(result);
        Assert.Empty(await ReportsAsync());
    }

    [Fact]
    public async Task TheSwitchOff_IsRefused()
    {
        var (tool, feedbackSwitch) = CreateTool();
        await feedbackSwitch.SetAsync(false, "admin", Ct);

        AssertNotAvailable(await tool.ExecuteAsync(Arguments(), Chat(), Ct));
        Assert.Empty(await ReportsAsync());
    }

    [Fact]
    public async Task ASubAgent_IsRefused()
    {
        var (tool, _) = CreateTool();
        var context = Chat();
        context.AgentDepth = 1;

        AssertNotAvailable(await tool.ExecuteAsync(Arguments(), context, Ct));
        Assert.Empty(await ReportsAsync());
    }

    [Fact]
    public async Task InvalidArguments_ReturnTheValidationError_AndDoNotUseTheCap()
    {
        var (tool, _) = CreateTool();
        var budget = new AgentRunBudget();

        var result = await tool.ExecuteAsync(Arguments(subject: " "), Chat(budget: budget), Ct);

        Assert.False(result.Success);
        Assert.Contains("'subject'", result.ErrorMessage);
        Assert.True(budget.TryIncrementFeedbackReport(ReportFeedbackTool.MaxReportsPerTurn));
        Assert.True(budget.TryIncrementFeedbackReport(ReportFeedbackTool.MaxReportsPerTurn));
        Assert.True(budget.TryIncrementFeedbackReport(ReportFeedbackTool.MaxReportsPerTurn));
    }

    // -- benchmark -----------------------------------------------------------------------------------

    [Fact]
    public async Task ABenchmarkCandidate_IsAcknowledged_WritesNoRow_AndCountsTowardTheCap()
    {
        var (tool, feedbackSwitch) = CreateTool();
        // The switch does not govern GnollBench.
        await feedbackSwitch.SetAsync(false, "admin", Ct);
        var budget = new AgentRunBudget();
        var context = new ToolExecutionContext
        {
            SessionId = SessionRef.Persistent(9001),
            UserId = UserId,
            IsBenchmarkCandidate = true,
            Budget = budget
        };

        for (int i = 0; i < ReportFeedbackTool.MaxReportsPerTurn; i++)
        {
            AssertRecorded(await tool.ExecuteAsync(Arguments(subject: "Subject " + i), context, Ct));
        }

        var fourth = await tool.ExecuteAsync(Arguments(subject: "Subject 4"), context, Ct);
        Assert.False(fourth.Success);
        Assert.Equal(ReportFeedbackTool.TurnLimitMessage, fourth.ErrorMessage);

        Assert.Empty(await IssuesAsync());
        Assert.Empty(await ReportsAsync());
    }

    // -- storing occurrences -------------------------------------------------------------------------

    [Fact]
    public async Task TheFirstReport_CreatesAnIssueWithCountOne_AndAnOccurrenceWithEveryField()
    {
        var (tool, _) = CreateTool();
        var arguments = FullArguments();
        Assert.True(ReportFeedbackArguments.TryParse(arguments, name => name is "wiki_search" or "WIKI_VIEW", out var parsed, out _));
        string expectedFingerprint = FeedbackFingerprint.Compute(parsed!);
        DateTime before = DateTime.UtcNow.AddSeconds(-1);

        AssertRecorded(await tool.ExecuteAsync(arguments, Chat(sessionId: 7), Ct));

        var issue = Assert.Single(await IssuesAsync());
        Assert.Equal(expectedFingerprint, issue.Fingerprint);
        Assert.Equal("own_mistake", issue.Category);
        Assert.Equal("Cockatrice corpse weight", issue.Subject);
        Assert.Equal("I gave the wrong weight for a cockatrice corpse.", issue.Summary);
        Assert.Equal(1, issue.OccurrenceCount);
        Assert.Equal(0, issue.RegressionCount);
        Assert.Null(issue.LastRegressionUtc);
        Assert.Equal(OverseerFeedbackStatus.New, issue.Status);
        Assert.Equal(issue.FirstSeenUtc, issue.LastSeenUtc);
        Assert.True(issue.FirstSeenUtc >= before);

        var report = Assert.Single(await ReportsAsync());
        Assert.Equal(issue.Id, report.IssueId);
        Assert.Equal(expectedFingerprint, report.Fingerprint);
        Assert.Equal(issue.FirstSeenUtc, report.CreatedUtc);
        Assert.Equal("own_mistake", report.Category);
        Assert.Equal("Cockatrice corpse weight", report.Subject);
        Assert.Equal("I gave the wrong weight for a cockatrice corpse.", report.Summary);
        Assert.Equal("The user corrected me with the wiki value.", report.Details);
        Assert.Equal("wiki_search 'cockatrice corpse'", report.SearchedFor);
        Assert.Equal("wiki page Cockatrice", report.ExpectedLocation);
        Assert.Equal(new[] { "wiki_search", "WIKI_VIEW" }, JsonSerializer.Deserialize<string[]>(report.RelatedToolsJson!));
        Assert.Equal("user_correction", report.NoticedVia);
        Assert.Equal("did_not_verify", report.SuspectedCause);
        Assert.Equal("I answered from memory.", report.SuspectedCauseNote);
        Assert.False(report.Truncated);
        Assert.False(report.Redacted);
        Assert.Equal(7L, report.ChatSessionId);
        Assert.Equal((long?)507, report.TurnUserMessageId);
        Assert.Equal("call-7", report.ToolCallId);
        Assert.Equal("Coordinator", report.AgentName);
        Assert.Equal("Anthropic", report.Provider);
        Assert.Equal("claude-test-model", report.ModelId);
        Assert.Equal(ExpectedAppVersion(), report.AppVersion);
        Assert.Equal(UserId, report.AspNetUserId);
    }

    [Fact]
    public async Task ASecondReport_FromAnotherSession_AddsAnOccurrence_AndAdvancesTheIssue()
    {
        var (tool, _) = CreateTool();
        AssertRecorded(await tool.ExecuteAsync(Arguments(), Chat(sessionId: 1), Ct));
        var first = Assert.Single(await IssuesAsync());
        var longAgo = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SetIssueAsync(first.Id, OverseerFeedbackStatus.New, seen: longAgo);

        // Same subject, normalized differently, and a different summary wording.
        AssertRecorded(await tool.ExecuteAsync(Arguments(subject: "dilithium CRYSTAL!", summary: "No price anywhere."), Chat(sessionId: 2, userId: OtherUserId), Ct));

        var issue = Assert.Single(await IssuesAsync());
        Assert.Equal(2, issue.OccurrenceCount);
        Assert.Equal(longAgo, issue.FirstSeenUtc);
        Assert.True(issue.LastSeenUtc > longAgo);
        Assert.Equal(OverseerFeedbackStatus.New, issue.Status);
        Assert.Equal(Subject, issue.Subject);

        var reports = await ReportsAsync();
        Assert.Equal(2, reports.Count);
        Assert.All(reports, r => Assert.Equal(issue.Id, r.IssueId));
        Assert.Equal(new long[] { 1, 2 }, reports.Select(r => r.ChatSessionId));
    }

    [Fact]
    public async Task ASameSessionRepeat_IsAlreadyRecorded_AndDoesNotUseTheCap()
    {
        var (tool, _) = CreateTool();
        var budget = new AgentRunBudget();

        AssertRecorded(await tool.ExecuteAsync(Arguments(), Chat(budget: budget), Ct));

        var repeat = await tool.ExecuteAsync(Arguments(summary: "Worded another way."), Chat(budget: budget), Ct);
        Assert.True(repeat.Success);
        Assert.Equal(ReportFeedbackTool.AlreadyRecordedMessage, repeat.Content);

        // Two more distinct reports still fit in the turn's three.
        AssertRecorded(await tool.ExecuteAsync(Arguments(subject: "Elbereth"), Chat(budget: budget), Ct));
        AssertRecorded(await tool.ExecuteAsync(Arguments(subject: "Excalibur"), Chat(budget: budget), Ct));

        Assert.Equal(3, (await ReportsAsync()).Count);
        Assert.Equal(1, (await IssuesAsync()).Single(i => i.Subject == Subject).OccurrenceCount);
    }

    [Fact]
    public async Task AResolvedIssue_ThatRecurs_BecomesRegressed()
    {
        var (tool, _) = CreateTool();
        AssertRecorded(await tool.ExecuteAsync(Arguments(), Chat(sessionId: 1), Ct));
        long issueId = Assert.Single(await IssuesAsync()).Id;
        await SetIssueAsync(issueId, OverseerFeedbackStatus.Resolved);

        AssertRecorded(await tool.ExecuteAsync(Arguments(), Chat(sessionId: 2), Ct));

        var issue = Assert.Single(await IssuesAsync());
        Assert.Equal(OverseerFeedbackStatus.Regressed, issue.Status);
        Assert.Equal(1, issue.RegressionCount);
        Assert.NotNull(issue.LastRegressionUtc);
        Assert.Equal(2, issue.OccurrenceCount);
    }

    [Fact]
    public async Task ARegressedIssue_StaysRegressed_AndItsRegressionCountStaysOne()
    {
        var (tool, _) = CreateTool();
        AssertRecorded(await tool.ExecuteAsync(Arguments(), Chat(sessionId: 1), Ct));
        long issueId = Assert.Single(await IssuesAsync()).Id;
        await SetIssueAsync(issueId, OverseerFeedbackStatus.Resolved);
        AssertRecorded(await tool.ExecuteAsync(Arguments(), Chat(sessionId: 2), Ct));

        AssertRecorded(await tool.ExecuteAsync(Arguments(), Chat(sessionId: 3), Ct));

        var issue = Assert.Single(await IssuesAsync());
        Assert.Equal(OverseerFeedbackStatus.Regressed, issue.Status);
        Assert.Equal(1, issue.RegressionCount);
        Assert.Equal(3, issue.OccurrenceCount);
    }

    [Fact]
    public async Task ADismissedIssue_StaysDismissed_AndItsCountAdvances()
    {
        var (tool, _) = CreateTool();
        AssertRecorded(await tool.ExecuteAsync(Arguments(), Chat(sessionId: 1), Ct));
        long issueId = Assert.Single(await IssuesAsync()).Id;
        await SetIssueAsync(issueId, OverseerFeedbackStatus.Dismissed);

        AssertRecorded(await tool.ExecuteAsync(Arguments(), Chat(sessionId: 2), Ct));

        var issue = Assert.Single(await IssuesAsync());
        Assert.Equal(OverseerFeedbackStatus.Dismissed, issue.Status);
        Assert.Equal(0, issue.RegressionCount);
        Assert.Null(issue.LastRegressionUtc);
        Assert.Equal(2, issue.OccurrenceCount);
    }

    // -- caps ----------------------------------------------------------------------------------------

    [Fact]
    public async Task TheFourthRecordedReportInARun_IsRefused()
    {
        var (tool, _) = CreateTool();
        var budget = new AgentRunBudget();

        AssertRecorded(await tool.ExecuteAsync(Arguments(subject: "One"), Chat(budget: budget), Ct));
        AssertRecorded(await tool.ExecuteAsync(Arguments(subject: "Two"), Chat(budget: budget), Ct));
        AssertRecorded(await tool.ExecuteAsync(Arguments(subject: "Three"), Chat(budget: budget), Ct));

        var fourth = await tool.ExecuteAsync(Arguments(subject: "Four"), Chat(budget: budget), Ct);

        Assert.False(fourth.Success);
        Assert.Equal("Feedback limit for this turn reached (3). Do not report again in this turn; continue with the user's request.", fourth.ErrorMessage);
        Assert.Equal(3, (await ReportsAsync()).Count);
    }

    [Fact]
    public async Task TheDailyCap_RefusesReportsBeyondIt_ForThatUserOnly()
    {
        var (tool, _) = CreateTool(dailyCap: 2);

        // An occurrence from yesterday does not count toward today.
        await using (var db = Db())
        {
            var old = new OverseerFeedbackIssue
            {
                Fingerprint = new string('a', 64), Category = "missing_information", Subject = "Old", Summary = "Old.",
                FirstSeenUtc = DateTime.UtcNow.AddDays(-1), LastSeenUtc = DateTime.UtcNow.AddDays(-1), OccurrenceCount = 1
            };
            db.OverseerFeedbackIssues.Add(old);
            await db.SaveChangesAsync(Ct);
            db.OverseerFeedbackReports.Add(new OverseerFeedbackReport
            {
                IssueId = old.Id, Fingerprint = old.Fingerprint, CreatedUtc = DateTime.UtcNow.Date.AddSeconds(-1),
                Category = old.Category, Subject = old.Subject, Summary = old.Summary, ChatSessionId = 99, AspNetUserId = UserId
            });
            await db.SaveChangesAsync(Ct);
        }

        AssertRecorded(await tool.ExecuteAsync(Arguments(subject: "One"), Chat(sessionId: 1), Ct));
        AssertRecorded(await tool.ExecuteAsync(Arguments(subject: "Two"), Chat(sessionId: 2), Ct));

        var third = await tool.ExecuteAsync(Arguments(subject: "Three"), Chat(sessionId: 3), Ct);
        Assert.False(third.Success);
        Assert.Equal("Feedback limit for today reached. Continue with the user's request.", third.ErrorMessage);

        AssertRecorded(await tool.ExecuteAsync(Arguments(subject: "Three"), Chat(sessionId: 4, userId: OtherUserId), Ct));
    }

    // -- scrubbing, failure and the log --------------------------------------------------------------

    [Fact]
    public async Task AScrubbedField_IsStoredScrubbed_AndMarkedRedacted()
    {
        var (tool, _) = CreateTool();

        AssertRecorded(await tool.ExecuteAsync(Arguments(details: "The user asked me to write to ada@example.com about it."), Chat(), Ct));

        var report = Assert.Single(await ReportsAsync());
        Assert.Equal("The user asked me to write to [redacted] about it.", report.Details);
        Assert.True(report.Redacted);
        Assert.Equal(Subject, report.Subject);
    }

    [Fact]
    public async Task AThrownDatabaseException_ReturnsNotAvailable_AndLogsNoText()
    {
        var (tool, feedbackSwitch) = CreateTool();
        Assert.True(feedbackSwitch.IsEnabled);
        var budget = new AgentRunBudget();
        _failing.Armed = true;

        var result = await tool.ExecuteAsync(Arguments(), Chat(budget: budget), Ct);

        _failing.Armed = false;
        AssertNotAvailable(result);
        Assert.Empty(await ReportsAsync());

        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("missing_information", warning.Message);
        Assert.All(_logger.Entries, e =>
        {
            Assert.DoesNotContain(Subject, e.Message);
            Assert.DoesNotContain(Summary, e.Message);
            Assert.DoesNotContain(Subject, e.Exception ?? "");
            Assert.DoesNotContain(Summary, e.Exception ?? "");
        });

        // The failed report used none of the turn's three.
        for (int i = 0; i < ReportFeedbackTool.MaxReportsPerTurn; i++)
        {
            Assert.True(budget.TryIncrementFeedbackReport(ReportFeedbackTool.MaxReportsPerTurn));
        }
    }

    [Fact]
    public async Task NoLoggedMessage_ContainsTheSummaryOrSubject()
    {
        var (tool, _) = CreateTool();

        AssertRecorded(await tool.ExecuteAsync(Arguments(details: "Some details."), Chat(sessionId: 1), Ct));
        await tool.ExecuteAsync(Arguments(), Chat(sessionId: 1), Ct);
        AssertRecorded(await tool.ExecuteAsync(Arguments(), Chat(sessionId: 2), Ct));

        var entries = _logger.Entries;
        Assert.Contains(entries, e => e.Message.Contains("missing_information"));
        Assert.All(entries, e =>
        {
            Assert.DoesNotContain(Subject, e.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Summary, e.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Some details.", e.Message);
        });
    }

    // -- unique-index races --------------------------------------------------------------------------

    [Fact]
    public async Task WhenAnotherRequestCreatesTheIssueFirst_TheOccurrenceJoinsIt()
    {
        var (tool, _) = CreateTool();
        Assert.True(ReportFeedbackArguments.TryParse(Arguments(), _ => true, out var parsed, out _));
        string fingerprint = FeedbackFingerprint.Compute(parsed!);

        _race.When = db => db.ChangeTracker.Entries<OverseerFeedbackIssue>().Any(e => e.State == EntityState.Added);
        _race.Act = () =>
        {
            using var db = Db();
            var issue = new OverseerFeedbackIssue
            {
                Fingerprint = fingerprint, Category = "missing_information", Subject = Subject, Summary = Summary,
                FirstSeenUtc = DateTime.UtcNow, LastSeenUtc = DateTime.UtcNow, OccurrenceCount = 1
            };
            db.OverseerFeedbackIssues.Add(issue);
            db.SaveChanges();
            db.OverseerFeedbackReports.Add(new OverseerFeedbackReport
            {
                IssueId = issue.Id, Fingerprint = fingerprint, CreatedUtc = DateTime.UtcNow, Category = issue.Category,
                Subject = Subject, Summary = Summary, ChatSessionId = 50, AspNetUserId = OtherUserId
            });
            db.SaveChanges();
        };

        AssertRecorded(await tool.ExecuteAsync(Arguments(), Chat(sessionId: 51), Ct));

        Assert.True(_race.Fired);
        var issue = Assert.Single(await IssuesAsync());
        Assert.Equal(2, issue.OccurrenceCount);
        var reports = await ReportsAsync();
        Assert.Equal(new long[] { 50, 51 }, reports.Select(r => r.ChatSessionId).OrderBy(id => id));
        Assert.All(reports, r => Assert.Equal(issue.Id, r.IssueId));
    }

    [Fact]
    public async Task WhenAParallelCallInTheSameSessionStoresFirst_ItIsAlreadyRecorded()
    {
        var (tool, _) = CreateTool();
        AssertRecorded(await tool.ExecuteAsync(Arguments(), Chat(sessionId: 1), Ct));
        var existing = Assert.Single(await IssuesAsync());
        var budget = new AgentRunBudget();

        _race.When = db => db.ChangeTracker.Entries<OverseerFeedbackReport>().Any(e => e.State == EntityState.Added);
        _race.Act = () =>
        {
            using var db = Db();
            db.OverseerFeedbackReports.Add(new OverseerFeedbackReport
            {
                IssueId = existing.Id, Fingerprint = existing.Fingerprint, CreatedUtc = DateTime.UtcNow, Category = existing.Category,
                Subject = Subject, Summary = Summary, ChatSessionId = 2, AspNetUserId = UserId
            });
            db.SaveChanges();
        };

        var result = await tool.ExecuteAsync(Arguments(), Chat(sessionId: 2, budget: budget), Ct);

        Assert.True(_race.Fired);
        Assert.True(result.Success);
        Assert.Equal(ReportFeedbackTool.AlreadyRecordedMessage, result.Content);
        Assert.Equal(1, Assert.Single(await IssuesAsync()).OccurrenceCount);
        Assert.Equal(2, (await ReportsAsync()).Count);

        // The lost race used none of the turn's three.
        for (int i = 0; i < ReportFeedbackTool.MaxReportsPerTurn; i++)
        {
            Assert.True(budget.TryIncrementFeedbackReport(ReportFeedbackTool.MaxReportsPerTurn));
        }
    }
}

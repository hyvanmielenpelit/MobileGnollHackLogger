namespace Overseer.Services.Tools;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services.Feedback;
using Overseer.Services.Privacy;
using Overseer.Services.Privacy.Dlp;

/// <summary>
/// <c>report_feedback</c>: the chat model's short report to the development team about missing
/// information, a misbehaving tool or its own mistake. In a persistent, non-confidential chat it is
/// stored as an <see cref="OverseerFeedbackReport"/> grouped under an <see cref="OverseerFeedbackIssue"/>;
/// for a benchmark candidate it is only acknowledged, because the benchmark stores the call itself.
/// </summary>
/// <remarks>
/// Outside the session's tool-call quota. Storage never fails the turn: a storage error returns the
/// not-available result. Only the category and the row ids are logged, never the report's text.
/// </remarks>
public sealed class ReportFeedbackTool : IToolHandler
{
    public const string Name = "report_feedback";
    public const int MaxReportsPerTurn = 3;

    internal const string RecordedMessage =
        "Feedback recorded for the development team. Nothing more is needed; continue with the user's request.";
    internal const string AlreadyRecordedMessage =
        "This feedback was already recorded in this conversation. Continue with the user's request.";
    internal static readonly string TurnLimitMessage =
        $"Feedback limit for this turn reached ({MaxReportsPerTurn}). Do not report again in this turn; continue with the user's request.";
    internal const string DailyLimitMessage =
        "Feedback limit for today reached. Continue with the user's request.";
    internal const string NotAvailableMessage =
        "Feedback reports are not available in this conversation.";

    private const int AppVersionMax = 64;

    private static readonly Lazy<string?> AppVersion = new(ReadAppVersion);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly FeedbackSwitchService _feedbackSwitch;
    private readonly DlpScannerService _dlpScanner;
    private readonly ILogger<ReportFeedbackTool> _logger;

    // Resolved on first use: the handler set contains this handler, so taking it by constructor would close a dependency cycle.
    private readonly Lazy<HashSet<string>> _knownToolNames;

    public string ToolName => Name;
    public string Description { get; set; } = "Sends a short report to the Overseer development team about missing information, a tool that misbehaved, or a mistake made earlier in this conversation.";
    public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
    public ToolCategory Category => ToolCategory.Feedback;
    public JsonElement ParameterSchema { get; }
    public int TimeoutSeconds => 10;
    public bool CountsTowardCallBudget => false;

    public ReportFeedbackTool(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        FeedbackSwitchService feedbackSwitch,
        DlpScannerService dlpScanner,
        ILogger<ReportFeedbackTool> logger,
        IServiceProvider serviceProvider)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _feedbackSwitch = feedbackSwitch;
        _dlpScanner = dlpScanner;
        _logger = logger;
        _knownToolNames = new Lazy<HashSet<string>>(
            () => new HashSet<string>(serviceProvider.GetServices<IToolHandler>().Select(h => h.ToolName), StringComparer.OrdinalIgnoreCase),
            LazyThreadSafetyMode.ExecutionAndPublication);

        ParameterSchema = JsonDocument.Parse(@"
        {
            ""type"": ""object"",
            ""properties"": {
                ""category"": { ""type"": ""string"", ""enum"": [""missing_information"", ""tool_problem"", ""own_mistake""],
                              ""description"": ""What kind of problem this is."" },
                ""subject"": { ""type"": ""string"", ""description"": ""What the problem is about, in two to six words, the same way each time: the game entity, wiki page, source file, tool or fact. Reports with the same category and subject are grouped as one issue. At most 120 characters."" },
                ""summary"": { ""type"": ""string"", ""description"": ""One sentence: what went wrong. At most 300 characters."" },
                ""details"": { ""type"": ""string"", ""description"": ""Optional. What happened, in your own words. At most 2000 characters."" },
                ""searched_for"": { ""type"": ""string"", ""description"": ""Optional. What you looked for, with which tools and queries. At most 500 characters."" },
                ""expected_location"": { ""type"": ""string"", ""description"": ""Optional. Where you expected to find it, e.g. a wiki page title or a source file. At most 300 characters."" },
                ""related_tools"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Optional. Names of the tools involved, at most 8. For tool_problem, the misbehaving tool first."" },
                ""noticed_via"": { ""type"": ""string"", ""enum"": [""user_correction"", ""tool_result"", ""self_review""],
                                 ""description"": ""For own_mistake: how you noticed it."" },
                ""suspected_cause"": { ""type"": ""string"",
                                     ""enum"": [""did_not_verify"", ""misread_tool_result"", ""training_knowledge_conflict"", ""search_strategy"", ""corpus_or_tool_limit"", ""ambiguous_question"", ""answered_too_quickly"", ""other""],
                                     ""description"": ""Optional. Your best guess at the cause. This is a hypothesis, not a finding."" },
                ""suspected_cause_note"": { ""type"": ""string"", ""description"": ""Optional. One or two sentences on the suspected cause. At most 500 characters."" }
            },
            ""required"": [""category"", ""subject"", ""summary""]
        }").RootElement;
    }

    public async Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        // 1. Sub-agents never report.
        if (context.AgentDepth > 0) return NotAvailable();

        // 2. Validation.
        if (!ReportFeedbackArguments.TryParse(parameters, IsKnownTool, out var args, out string? error))
        {
            return new ToolResult { Success = false, ErrorMessage = error };
        }

        // 3. A benchmark candidate's SessionId is a run id; the benchmark stores the call itself. The run's
        //    recorded prompt options decided whether the tool was declared, so no switch is read here.
        if (context.IsBenchmarkCandidate)
        {
            if (context.Budget != null && !context.Budget.TryIncrementFeedbackReport(MaxReportsPerTurn)) return TurnLimit();
            return Recorded();
        }

        // 4. The chat switch is read again here, so a turn already running when it was turned off records nothing.
        if (!context.SessionId.IsPersistent || ConfidentialExecutionScope.IsConfidential || !_feedbackSwitch.IsEnabled())
        {
            return NotAvailable();
        }

        // 5. Scrubbed before the fingerprint, so a subject that was itself a secret groups by its redacted form.
        FeedbackRedaction.Apply(_dlpScanner, args);
        string fingerprint = FeedbackFingerprint.Compute(args);
        long sessionId = context.SessionId.PersistentId;
        string? userId = string.IsNullOrEmpty(context.UserId) ? null : context.UserId;
        bool claimed = false;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            DateTime now = DateTime.UtcNow;

            // 6. Daily cap per user.
            if (userId != null)
            {
                DateTime midnight = now.Date;
                int recordedToday = await db.OverseerFeedbackReports
                    .CountAsync(r => r.AspNetUserId == userId && r.CreatedUtc >= midnight, cancellationToken);
                if (recordedToday >= FeedbackSettings.Read(_configuration).MaxReportsPerUserPerDay)
                {
                    return new ToolResult { Success = false, ErrorMessage = DailyLimitMessage };
                }
            }

            // 7. One occurrence of an issue per conversation.
            if (await db.OverseerFeedbackReports.AnyAsync(r => r.ChatSessionId == sessionId && r.Fingerprint == fingerprint, cancellationToken))
            {
                return AlreadyRecorded();
            }

            // 8. The per-turn cap counts recorded reports only.
            if (context.Budget != null)
            {
                if (!context.Budget.TryIncrementFeedbackReport(MaxReportsPerTurn)) return TurnLimit();
                claimed = true;
            }

            // 9. Store.
            var report = BuildReport(args, fingerprint, sessionId, userId, context, now);
            var stored = await StoreAsync(db, report, args, fingerprint, now, cancellationToken);
            if (stored == null)
            {
                if (claimed) context.Budget!.ReleaseFeedbackReport();
                return AlreadyRecorded();
            }

            _logger.LogInformation("Feedback report recorded: category {Category}, issue {IssueId}, report {ReportId}.",
                args.Category, stored.Value.IssueId, stored.Value.ReportId);
            return Recorded();
        }
        catch (Exception ex)
        {
            if (claimed) context.Budget!.ReleaseFeedbackReport();
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;

            _logger.LogWarning(ex, "Feedback report could not be stored: category {Category}.", args.Category);
            return NotAvailable();
        }
    }

    /// <summary>
    /// Adds the occurrence, creating its issue when none exists, and advances the issue's counts with
    /// set-based updates so concurrent occurrences never lose one. Null when a parallel call in the
    /// same conversation stored the same fingerprint first.
    /// </summary>
    private static async Task<(long IssueId, long ReportId)?> StoreAsync(
        ApplicationDbContext db, OverseerFeedbackReport report, ReportFeedbackArguments args,
        string fingerprint, DateTime now, CancellationToken ct)
    {
        var issue = await db.OverseerFeedbackIssues.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Fingerprint == fingerprint, ct);

        if (issue == null)
        {
            // A temporary key links the occurrence to its new issue, so both are written in one SaveChanges.
            var newIssue = new OverseerFeedbackIssue
            {
                Id = -1,
                Fingerprint = fingerprint,
                Category = args.Category,
                Subject = args.Subject,
                Summary = args.Summary,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                OccurrenceCount = 1,
                Status = OverseerFeedbackStatus.New
            };
            db.OverseerFeedbackIssues.Add(newIssue).Property(i => i.Id).IsTemporary = true;
            report.IssueId = -1;
            db.OverseerFeedbackReports.Add(report);

            try
            {
                await db.SaveChangesAsync(ct);
                return (newIssue.Id, report.Id);
            }
            catch (DbUpdateException)
            {
                // Another request created the issue first: continue with it as an existing issue.
                db.ChangeTracker.Clear();
                issue = await db.OverseerFeedbackIssues.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Fingerprint == fingerprint, ct);
                if (issue == null) throw;
                report = Copy(report);
            }
        }

        report.IssueId = issue.Id;
        db.OverseerFeedbackReports.Add(report);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            long sessionId = report.ChatSessionId;
            if (await db.OverseerFeedbackReports.AnyAsync(r => r.ChatSessionId == sessionId && r.Fingerprint == fingerprint, ct))
            {
                return null;
            }
            throw;
        }

        long issueId = issue.Id;
        await db.OverseerFeedbackIssues
            .Where(i => i.Id == issueId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.OccurrenceCount, i => i.OccurrenceCount + 1)
                .SetProperty(i => i.LastSeenUtc, now), ct);

        // Resolve reopens as Regressed; Dismissed stays dismissed.
        await db.OverseerFeedbackIssues
            .Where(i => i.Id == issueId && i.Status == OverseerFeedbackStatus.Resolved)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, OverseerFeedbackStatus.Regressed)
                .SetProperty(i => i.RegressionCount, i => i.RegressionCount + 1)
                .SetProperty(i => i.LastRegressionUtc, now), ct);

        return (issueId, report.Id);
    }

    private static OverseerFeedbackReport BuildReport(
        ReportFeedbackArguments args, string fingerprint, long sessionId, string? userId,
        ToolExecutionContext context, DateTime now) => new()
    {
        Fingerprint = fingerprint,
        CreatedUtc = now,
        Category = args.Category,
        Subject = args.Subject,
        Summary = args.Summary,
        Details = args.Details,
        SearchedFor = args.SearchedFor,
        ExpectedLocation = args.ExpectedLocation,
        RelatedToolsJson = args.RelatedTools.Count > 0 ? JsonSerializer.Serialize(args.RelatedTools) : null,
        NoticedVia = args.NoticedVia,
        SuspectedCause = args.SuspectedCause,
        SuspectedCauseNote = args.SuspectedCauseNote,
        Truncated = args.Truncated,
        Redacted = args.Redacted,
        ChatSessionId = sessionId,
        TurnUserMessageId = context.TurnUserMessageId,
        ToolCallId = Bounded(context.ToolCallId, 128),
        AgentName = Bounded(context.AgentName, 128),
        Provider = Bounded(context.ActiveProvider, 64),
        ModelId = Bounded(context.ActiveModelId, 256),
        AppVersion = AppVersion.Value,
        AspNetUserId = userId
    };

    // A fresh entity for the retry; the failed SaveChanges may have left state on the first one.
    private static OverseerFeedbackReport Copy(OverseerFeedbackReport r) => new()
    {
        Fingerprint = r.Fingerprint,
        CreatedUtc = r.CreatedUtc,
        Category = r.Category,
        Subject = r.Subject,
        Summary = r.Summary,
        Details = r.Details,
        SearchedFor = r.SearchedFor,
        ExpectedLocation = r.ExpectedLocation,
        RelatedToolsJson = r.RelatedToolsJson,
        NoticedVia = r.NoticedVia,
        SuspectedCause = r.SuspectedCause,
        SuspectedCauseNote = r.SuspectedCauseNote,
        Truncated = r.Truncated,
        Redacted = r.Redacted,
        ChatSessionId = r.ChatSessionId,
        TurnUserMessageId = r.TurnUserMessageId,
        ToolCallId = r.ToolCallId,
        AgentName = r.AgentName,
        Provider = r.Provider,
        ModelId = r.ModelId,
        AppVersion = r.AppVersion,
        AspNetUserId = r.AspNetUserId
    };

    private bool IsKnownTool(string name) => _knownToolNames.Value.Contains(name);

    private static string? Bounded(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value.Substring(0, max);

    private static string? ReadAppVersion()
    {
        string? version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return Bounded(version, AppVersionMax);
    }

    private static ToolResult Recorded() => new() { Success = true, Content = RecordedMessage };
    private static ToolResult AlreadyRecorded() => new() { Success = true, Content = AlreadyRecordedMessage };
    private static ToolResult TurnLimit() => new() { Success = false, ErrorMessage = TurnLimitMessage };
    private static ToolResult NotAvailable() => new() { Success = false, ErrorMessage = NotAvailableMessage };
}

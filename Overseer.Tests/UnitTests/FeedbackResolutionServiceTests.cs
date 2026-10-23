using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Feedback;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The feedback resolution import: rules R1–R9, the preview against the stored issues of both sources, and the
/// transactional apply with its keep and clear rules and its conflict rollback. Runs on SQLite
/// (<see cref="SqliteSchemaTemplate"/>), because the InMemory provider throws on <c>BeginTransactionAsync</c> and
/// would hide a query SQL Server cannot translate.
/// </summary>
public sealed class FeedbackResolutionServiceTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string AdminId = "resolution-import-admin";
    private const string MissingInformation = "missing_information";

    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = SqliteSchemaTemplate.OpenCopy();
    private readonly ApplicationDbContext _db;
    private readonly FeedbackResolutionService _service;
    private readonly SystemAiConfigurationSnapshot _model = BenchmarkModelSnapshots.Model("OpenAI", "model-a");
    private readonly SystemAiConfigurationSnapshot _assessor = BenchmarkModelSnapshots.Model("Google", "grader");

    public FeedbackResolutionServiceTests()
    {
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _service = ServiceOn(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static FeedbackResolutionService ServiceOn(ApplicationDbContext db)
        => new(db, new GnollBenchFeedbackQuery(db), new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static string Fp(int n) => n.ToString("x64");

    // --- Seeding ---

    private async Task<OverseerFeedbackIssue> AddIssueAsync(int n, OverseerFeedbackStatus status = OverseerFeedbackStatus.New,
        DateTime? triagedUtc = null, string? note = null, string? resolution = null, string? fixedInVersion = null,
        DateTime? lastRegressionUtc = null, params DateTime[] reports)
    {
        var issue = new OverseerFeedbackIssue
        {
            Fingerprint = Fp(n),
            Category = MissingInformation,
            Subject = $"Subject {n}",
            Summary = "Summary.",
            FirstSeenUtc = T0,
            LastSeenUtc = reports.Length > 0 ? reports.Max() : T0,
            OccurrenceCount = Math.Max(1, reports.Length),
            Status = status,
            TriageNote = note,
            Resolution = resolution,
            FixedInVersion = fixedInVersion,
            TriagedUtc = triagedUtc,
            TriagedByUserId = triagedUtc.HasValue ? "earlier-admin" : null,
            LastRegressionUtc = lastRegressionUtc,
            RegressionCount = lastRegressionUtc.HasValue ? 1 : 0
        };
        _db.OverseerFeedbackIssues.Add(issue);
        await _db.SaveChangesAsync(Ct);

        long session = n * 1000;
        foreach (var created in reports)
        {
            _db.OverseerFeedbackReports.Add(new OverseerFeedbackReport
            {
                IssueId = issue.Id,
                Fingerprint = issue.Fingerprint,
                CreatedUtc = created,
                Category = MissingInformation,
                Subject = issue.Subject,
                Summary = issue.Summary,
                ChatSessionId = session++
            });
        }
        await _db.SaveChangesAsync(Ct);
        return issue;
    }

    private async Task AddSelfReportAsync(string fingerprint, DateTime started, bool accepted = true)
    {
        var run = new BenchmarkRun
        {
            SuiteName = "Suite One",
            TestedModelSnapshot = _model,
            AssessorModelSnapshot = _assessor,
            CandidatePromptOptionsJson = new BenchmarkCandidatePromptOptions().ToCanonicalJson(),
            HarnessVersion = "60",
            StartedAtUtc = T0
        };
        _db.BenchmarkRuns.Add(run);
        var answer = new BenchmarkRunAnswer { BenchmarkRun = run, QuestionText = "Q?", AnswerText = "A.", StartedAtUtc = started };
        _db.BenchmarkRunAnswers.Add(answer);
        _db.BenchmarkRunAnswerSelfReports.Add(new BenchmarkRunAnswerSelfReport
        {
            BenchmarkRunAnswer = answer,
            Fingerprint = fingerprint,
            Accepted = accepted,
            Category = MissingInformation,
            Subject = "GnollBench subject",
            Summary = "Summary."
        });
        await _db.SaveChangesAsync(Ct);
    }

    private async Task AddTriageAsync(string fingerprint, BenchmarkFeedbackTriageStatus status, DateTime? triagedUtc, DateTime? resolvedUtc = null,
        string? note = null, string? resolution = null)
    {
        _db.BenchmarkFeedbackTriages.Add(new BenchmarkFeedbackTriage
        {
            Fingerprint = fingerprint,
            Status = status,
            TriagedUtc = triagedUtc,
            ResolvedUtc = resolvedUtc,
            TriageNote = note,
            Resolution = resolution,
            TriagedByUserId = "earlier-admin"
        });
        await _db.SaveChangesAsync(Ct);
    }

    private static AdminFeedbackResolutionRequestDto OverseerFile(DateTime? exportedAt, params AdminFeedbackResolutionItemDto[] items)
        => new() { Format = "overseer-feedback-resolutions", Version = 1, Source = "overseer", ExportedAtUtc = exportedAt, Items = items.ToList() };

    private static AdminFeedbackResolutionRequestDto GnollBenchFile(DateTime? exportedAt, params AdminFeedbackResolutionItemDto[] items)
        => new() { Source = "gnollbench", ExportedAtUtc = exportedAt, Items = items.ToList() };

    private async Task<OverseerFeedbackIssue> ReloadAsync(long id)
        => await _db.OverseerFeedbackIssues.AsNoTracking().SingleAsync(i => i.Id == id, Ct);

    private async Task<BenchmarkFeedbackTriage?> TriageAsync(string fingerprint)
        => await _db.BenchmarkFeedbackTriages.AsNoTracking().SingleOrDefaultAsync(t => t.Fingerprint == fingerprint, Ct);

    private static List<string> Errors(AdminFeedbackResolutionRequestDto? request, bool forApply = false)
    {
        Assert.False(FeedbackResolutionService.TryValidate(request, forApply, out _, out var errors));
        return errors.Select(e => e.Message).ToList();
    }

    // --- Validation (R1–R9) ---

    [Fact]
    public void Validate_R1R2R3_TheFileItself()
    {
        var item = new AdminFeedbackResolutionItemDto { Id = 1, Fingerprint = Fp(1) };

        Assert.Contains(Errors(new AdminFeedbackResolutionRequestDto { Format = "overseer-feedback-export", Source = "overseer", Items = new() { item } }),
            m => m.Contains("format must be", StringComparison.Ordinal));
        Assert.Contains(Errors(new AdminFeedbackResolutionRequestDto { Version = 2, Source = "overseer", Items = new() { item } }),
            m => m.Contains("version must be 1", StringComparison.Ordinal));
        Assert.Contains(Errors(new AdminFeedbackResolutionRequestDto { Source = "chat", Items = new() { item } }),
            m => m.Contains("source must be", StringComparison.Ordinal));
        Assert.Contains(Errors(new AdminFeedbackResolutionRequestDto { Source = "overseer", Items = new() }),
            m => m.Contains("non-empty list", StringComparison.Ordinal));
        Assert.Contains(Errors(new AdminFeedbackResolutionRequestDto { Source = "overseer" }),
            m => m.Contains("non-empty list", StringComparison.Ordinal));
        var tooMany = Enumerable.Range(1, FeedbackResolutionService.MaxItems + 1)
            .Select(i => new AdminFeedbackResolutionItemDto { Id = i, Fingerprint = Fp(i) }).ToList();
        Assert.Contains(Errors(new AdminFeedbackResolutionRequestDto { Source = "overseer", Items = tooMany }),
            m => m.Contains("at most 500", StringComparison.Ordinal));
        Assert.Single(Errors(null));

        Assert.True(FeedbackResolutionService.TryValidate(new AdminFeedbackResolutionRequestDto { Source = "Overseer", Items = new() { item } }, false,
            out var file, out _));
        Assert.Equal(FeedbackTransferSources.Overseer, file.Source);
        Assert.Null(file.ExportedAtUtc);
    }

    [Fact]
    public void Validate_R4_UnknownKeysAtTheTopAndInAnItem()
    {
        string json = $$"""
            {
              "format": "overseer-feedback-resolutions",
              "version": 1,
              "source": "overseer",
              "comment": "not a key",
              "items": [ { "id": 42, "fingerprint": "{{Fp(42)}}", "state": "resolved" } ]
            }
            """;
        var request = JsonSerializer.Deserialize<AdminFeedbackResolutionRequestDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var errors = Errors(request);

        Assert.Contains("Unknown key `comment`.", errors);
        Assert.Contains("issues[0] (id 42): unknown key `state`.", errors);
    }

    [Fact]
    public void Validate_R5R6R7_IdsAndFingerprints()
    {
        var overseer = Errors(OverseerFile(null,
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(1) },
            new AdminFeedbackResolutionItemDto { Id = 0, Fingerprint = Fp(2) },
            new AdminFeedbackResolutionItemDto { Id = 3, Fingerprint = Fp(3).ToUpperInvariant().Replace('0', 'A') },
            new AdminFeedbackResolutionItemDto { Id = 4, Fingerprint = "3f2a" },
            new AdminFeedbackResolutionItemDto { Id = 5 },
            new AdminFeedbackResolutionItemDto { Id = 6, Fingerprint = Fp(6) },
            new AdminFeedbackResolutionItemDto { Id = 6, Fingerprint = Fp(7) },
            new AdminFeedbackResolutionItemDto { Id = 8, Fingerprint = Fp(6) }));

        Assert.Contains(overseer, m => m.StartsWith("issues[0]", StringComparison.Ordinal) && m.Contains("id is required", StringComparison.Ordinal));
        Assert.Contains("issues[1] (id 0): id must be a positive integer.", overseer);
        Assert.Contains("issues[2] (id 3): fingerprint must be 64 lowercase hexadecimal characters.", overseer);
        Assert.Contains("issues[3] (id 4): fingerprint must be 64 lowercase hexadecimal characters.", overseer);
        Assert.Contains("issues[4] (id 5): fingerprint is required.", overseer);
        Assert.Contains("issues[6] (id 6): id 6 appears more than once.", overseer);
        Assert.Contains("issues[7] (id 8): this fingerprint appears more than once.", overseer);
        Assert.DoesNotContain(overseer, m => m.StartsWith("issues[5]", StringComparison.Ordinal));

        var gnollBench = Errors(GnollBenchFile(null,
            new AdminFeedbackResolutionItemDto { Id = 1, Fingerprint = Fp(1) },
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(2) },
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(2) }));

        Assert.Contains(gnollBench, m => m.StartsWith("issues[0] (id 1): id is not allowed", StringComparison.Ordinal));
        Assert.Contains(gnollBench, m => m.StartsWith("issues[2]", StringComparison.Ordinal) && m.Contains("appears more than once", StringComparison.Ordinal));
        Assert.DoesNotContain(gnollBench, m => m.StartsWith("issues[1]", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_R8R9_StatusesLengthsAndTheVersionPattern()
    {
        var errors = Errors(OverseerFile(null,
            new AdminFeedbackResolutionItemDto { Id = 1, Fingerprint = Fp(1), Status = "regressed" },
            new AdminFeedbackResolutionItemDto { Id = 2, Fingerprint = Fp(2), Status = "fixed" },
            new AdminFeedbackResolutionItemDto { Id = 3, Fingerprint = Fp(3), Note = new string('n', FeedbackTriageFields.MaxNoteLength + 1) },
            new AdminFeedbackResolutionItemDto { Id = 4, Fingerprint = Fp(4), Resolution = new string('r', FeedbackTriageFields.MaxResolutionLength + 1) },
            new AdminFeedbackResolutionItemDto { Id = 5, Fingerprint = Fp(5), FixedInVersion = "1.4 beta" },
            new AdminFeedbackResolutionItemDto { Id = 6, Fingerprint = Fp(6), Status = " Resolved ", Note = new string('n', FeedbackTriageFields.MaxNoteLength) }));

        Assert.Contains(errors, m => m.StartsWith("issues[0] (id 1): status cannot be `regressed`", StringComparison.Ordinal)
            && m.Contains("recurrence is recorded by the system", StringComparison.Ordinal));
        Assert.Contains(errors, m => m.StartsWith("issues[1] (id 2): unknown status `fixed`", StringComparison.Ordinal));
        Assert.Contains(errors, m => m.StartsWith("issues[2] (id 3): The note can be at most 1000", StringComparison.Ordinal));
        Assert.Contains(errors, m => m.StartsWith("issues[3] (id 4): The resolution can be at most 4000", StringComparison.Ordinal));
        Assert.Contains(errors, m => m.StartsWith("issues[4] (id 5): The fixed-in version", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, m => m.StartsWith("issues[5]", StringComparison.Ordinal));
        Assert.Equal(5, errors.Count);
    }

    [Fact]
    public void Validate_ForApply_RequiresTheExpectedStatus()
    {
        var errors = Errors(OverseerFile(null, new AdminFeedbackResolutionItemDto { Id = 1, Fingerprint = Fp(1), Status = "resolved" }), forApply: true);
        Assert.Contains(errors, m => m.Contains("expectedStatus is required", StringComparison.Ordinal));

        errors = Errors(OverseerFile(null, new AdminFeedbackResolutionItemDto { Id = 1, Fingerprint = Fp(1), ExpectedStatus = "open" }), forApply: true);
        Assert.Contains(errors, m => m.Contains("unknown expectedStatus", StringComparison.Ordinal));

        Assert.True(FeedbackResolutionService.TryValidate(
            OverseerFile(null, new AdminFeedbackResolutionItemDto { Id = 1, Fingerprint = Fp(1), ExpectedStatus = "Regressed" }), true, out _, out _));
    }

    // --- Preview ---

    [Fact]
    public async Task Preview_Overseer_ShowsCurrentAndAfterValues_ChangesAndNotices()
    {
        var a = await AddIssueAsync(1, note: "old note", fixedInVersion: "1.3.0");
        var b = await AddIssueAsync(2, OverseerFeedbackStatus.Acknowledged, T0, note: "kept");
        var c = await AddIssueAsync(3);

        var result = await _service.PreviewAsync(OverseerFile(null,
            new AdminFeedbackResolutionItemDto { Id = a.Id, Fingerprint = a.Fingerprint, Status = "resolved", Resolution = "Added the speed.", Note = "" },
            new AdminFeedbackResolutionItemDto { Id = b.Id, Fingerprint = b.Fingerprint, Status = "acknowledged" },
            new AdminFeedbackResolutionItemDto { Id = c.Id, Fingerprint = c.Fingerprint, Status = "resolved" }), Ct);

        Assert.Equal(FeedbackResolutionOutcome.Ok, result.Outcome);
        var preview = result.Preview!;
        Assert.Equal("overseer", preview.Source);
        Assert.Contains(preview.Notices, n => n.Contains("cannot be detected", StringComparison.Ordinal));
        Assert.Equal(0, preview.ErrorCount);
        Assert.Equal(2, preview.ChangeCount);

        var first = preview.Items[0];
        Assert.True(first.Exists);
        Assert.True(first.FingerprintMatches);
        Assert.Equal("Subject 1", first.Subject);
        Assert.Equal(("new", "old note", (string?)null, "1.3.0"), (first.Current!.Status, first.Current.Note, first.Current.Resolution, first.Current.FixedInVersion));
        Assert.Equal(("resolved", (string?)null, "Added the speed.", "1.3.0"), (first.After!.Status, first.After.Note, first.After.Resolution, first.After.FixedInVersion));
        Assert.Equal((true, true, true, false), (first.Changes.Status, first.Changes.Note, first.Changes.Resolution, first.Changes.FixedInVersion));
        Assert.True(first.HasChanges);
        Assert.Null(first.OccurrencesSinceExport);
        Assert.Empty(first.Notices);

        var second = preview.Items[1];
        Assert.False(second.HasChanges);
        Assert.Equal("kept", second.After!.Note);
        Assert.Contains(second.Notices, n => n.Contains("changes nothing", StringComparison.Ordinal));
        Assert.Equal(T0, second.Current!.TriagedUtc);

        Assert.Contains(preview.Items[2].Notices, n => n.Contains("should say what was done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preview_Overseer_FlagsTriageRegressionsAndOccurrencesSinceTheExport()
    {
        DateTime exportedAt = T0.AddDays(10);
        var changed = await AddIssueAsync(1, OverseerFeedbackStatus.Regressed, exportedAt.AddHours(1), lastRegressionUtc: exportedAt.AddHours(2),
            reports: new[] { T0.AddDays(1), exportedAt.AddHours(2), exportedAt.AddHours(3) });
        var quiet = await AddIssueAsync(2, OverseerFeedbackStatus.Resolved, exportedAt.AddHours(-1), resolution: "Fixed.", lastRegressionUtc: T0.AddDays(2),
            reports: new[] { T0.AddDays(2) });

        var preview = (await _service.PreviewAsync(OverseerFile(exportedAt,
            new AdminFeedbackResolutionItemDto { Id = changed.Id, Fingerprint = changed.Fingerprint, Status = "resolved", Resolution = "Fixed again." },
            new AdminFeedbackResolutionItemDto { Id = quiet.Id, Fingerprint = quiet.Fingerprint, Note = "Checked." }), Ct)).Preview!;

        Assert.Empty(preview.Notices);
        Assert.Equal(1, preview.TriagedSinceExportCount);
        var first = preview.Items[0];
        Assert.True(first.TriagedSinceExport);
        Assert.True(first.RegressedSinceExport);
        Assert.Equal(2, first.OccurrencesSinceExport);
        Assert.Equal("regressed", first.Current!.Status);
        Assert.Equal(exportedAt.AddHours(3), first.Current.LastSeenUtc);

        var second = preview.Items[1];
        Assert.False(second.TriagedSinceExport);
        Assert.False(second.RegressedSinceExport);
        Assert.Equal(0, second.OccurrencesSinceExport);
    }

    [Fact]
    public async Task Preview_AMissingIssueAndAMismatchedFingerprint_AreItemErrors()
    {
        var issue = await AddIssueAsync(1);

        var preview = (await _service.PreviewAsync(OverseerFile(null,
            new AdminFeedbackResolutionItemDto { Id = issue.Id, Fingerprint = Fp(99), Status = "resolved" },
            new AdminFeedbackResolutionItemDto { Id = 9999, Fingerprint = Fp(2), Status = "resolved" }), Ct)).Preview!;

        Assert.Equal(2, preview.ErrorCount);
        Assert.True(preview.Items[0].Exists);
        Assert.False(preview.Items[0].FingerprintMatches);
        Assert.Contains(preview.Items[0].Errors, e => e.Contains("does not belong to issue", StringComparison.Ordinal));
        Assert.Null(preview.Items[0].After);
        Assert.False(preview.Items[1].Exists);
        Assert.Null(preview.Items[1].Current);
        Assert.Contains(preview.Items[1].Errors, e => e.Contains("no Overseer feedback issue has id 9999", StringComparison.Ordinal));

        var gnollBench = (await _service.PreviewAsync(GnollBenchFile(null, new AdminFeedbackResolutionItemDto { Fingerprint = Fp(5), Status = "dismissed" }), Ct)).Preview!;
        Assert.False(gnollBench.Items[0].Exists);
        Assert.Contains(gnollBench.Items[0].Errors, e => e.Contains("no GnollBench self-report has this fingerprint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preview_GnollBench_ReadsTheDerivedStatus_AndChangesSinceTheExport()
    {
        await AddSelfReportAsync(Fp(1), T0);
        await AddSelfReportAsync(Fp(1), T0.AddDays(2));
        await AddTriageAsync(Fp(1), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(1), T0.AddDays(1), resolution: "Added it.");
        await AddSelfReportAsync(Fp(2), T0, accepted: false);
        DateTime exportedAt = T0.AddHours(36);

        var preview = (await _service.PreviewAsync(GnollBenchFile(exportedAt,
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(1), Status = "resolved" },
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(2), Status = "dismissed", Note = "Refused call only." }), Ct)).Preview!;

        var regressed = preview.Items[0];
        Assert.True(regressed.Exists);
        Assert.Null(regressed.Id);
        Assert.Equal("regressed", regressed.Current!.Status);
        Assert.Equal("Added it.", regressed.Current.Resolution);
        Assert.Equal("GnollBench subject", regressed.Subject);
        Assert.True(regressed.Changes.Status);
        Assert.True(regressed.RegressedSinceExport);
        Assert.False(regressed.TriagedSinceExport);
        Assert.Equal(1, regressed.OccurrencesSinceExport);
        Assert.Empty(regressed.Notices);

        var refusedOnly = preview.Items[1];
        Assert.True(refusedOnly.Exists);
        Assert.Equal("new", refusedOnly.Current!.Status);
        Assert.True(refusedOnly.HasChanges);
    }

    [Fact]
    public async Task Preview_RefusesAnInvalidFile()
    {
        var result = await _service.PreviewAsync(OverseerFile(null, new AdminFeedbackResolutionItemDto { Id = 1, Fingerprint = Fp(1), Status = "regressed" }), Ct);

        Assert.Equal(FeedbackResolutionOutcome.Invalid, result.Outcome);
        Assert.Equal(0, Assert.Single(result.Invalid!.Errors).Index);
    }

    // --- Apply ---

    [Fact]
    public async Task Apply_Overseer_WritesTheChangedItems_RecordsTheImporter_AndCountsTheRest()
    {
        var a = await AddIssueAsync(1);
        var b = await AddIssueAsync(2, OverseerFeedbackStatus.Acknowledged, T0, note: "same");

        var result = await _service.ApplyAsync(OverseerFile(T0,
            new AdminFeedbackResolutionItemDto { Id = a.Id, Fingerprint = a.Fingerprint, Status = "resolved", Resolution = "Added the speed.", FixedInVersion = "1.4.1", ExpectedStatus = "new" },
            new AdminFeedbackResolutionItemDto { Id = b.Id, Fingerprint = b.Fingerprint, Note = "same", ExpectedStatus = "acknowledged", ExpectedTriagedUtc = T0 }), AdminId, Ct);

        Assert.Equal(FeedbackResolutionOutcome.Ok, result.Outcome);
        Assert.Equal((1, 1), (result.Applied!.Changed, result.Applied.Unchanged));

        var written = await ReloadAsync(a.Id);
        Assert.Equal(OverseerFeedbackStatus.Resolved, written.Status);
        Assert.Equal("Added the speed.", written.Resolution);
        Assert.Equal("1.4.1", written.FixedInVersion);
        Assert.Equal(AdminId, written.TriagedByUserId);
        Assert.Equal(Now.UtcDateTime, written.TriagedUtc);

        var untouched = await ReloadAsync(b.Id);
        Assert.Equal("earlier-admin", untouched.TriagedByUserId);
        Assert.Equal(T0, untouched.TriagedUtc);
    }

    [Fact]
    public async Task Apply_AnAbsentFieldKeeps_AndAnEmptyOneClears()
    {
        var issue = await AddIssueAsync(1, OverseerFeedbackStatus.Regressed, T0, note: "note", resolution: "resolution", fixedInVersion: "1.3.0");

        var result = await _service.ApplyAsync(OverseerFile(null,
            new AdminFeedbackResolutionItemDto { Id = issue.Id, Fingerprint = issue.Fingerprint, Note = "", Resolution = "  ", ExpectedStatus = "regressed", ExpectedTriagedUtc = T0 }), AdminId, Ct);

        Assert.Equal(FeedbackResolutionOutcome.Ok, result.Outcome);
        var written = await ReloadAsync(issue.Id);
        Assert.Equal(OverseerFeedbackStatus.Regressed, written.Status);
        Assert.Null(written.TriageNote);
        Assert.Null(written.Resolution);
        Assert.Equal("1.3.0", written.FixedInVersion);
    }

    [Fact]
    public async Task Apply_GnollBench_CreatesTheTriageRow_AndSetsResolvedUtc()
    {
        await AddSelfReportAsync(Fp(1), T0);

        var result = await _service.ApplyAsync(GnollBenchFile(null,
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(1), Status = "resolved", Resolution = "Added the page.", FixedInVersion = "1.4.1", ExpectedStatus = "new" }), AdminId, Ct);

        Assert.Equal(FeedbackResolutionOutcome.Ok, result.Outcome);
        Assert.Equal(1, result.Applied!.Changed);
        var row = await TriageAsync(Fp(1));
        Assert.NotNull(row);
        Assert.Equal(BenchmarkFeedbackTriageStatus.Resolved, row!.Status);
        Assert.Equal("Added the page.", row.Resolution);
        Assert.Equal("1.4.1", row.FixedInVersion);
        Assert.Equal(AdminId, row.TriagedByUserId);
        Assert.Equal(Now.UtcDateTime, row.TriagedUtc);
        Assert.Equal(Now.UtcDateTime, row.ResolvedUtc);
    }

    [Fact]
    public async Task Apply_GnollBench_UpdatesExistingRows_WithTheResolvedUtcRules()
    {
        // Fp(1): resolved and quiet; only the note changes. Fp(2): resolved, then reported again. Fp(3): acknowledged.
        await AddSelfReportAsync(Fp(1), T0);
        await AddTriageAsync(Fp(1), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(1), T0.AddDays(1), resolution: "r1");
        await AddSelfReportAsync(Fp(2), T0.AddDays(2));
        await AddTriageAsync(Fp(2), BenchmarkFeedbackTriageStatus.Resolved, T0.AddDays(1), T0.AddDays(1), resolution: "r2");
        await AddSelfReportAsync(Fp(3), T0);
        await AddTriageAsync(Fp(3), BenchmarkFeedbackTriageStatus.Acknowledged, T0.AddDays(1), note: "n3");

        var result = await _service.ApplyAsync(GnollBenchFile(null,
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(1), Note = "Checked again.", ExpectedStatus = "resolved", ExpectedTriagedUtc = T0.AddDays(1) },
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(2), Status = "resolved", ExpectedStatus = "regressed", ExpectedTriagedUtc = T0.AddDays(1) },
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(3), Status = "dismissed", Note = "", ExpectedStatus = "acknowledged", ExpectedTriagedUtc = T0.AddDays(1) }), AdminId, Ct);

        Assert.Equal(FeedbackResolutionOutcome.Ok, result.Outcome);
        Assert.Equal(3, result.Applied!.Changed);

        var kept = (await TriageAsync(Fp(1)))!;
        Assert.Equal(BenchmarkFeedbackTriageStatus.Resolved, kept.Status);
        Assert.Equal(T0.AddDays(1), kept.ResolvedUtc);
        Assert.Equal("Checked again.", kept.TriageNote);
        Assert.Equal("r1", kept.Resolution);

        var reResolved = (await TriageAsync(Fp(2)))!;
        Assert.Equal(BenchmarkFeedbackTriageStatus.Resolved, reResolved.Status);
        Assert.Equal(Now.UtcDateTime, reResolved.ResolvedUtc);
        Assert.Equal("r2", reResolved.Resolution);

        var dismissed = (await TriageAsync(Fp(3)))!;
        Assert.Equal(BenchmarkFeedbackTriageStatus.Dismissed, dismissed.Status);
        Assert.Null(dismissed.ResolvedUtc);
        Assert.Null(dismissed.TriageNote);
        Assert.Equal(AdminId, dismissed.TriagedByUserId);
    }

    [Fact]
    public async Task Apply_AnExpectedTimeRoundedToTheMillisecond_StillMatches()
    {
        DateTime triaged = T0.AddTicks(1234567);
        var issue = await AddIssueAsync(1, OverseerFeedbackStatus.Acknowledged, triaged);
        DateTime rounded = new(triaged.Ticks - triaged.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

        var result = await _service.ApplyAsync(OverseerFile(null,
            new AdminFeedbackResolutionItemDto { Id = issue.Id, Fingerprint = issue.Fingerprint, Status = "dismissed", ExpectedStatus = "acknowledged", ExpectedTriagedUtc = rounded }), AdminId, Ct);

        Assert.Equal(FeedbackResolutionOutcome.Ok, result.Outcome);
        Assert.Equal(OverseerFeedbackStatus.Dismissed, (await ReloadAsync(issue.Id)).Status);
    }

    [Fact]
    public async Task Apply_AChangedIssue_IsAConflict_AndNothingIsWritten()
    {
        var a = await AddIssueAsync(1);
        var b = await AddIssueAsync(2, OverseerFeedbackStatus.Acknowledged, T0.AddDays(5), note: "Someone else.");
        var c = await AddIssueAsync(3, OverseerFeedbackStatus.Acknowledged, T0.AddDays(6));

        var result = await _service.ApplyAsync(OverseerFile(T0,
            new AdminFeedbackResolutionItemDto { Id = a.Id, Fingerprint = a.Fingerprint, Status = "resolved", Resolution = "Done.", ExpectedStatus = "new" },
            new AdminFeedbackResolutionItemDto { Id = b.Id, Fingerprint = b.Fingerprint, Status = "dismissed", ExpectedStatus = "new" },
            new AdminFeedbackResolutionItemDto { Id = c.Id, Fingerprint = c.Fingerprint, Status = "dismissed", ExpectedStatus = "acknowledged", ExpectedTriagedUtc = T0 }), AdminId, Ct);

        Assert.Equal(FeedbackResolutionOutcome.Conflict, result.Outcome);
        var conflicts = result.Conflict!.Conflicts;
        Assert.Equal(new long?[] { b.Id, c.Id }, conflicts.Select(x => x.Id));
        Assert.All(conflicts, x => Assert.Equal("changed", x.Reason));
        Assert.Equal(("new", "acknowledged"), (conflicts[0].ExpectedStatus, conflicts[0].CurrentStatus));
        Assert.Equal(T0.AddDays(5), conflicts[0].CurrentTriagedUtc);
        Assert.Equal(T0, conflicts[1].ExpectedTriagedUtc);
        Assert.Equal(T0.AddDays(6), conflicts[1].CurrentTriagedUtc);

        var unchanged = await ReloadAsync(a.Id);
        Assert.Equal(OverseerFeedbackStatus.New, unchanged.Status);
        Assert.Null(unchanged.Resolution);
        Assert.Null(unchanged.TriagedUtc);
        Assert.Equal(OverseerFeedbackStatus.Acknowledged, (await ReloadAsync(b.Id)).Status);
    }

    [Fact]
    public async Task Apply_AnIssueDeletedSinceThePreview_IsAMissingConflict()
    {
        await AddSelfReportAsync(Fp(1), T0);

        var result = await _service.ApplyAsync(GnollBenchFile(null,
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(1), Status = "dismissed", ExpectedStatus = "new" },
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(2), Status = "dismissed", ExpectedStatus = "new" }), AdminId, Ct);

        Assert.Equal(FeedbackResolutionOutcome.Conflict, result.Outcome);
        var conflict = Assert.Single(result.Conflict!.Conflicts);
        Assert.Equal((Fp(2), "missing"), (conflict.Fingerprint, conflict.Reason));
        Assert.Null(conflict.CurrentStatus);
        Assert.Null(await TriageAsync(Fp(1)));
    }

    [Fact]
    public async Task Apply_AMismatchedFingerprint_IsInvalid()
    {
        var issue = await AddIssueAsync(1);

        var result = await _service.ApplyAsync(OverseerFile(null,
            new AdminFeedbackResolutionItemDto { Id = issue.Id, Fingerprint = Fp(2), Status = "resolved", ExpectedStatus = "new" }), AdminId, Ct);

        Assert.Equal(FeedbackResolutionOutcome.Invalid, result.Outcome);
        Assert.Contains("does not belong to issue", Assert.Single(result.Invalid!.Errors).Message);
        Assert.Equal(OverseerFeedbackStatus.New, (await ReloadAsync(issue.Id)).Status);
    }

    /// <summary>
    /// Before the <c>n</c>th update of a table, runs <see cref="Sql"/> on the same connection and transaction: another
    /// writer changing a row between the check and the write.
    /// </summary>
    private sealed class InterfereBeforeUpdate(string table, int n, string sql) : DbCommandInterceptor
    {
        private int _updates;

        public string Sql { get; } = sql;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith($"UPDATE \"{table}\"", StringComparison.Ordinal) && ++_updates == n)
            {
                await using var other = command.Connection!.CreateCommand();
                other.Transaction = command.Transaction;
                other.CommandText = Sql;
                await other.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }

    [Fact]
    public async Task Apply_AnIssueChangedBetweenTheCheckAndTheWrite_RollsBackTheEarlierWrites()
    {
        var a = await AddIssueAsync(1);
        var b = await AddIssueAsync(2);
        var interceptor = new InterfereBeforeUpdate("OverseerFeedbackIssues", 2,
            $"UPDATE \"OverseerFeedbackIssues\" SET \"Status\" = 'Dismissed' WHERE \"Id\" = {b.Id}");
        await using var raced = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection).AddInterceptors(interceptor).Options);

        var result = await ServiceOn(raced).ApplyAsync(OverseerFile(null,
            new AdminFeedbackResolutionItemDto { Id = a.Id, Fingerprint = a.Fingerprint, Status = "resolved", Resolution = "Done.", ExpectedStatus = "new" },
            new AdminFeedbackResolutionItemDto { Id = b.Id, Fingerprint = b.Fingerprint, Status = "acknowledged", ExpectedStatus = "new" }), AdminId, Ct);

        Assert.Equal(FeedbackResolutionOutcome.Conflict, result.Outcome);
        Assert.Equal(b.Id, Assert.Single(result.Conflict!.Conflicts).Id);
        var first = await ReloadAsync(a.Id);
        Assert.Equal(OverseerFeedbackStatus.New, first.Status);
        Assert.Null(first.Resolution);
        Assert.Null(first.TriagedUtc);
        Assert.Equal(OverseerFeedbackStatus.New, (await ReloadAsync(b.Id)).Status);
    }

    [Fact]
    public async Task Apply_GnollBench_ATriageRowCreatedByAnotherWriter_RollsBackEverything()
    {
        await AddSelfReportAsync(Fp(1), T0);
        await AddTriageAsync(Fp(1), BenchmarkFeedbackTriageStatus.Acknowledged, T0.AddDays(1));
        await AddSelfReportAsync(Fp(2), T0);
        var interceptor = new InterfereBeforeUpdate("BenchmarkFeedbackTriages", 1,
            $"INSERT INTO \"BenchmarkFeedbackTriages\" (\"Fingerprint\", \"Status\") VALUES ('{Fp(2)}', 'Dismissed')");
        await using var raced = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection).AddInterceptors(interceptor).Options);

        var result = await ServiceOn(raced).ApplyAsync(GnollBenchFile(null,
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(1), Status = "resolved", ExpectedStatus = "acknowledged", ExpectedTriagedUtc = T0.AddDays(1) },
            new AdminFeedbackResolutionItemDto { Fingerprint = Fp(2), Status = "resolved", ExpectedStatus = "new" }), AdminId, Ct);

        Assert.Equal(FeedbackResolutionOutcome.Conflict, result.Outcome);
        Assert.Equal(Fp(2), Assert.Single(result.Conflict!.Conflicts).Fingerprint);
        Assert.Equal(BenchmarkFeedbackTriageStatus.Acknowledged, (await TriageAsync(Fp(1)))!.Status);
        Assert.Null(await TriageAsync(Fp(2)));
    }
}

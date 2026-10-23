using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Feedback;

namespace Overseer.Controllers;

/// <summary>
/// The admin Feedback tab's GnollBench sub-tab: the issues GnollBench candidates reported through
/// <c>report_feedback</c>, grouped by fingerprint at query time (<see cref="GnollBenchFeedbackQuery"/>), with their
/// occurrences, facets, per-status counts and triage. There is no delete: self-reports are run records and leave with
/// their run. Errors are <c>{ message }</c>: 400 for invalid input, 404 for an unknown fingerprint. Reads answer 499
/// when the client aborts; the triage action takes no request token, so a dropped connection never stops it part-way.
/// </summary>
[ApiController]
[Route("api/admin/feedback/gnollbench")]
[Authorize(Policy = "AdminOnly")]
public class AdminGnollBenchFeedbackController : ControllerBase
{
    public const int DefaultIssueTake = 25;
    public const int DefaultOccurrenceTake = 10;

    private readonly ApplicationDbContext _db;
    private readonly GnollBenchFeedbackQuery _query;

    public AdminGnollBenchFeedbackController(ApplicationDbContext db, GnollBenchFeedbackQuery query)
    {
        _db = db;
        _query = query;
    }

    private string? CurrentUserId => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// A page of issues. An issue matches when one self-report of the base set matches every occurrence-level filter,
    /// and it is in the date range when its active span overlaps it; <c>endDate</c> includes its whole day.
    /// </summary>
    [HttpGet("issues")]
    public async Task<IActionResult> ListIssues([FromQuery] GnollBenchFeedbackFilterQuery? filter,
        [FromQuery] int skip = 0, [FromQuery] int take = DefaultIssueTake, CancellationToken ct = default)
    {
        if (!GnollBenchFeedbackFilter.TryParse(filter, out var parsed, out var error)) return BadRequest(new { message = error });
        try
        {
            var page = await _query.ListIssuesAsync(parsed, skip, take, ct);
            return Ok(new GnollBenchFeedbackIssuePageDto { TotalCount = page.TotalCount, Rows = await ToIssueDtosAsync(page.Rows, ct) });
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Every matching fingerprint in sort order, at most 1,000, with <c>truncated</c> when the cap cut the list.</summary>
    [HttpGet("issues/ids")]
    public async Task<IActionResult> ListIssueIds([FromQuery] GnollBenchFeedbackFilterQuery? filter, CancellationToken ct = default)
    {
        if (!GnollBenchFeedbackFilter.TryParse(filter, out var parsed, out var error)) return BadRequest(new { message = error });
        try
        {
            var ids = await _query.ListFingerprintsAsync(parsed, GnollBenchFeedbackQuery.MaxIds, ct);
            return Ok(new GnollBenchFeedbackIdsDto { Fingerprints = ids.Fingerprints.ToList(), TotalCount = ids.TotalCount, Truncated = ids.Truncated });
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// A page of one issue's self-reports, newest first, over the <c>accepted</c> base set. The occurrence-level filters
    /// apply unless <paramref name="all"/> is true; the date range and the status never do.
    /// </summary>
    [HttpGet("issues/{fingerprint}/occurrences")]
    public async Task<IActionResult> ListOccurrences(string fingerprint, [FromQuery] GnollBenchFeedbackFilterQuery? filter,
        [FromQuery] bool all = false, [FromQuery] int skip = 0, [FromQuery] int take = DefaultOccurrenceTake, CancellationToken ct = default)
    {
        if (!GnollBenchFeedbackFilter.TryParse(filter, out var parsed, out var error)) return BadRequest(new { message = error });
        try
        {
            var page = await _query.ListOccurrencesAsync(fingerprint, parsed, all, skip, take, ct);
            if (page == null) return NotFound(new { message = "No such GnollBench feedback issue." });
            return Ok(new GnollBenchFeedbackOccurrencePageDto { TotalCount = page.TotalCount, Rows = page.Rows.Select(ToOccurrenceDto).ToList() });
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>The filter options present among the self-reports in the range, each with its number of issues.</summary>
    [HttpGet("facets")]
    public async Task<IActionResult> GetFacets([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null,
        [FromQuery] string? accepted = null, CancellationToken ct = default)
    {
        if (!GnollBenchFeedbackStatuses.TryParseScope(accepted, out var scope))
            return BadRequest(new { message = $"Unknown accepted value '{accepted}'. Use accepted, all or refused." });
        try
        {
            return Ok(await _query.GetFacetsAsync(startDate, endDate, scope, ct));
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Issue counts per derived status over accepted self-reports and all dates, for the sub-tab label.</summary>
    [HttpGet("counts")]
    public async Task<IActionResult> GetCounts(CancellationToken ct = default)
    {
        try
        {
            var counts = await _query.CountByStatusAsync(ct);
            return Ok(new GnollBenchFeedbackCountsDto
            {
                New = counts.GetValueOrDefault(GnollBenchFeedbackStatus.New),
                Acknowledged = counts.GetValueOrDefault(GnollBenchFeedbackStatus.Acknowledged),
                Resolved = counts.GetValueOrDefault(GnollBenchFeedbackStatus.Resolved),
                Regressed = counts.GetValueOrDefault(GnollBenchFeedbackStatus.Regressed),
                Dismissed = counts.GetValueOrDefault(GnollBenchFeedbackStatus.Dismissed)
            });
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Triages an issue: creates or updates its triage row and records who did it and when. An absent note, resolution
    /// or fixed-in version keeps the stored value and an empty one clears it. <c>regressed</c> is refused, because it
    /// is derived. Returns the issue as the list shows it.
    /// </summary>
    [HttpPost("issues/{fingerprint}/status")]
    public async Task<IActionResult> SetStatus(string fingerprint, [FromBody] GnollBenchFeedbackStatusUpdateDto? request)
    {
        if (request == null) return BadRequest(new { message = "The request body is required." });

        var result = await _query.SetTriageAsync(fingerprint, new GnollBenchFeedbackTriageUpdate
        {
            Status = request.Status,
            Note = request.Note,
            Resolution = request.Resolution,
            FixedInVersion = request.FixedInVersion
        }, CurrentUserId, CancellationToken.None);

        switch (result.Outcome)
        {
            case GnollBenchFeedbackTriageOutcome.Invalid: return BadRequest(new { message = result.Message });
            case GnollBenchFeedbackTriageOutcome.NotFound: return NotFound(new { message = result.Message });
        }

        var fingerprints = new[] { result.Fingerprint };
        var issues = await _query.LoadIssuesAsync(fingerprints, 0, GnollBenchAcceptedScope.Accepted, CancellationToken.None);
        if (issues.Count == 0) issues = await _query.LoadIssuesAsync(fingerprints, 0, GnollBenchAcceptedScope.All, CancellationToken.None);
        if (issues.Count == 0) return NotFound(new { message = "No such GnollBench feedback issue." });
        var dtos = await ToIssueDtosAsync(issues, CancellationToken.None);
        return Ok(dtos[0]);
    }

    // --- Helpers ---

    private async Task<List<GnollBenchFeedbackIssueDto>> ToIssueDtosAsync(IReadOnlyList<GnollBenchFeedbackIssue> issues, CancellationToken ct)
    {
        var triagers = await UserNamesAsync(issues.Select(i => i.TriagedByUserId), ct);
        return issues.Select(i => new GnollBenchFeedbackIssueDto
        {
            Fingerprint = i.Fingerprint,
            Category = i.Category,
            Subject = i.Subject,
            Summary = i.Summary,
            FirstSeenUtc = i.FirstSeenUtc,
            LastSeenUtc = i.LastSeenUtc,
            SelfReportCount = i.SelfReportCount,
            RunCount = i.RunCount,
            ModelCount = i.ModelIds.Count,
            Models = Shown(i.ModelLabels),
            Providers = i.Providers.ToList(),
            Suites = Shown(i.Suites),
            Status = GnollBenchFeedbackStatuses.ToWire(i.Status),
            TriageNote = i.TriageNote,
            Resolution = i.Resolution,
            FixedInVersion = i.FixedInVersion,
            TriagedByUserName = i.TriagedByUserId != null && triagers.TryGetValue(i.TriagedByUserId, out var name) ? name : null,
            TriagedUtc = i.TriagedUtc,
            ResolvedUtc = i.ResolvedUtc,
            AlsoInOverseer = i.AlsoInOverseer == null
                ? null
                : new GnollBenchFeedbackOverseerLinkDto { IssueId = i.AlsoInOverseer.IssueId, Status = i.AlsoInOverseer.Status }
        }).ToList();
    }

    private static GnollBenchFeedbackOccurrenceDto ToOccurrenceDto(GnollBenchFeedbackOccurrence o) => new()
    {
        Id = o.Id,
        OccurredUtc = o.OccurredUtc,
        RunId = o.RunId,
        SeriesId = o.SeriesId,
        SuiteId = o.SuiteId,
        SuiteName = o.SuiteName,
        QuestionId = o.QuestionId,
        QuestionText = o.QuestionText,
        ModelId = o.ModelId,
        ModelLabel = o.ModelLabel,
        Provider = o.Provider,
        Condition = o.Condition,
        Variant = o.Variant,
        VariantName = o.VariantName,
        Corpus = o.Corpus,
        Harness = o.Harness,
        Accepted = o.Accepted,
        Error = o.Error,
        Category = o.Category,
        Subject = o.Subject,
        Summary = o.Summary,
        Details = o.Details,
        SearchedFor = o.SearchedFor,
        ExpectedLocation = o.ExpectedLocation,
        RelatedTools = o.RelatedTools.ToList(),
        NoticedVia = o.NoticedVia,
        SuspectedCause = o.SuspectedCause,
        SuspectedCauseNote = o.SuspectedCauseNote,
        Truncated = o.Truncated
    };

    /// <summary>At most <see cref="GnollBenchFeedbackQuery.MaxListed"/> values, then <c>+N</c> for the ones left out.</summary>
    private static List<string> Shown(IReadOnlyList<string> values)
    {
        if (values.Count <= GnollBenchFeedbackQuery.MaxListed) return values.ToList();
        var shown = values.Take(GnollBenchFeedbackQuery.MaxListed).ToList();
        shown.Add($"+{values.Count - GnollBenchFeedbackQuery.MaxListed}");
        return shown;
    }

    /// <summary>User names by id for the ids that still have an account.</summary>
    private async Task<Dictionary<string, string?>> UserNamesAsync(IEnumerable<string?> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string?>();
        return await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
    }
}

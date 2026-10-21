using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Feedback;

namespace Overseer.Controllers;

/// <summary>
/// The admin Feedback sub-tab: the two feedback switches, the issues the chat model reported with their occurrences,
/// per-status counts, triage and deletion. Errors are <c>{ message }</c>: 400 for invalid input, 404 for an unknown
/// issue. Reads answer 499 when the client aborts (<see cref="CancellationExceptionExtensions.IsCanceledBy"/>);
/// actions that change state take no request token, so a dropped connection never stops one part-way.
/// </summary>
[ApiController]
[Route("api/admin/feedback")]
[Authorize(Policy = "AdminOnly")]
public class AdminFeedbackController : ControllerBase
{
    public const int DefaultIssueTake = 25;
    public const int DefaultOccurrenceTake = 10;
    public const int MaxTake = 100;
    public const int MaxModelIdsShown = 5;
    public const int MaxTriageNoteLength = 1000;
    public const int MaxSearchLength = 200;

    public const string SortLastSeen = "lastSeen";
    public const string SortFirstSeen = "firstSeen";
    public const string SortCount = "count";

    private readonly ApplicationDbContext _db;
    private readonly FeedbackSwitchService _switch;

    public AdminFeedbackController(ApplicationDbContext db, FeedbackSwitchService feedbackSwitch)
    {
        _db = db;
        _switch = feedbackSwitch;
    }

    private string? CurrentUserId => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    // --- Switch ---

    [HttpGet("switch")]
    public async Task<IActionResult> GetSwitch(CancellationToken ct = default)
    {
        try
        {
            return Ok(await ToSwitchesDtoAsync(ct));
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Appends a setting row for the <paramref name="target"/> switch (<c>chat</c> or <c>gnollbench</c>), even
    /// when the value is unchanged, and returns both switches.
    /// </summary>
    [HttpPost("switch/{target}")]
    public async Task<IActionResult> SetSwitch(string target, [FromBody] AdminFeedbackSwitchUpdateDto? request)
    {
        if (!TryParseSwitchTarget(target, out var parsed)) return BadRequest(new { message = "Unknown switch." });
        if (request == null) return BadRequest(new { message = "The request body is required." });
        await _switch.SetAsync(parsed, request.Enabled, CurrentUserId, CancellationToken.None);
        return Ok(await ToSwitchesDtoAsync(CancellationToken.None));
    }

    // --- Issues ---

    /// <summary>
    /// A page of issues. An issue is in the date range when its active span overlaps it; <paramref name="endDate"/>
    /// includes its whole day, as in AI Telemetry. <paramref name="status"/> is a comma-separated list of wire values.
    /// </summary>
    [HttpGet("issues")]
    public async Task<IActionResult> ListIssues(
        [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null,
        [FromQuery] string? status = null, [FromQuery] string? category = null, [FromQuery] string? modelId = null,
        [FromQuery] string? search = null, [FromQuery] string? sort = null,
        [FromQuery] int skip = 0, [FromQuery] int take = DefaultIssueTake, CancellationToken ct = default)
    {
        var statuses = new List<OverseerFeedbackStatus>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            foreach (var part in status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!AdminFeedbackStatuses.TryParse(part, out var parsed))
                    return BadRequest(new { message = $"Unknown status '{part}'." });
                if (!statuses.Contains(parsed)) statuses.Add(parsed);
            }
        }

        string? categoryFilter = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (categoryFilter != null && !FeedbackCategories.IsKnown(categoryFilter))
            return BadRequest(new { message = $"Unknown category '{categoryFilter}'." });

        string sortKey;
        if (string.IsNullOrWhiteSpace(sort) || string.Equals(sort, SortLastSeen, StringComparison.OrdinalIgnoreCase)) sortKey = SortLastSeen;
        else if (string.Equals(sort, SortFirstSeen, StringComparison.OrdinalIgnoreCase)) sortKey = SortFirstSeen;
        else if (string.Equals(sort, SortCount, StringComparison.OrdinalIgnoreCase)) sortKey = SortCount;
        else return BadRequest(new { message = $"Unknown sort '{sort}'." });

        string? term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (term != null && term.Length > MaxSearchLength)
            return BadRequest(new { message = $"The search can be at most {MaxSearchLength} characters." });

        string? model = string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim();

        try
        {
            var query = _db.OverseerFeedbackIssues.AsNoTracking();
            if (startDate.HasValue) { var from = startDate.Value; query = query.Where(i => i.LastSeenUtc >= from); }
            if (endDate.HasValue) { var to = endDate.Value.AddDays(1); query = query.Where(i => i.FirstSeenUtc < to); }
            if (statuses.Count > 0) query = query.Where(i => statuses.Contains(i.Status));
            if (categoryFilter != null) query = query.Where(i => i.Category == categoryFilter);
            if (model != null) query = query.Where(i => _db.OverseerFeedbackReports.Any(r => r.IssueId == i.Id && r.ModelId == model));
            if (term != null)
            {
                // Lowered on both sides so the match ignores case on every provider, whatever the collation.
                var lowered = term.ToLowerInvariant();
                query = query.Where(i => i.Subject.ToLower().Contains(lowered) || i.Summary.ToLower().Contains(lowered));
            }

            int total = await query.CountAsync(ct);

            var ordered = sortKey switch
            {
                SortFirstSeen => query.OrderByDescending(i => i.FirstSeenUtc).ThenByDescending(i => i.Id),
                SortCount => query.OrderByDescending(i => i.OccurrenceCount).ThenByDescending(i => i.LastSeenUtc).ThenByDescending(i => i.Id),
                _ => query.OrderByDescending(i => i.LastSeenUtc).ThenByDescending(i => i.Id)
            };

            var issues = await ordered.Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MaxTake)).ToListAsync(ct);
            return Ok(new AdminFeedbackIssuePageDto { TotalCount = total, Rows = await ToIssueDtosAsync(issues, ct) });
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Distinct model ids over all occurrences, for the Model facet.</summary>
    [HttpGet("models")]
    public async Task<IActionResult> ListModels(CancellationToken ct = default)
    {
        try
        {
            var models = await _db.OverseerFeedbackReports.AsNoTracking()
                .Where(r => r.ModelId != null && r.ModelId != "")
                .Select(r => r.ModelId!)
                .Distinct()
                .OrderBy(m => m)
                .ToListAsync(ct);
            return Ok(models);
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>A page of one issue's occurrences, newest first.</summary>
    [HttpGet("issues/{id:long}/occurrences")]
    public async Task<IActionResult> ListOccurrences(long id, [FromQuery] int skip = 0, [FromQuery] int take = DefaultOccurrenceTake,
        CancellationToken ct = default)
    {
        try
        {
            if (!await _db.OverseerFeedbackIssues.AnyAsync(i => i.Id == id, ct))
                return NotFound(new { message = "No such feedback issue." });

            var query = _db.OverseerFeedbackReports.AsNoTracking().Where(r => r.IssueId == id);
            int total = await query.CountAsync(ct);
            var rows = await query
                .OrderByDescending(r => r.CreatedUtc).ThenByDescending(r => r.Id)
                .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MaxTake))
                .ToListAsync(ct);

            var names = await UserNamesAsync(rows.Select(r => r.AspNetUserId), ct);
            return Ok(new AdminFeedbackOccurrencePageDto
            {
                TotalCount = total,
                Rows = rows.Select(r => new AdminFeedbackOccurrenceDto
                {
                    Id = r.Id,
                    CreatedUtc = r.CreatedUtc,
                    Subject = r.Subject,
                    Summary = r.Summary,
                    Details = r.Details,
                    SearchedFor = r.SearchedFor,
                    ExpectedLocation = r.ExpectedLocation,
                    RelatedTools = ParseRelatedTools(r.RelatedToolsJson),
                    NoticedVia = r.NoticedVia,
                    SuspectedCause = r.SuspectedCause,
                    SuspectedCauseNote = r.SuspectedCauseNote,
                    Truncated = r.Truncated,
                    Redacted = r.Redacted,
                    ChatSessionId = r.ChatSessionId,
                    TurnUserMessageId = r.TurnUserMessageId,
                    Provider = r.Provider,
                    ModelId = r.ModelId,
                    AppVersion = r.AppVersion,
                    UserName = r.AspNetUserId != null && names.TryGetValue(r.AspNetUserId, out var name) ? name : null
                }).ToList()
            });
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Issue counts per status over all dates, for the sub-tab label.</summary>
    [HttpGet("counts")]
    public async Task<IActionResult> GetCounts(CancellationToken ct = default)
    {
        try
        {
            var groups = await _db.OverseerFeedbackIssues.AsNoTracking()
                .GroupBy(i => i.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var dto = new AdminFeedbackCountsDto();
            foreach (var g in groups)
            {
                switch (g.Status)
                {
                    case OverseerFeedbackStatus.New: dto.New = g.Count; break;
                    case OverseerFeedbackStatus.Acknowledged: dto.Acknowledged = g.Count; break;
                    case OverseerFeedbackStatus.Resolved: dto.Resolved = g.Count; break;
                    case OverseerFeedbackStatus.Regressed: dto.Regressed = g.Count; break;
                    case OverseerFeedbackStatus.Dismissed: dto.Dismissed = g.Count; break;
                }
            }
            return Ok(dto);
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Triages an issue: sets its status and note and records who did it and when. <c>regressed</c> is refused,
    /// because the system sets it when a resolved issue recurs.
    /// </summary>
    [HttpPost("issues/{id:long}/status")]
    public async Task<IActionResult> SetStatus(long id, [FromBody] AdminFeedbackStatusUpdateDto? request)
    {
        if (request == null) return BadRequest(new { message = "The request body is required." });
        if (!AdminFeedbackStatuses.TryParse(request.Status, out var status))
            return BadRequest(new { message = $"Unknown status '{request.Status}'." });
        if (status == OverseerFeedbackStatus.Regressed)
            return BadRequest(new { message = "Regressed is set by the system when a resolved issue recurs; it cannot be chosen." });

        var issue = await _db.OverseerFeedbackIssues.FirstOrDefaultAsync(i => i.Id == id);
        if (issue == null) return NotFound(new { message = "No such feedback issue." });

        string? note = request.Note?.Trim();
        if (string.IsNullOrEmpty(note)) note = null;
        else if (note.Length > MaxTriageNoteLength) note = note[..MaxTriageNoteLength];

        issue.Status = status;
        issue.TriageNote = note;
        issue.TriagedByUserId = CurrentUserId;
        issue.TriagedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dtos = await ToIssueDtosAsync(new List<OverseerFeedbackIssue> { issue }, CancellationToken.None);
        return Ok(dtos[0]);
    }

    /// <summary>Deletes an issue and its occurrences.</summary>
    [HttpDelete("issues/{id:long}")]
    public async Task<IActionResult> DeleteIssue(long id)
    {
        var issue = await _db.OverseerFeedbackIssues.FirstOrDefaultAsync(i => i.Id == id);
        if (issue == null) return NotFound(new { message = "No such feedback issue." });

        // Removed explicitly as well as by the cascade, so a provider without cascades deletes them too.
        var occurrences = await _db.OverseerFeedbackReports.Where(r => r.IssueId == id).ToListAsync();
        _db.OverseerFeedbackReports.RemoveRange(occurrences);
        _db.OverseerFeedbackIssues.Remove(issue);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // --- Helpers ---

    private static bool TryParseSwitchTarget(string? target, out OverseerFeedbackSwitchTarget parsed)
    {
        if (string.Equals(target, "chat", StringComparison.OrdinalIgnoreCase)) { parsed = OverseerFeedbackSwitchTarget.Chat; return true; }
        if (string.Equals(target, "gnollbench", StringComparison.OrdinalIgnoreCase)) { parsed = OverseerFeedbackSwitchTarget.GnollBench; return true; }
        parsed = default;
        return false;
    }

    private async Task<AdminFeedbackSwitchesDto> ToSwitchesDtoAsync(CancellationToken ct)
    {
        var chat = await _switch.GetStateAsync(OverseerFeedbackSwitchTarget.Chat, ct);
        var gnollBench = await _switch.GetStateAsync(OverseerFeedbackSwitchTarget.GnollBench, ct);
        var names = await UserNamesAsync(new[] { chat.SetByUserId, gnollBench.SetByUserId }, ct);
        return new AdminFeedbackSwitchesDto
        {
            Chat = ToSwitchDto(chat, names),
            GnollBench = ToSwitchDto(gnollBench, names)
        };
    }

    private static AdminFeedbackSwitchDto ToSwitchDto(FeedbackSwitchState state, Dictionary<string, string?> names)
        => new()
        {
            Enabled = state.Enabled,
            SetAtUtc = state.SetAtUtc,
            SetByUserName = state.SetByUserId != null && names.TryGetValue(state.SetByUserId, out var name) ? name : null,
            Seeded = state.Seeded
        };

    /// <summary>
    /// Builds the rows of <paramref name="issues"/>, in their order, with the aggregates over their retained
    /// occurrences read by a few set-based queries over the page's issue ids.
    /// </summary>
    private async Task<List<AdminFeedbackIssueDto>> ToIssueDtosAsync(List<OverseerFeedbackIssue> issues, CancellationToken ct)
    {
        if (issues.Count == 0) return new List<AdminFeedbackIssueDto>();
        var ids = issues.Select(i => i.Id).ToList();
        var reports = _db.OverseerFeedbackReports.AsNoTracking().Where(r => ids.Contains(r.IssueId));

        var retained = await reports
            .GroupBy(r => r.IssueId)
            .Select(g => new { IssueId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.IssueId, x => x.Count, ct);

        var userPairs = await reports
            .Where(r => r.AspNetUserId != null)
            .Select(r => new { r.IssueId, r.AspNetUserId })
            .Distinct()
            .ToListAsync(ct);
        var userCounts = userPairs.GroupBy(p => p.IssueId).ToDictionary(g => g.Key, g => g.Count());

        var modelPairs = await reports
            .Where(r => r.ModelId != null && r.ModelId != "")
            .Select(r => new { r.IssueId, r.ModelId })
            .Distinct()
            .ToListAsync(ct);
        var modelsByIssue = modelPairs.GroupBy(p => p.IssueId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ModelId!).OrderBy(m => m, StringComparer.Ordinal).ToList());

        // The newest occurrence is the one with the highest id: rows are only ever appended.
        var latestIds = await reports
            .GroupBy(r => r.IssueId)
            .Select(g => g.Max(r => r.Id))
            .ToListAsync(ct);
        var latest = await _db.OverseerFeedbackReports.AsNoTracking()
            .Where(r => latestIds.Contains(r.Id))
            .Select(r => new { r.IssueId, r.ExpectedLocation, r.RelatedToolsJson, r.SuspectedCause, r.AppVersion })
            .ToDictionaryAsync(r => r.IssueId, ct);

        var triagers = await UserNamesAsync(issues.Select(i => i.TriagedByUserId), ct);

        var rows = new List<AdminFeedbackIssueDto>(issues.Count);
        foreach (var issue in issues)
        {
            latest.TryGetValue(issue.Id, out var newest);
            rows.Add(new AdminFeedbackIssueDto
            {
                Id = issue.Id,
                Category = issue.Category,
                Subject = issue.Subject,
                Summary = issue.Summary,
                FirstSeenUtc = issue.FirstSeenUtc,
                LastSeenUtc = issue.LastSeenUtc,
                OccurrenceCount = issue.OccurrenceCount,
                RetainedCount = retained.GetValueOrDefault(issue.Id),
                DistinctUserCount = userCounts.GetValueOrDefault(issue.Id),
                ModelIds = ShownModelIds(modelsByIssue.GetValueOrDefault(issue.Id)),
                RegressionCount = issue.RegressionCount,
                LastRegressionUtc = issue.LastRegressionUtc,
                LatestExpectedLocation = newest?.ExpectedLocation,
                LatestRelatedTools = ParseRelatedTools(newest?.RelatedToolsJson),
                LatestSuspectedCause = newest?.SuspectedCause,
                LatestAppVersion = newest?.AppVersion,
                Status = AdminFeedbackStatuses.ToWire(issue.Status),
                TriageNote = issue.TriageNote,
                TriagedByUserName = issue.TriagedByUserId != null && triagers.TryGetValue(issue.TriagedByUserId, out var name) ? name : null,
                TriagedUtc = issue.TriagedUtc
            });
        }
        return rows;
    }

    /// <summary>At most <see cref="MaxModelIdsShown"/> model ids, then <c>+N</c> for the ones left out.</summary>
    private static List<string> ShownModelIds(List<string>? models)
    {
        if (models == null || models.Count == 0) return new List<string>();
        if (models.Count <= MaxModelIdsShown) return models;
        var shown = models.Take(MaxModelIdsShown).ToList();
        shown.Add($"+{models.Count - MaxModelIdsShown}");
        return shown;
    }

    /// <summary>The stored JSON array of tool names; empty when absent or malformed.</summary>
    private static List<string> ParseRelatedTools(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            var tools = JsonSerializer.Deserialize<List<string?>>(json);
            return tools?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).ToList() ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
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

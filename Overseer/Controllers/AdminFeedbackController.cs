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
/// The admin Feedback sub-tab: the chat feedback switch, the issues the chat model reported with their occurrences,
/// facets, per-status counts, triage and deletion. The filters, sorts and row building are
/// <see cref="OverseerFeedbackQuery"/>'s. Errors are <c>{ message }</c>: 400 for invalid input, 404 for an unknown
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
    public const int MaxIssueIdsReturned = 1000;
    public const int MaxModelIdsShown = OverseerFeedbackQuery.MaxModelIdsShown;
    public const int MaxTriageNoteLength = FeedbackTriageFields.MaxNoteLength;
    public const int MaxResolutionLength = FeedbackTriageFields.MaxResolutionLength;
    public const int MaxFixedInVersionLength = FeedbackTriageFields.MaxFixedInVersionLength;
    public const int MaxSearchLength = OverseerFeedbackFilter.MaxSearchLength;

    public const string SortLastSeen = OverseerFeedbackSorts.LastSeen;
    public const string SortFirstSeen = OverseerFeedbackSorts.FirstSeen;
    public const string SortCount = OverseerFeedbackSorts.Count;
    public const string SortUsers = OverseerFeedbackSorts.Users;
    public const string SortRegressions = OverseerFeedbackSorts.Regressions;
    public const string SortTriaged = OverseerFeedbackSorts.Triaged;
    public const string SortSubject = OverseerFeedbackSorts.Subject;

    private readonly ApplicationDbContext _db;
    private readonly FeedbackSwitchService _switch;
    private readonly OverseerFeedbackQuery _query;

    public AdminFeedbackController(ApplicationDbContext db, FeedbackSwitchService feedbackSwitch, OverseerFeedbackQuery query)
    {
        _db = db;
        _switch = feedbackSwitch;
        _query = query;
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
    /// Appends a setting row for the <paramref name="target"/> switch, <c>chat</c> (the only one), even when
    /// the value is unchanged, and returns it.
    /// </summary>
    [HttpPost("switch/{target}")]
    public async Task<IActionResult> SetSwitch(string target, [FromBody] AdminFeedbackSwitchUpdateDto? request)
    {
        if (!IsChatSwitch(target)) return BadRequest(new { message = "Unknown switch." });
        if (request == null) return BadRequest(new { message = "The request body is required." });
        await _switch.SetAsync(request.Enabled, CurrentUserId, CancellationToken.None);
        return Ok(await ToSwitchesDtoAsync(CancellationToken.None));
    }

    // --- Issues ---

    /// <summary>
    /// A page of issues. An issue is in the date range when its active span overlaps it; <c>endDate</c> includes its
    /// whole day, as in AI Telemetry. The filters are <see cref="AdminFeedbackFilterQuery"/>'s comma lists.
    /// </summary>
    [HttpGet("issues")]
    public async Task<IActionResult> ListIssues([FromQuery] AdminFeedbackFilterQuery? filter = null, [FromQuery] string? sort = null,
        [FromQuery] int skip = 0, [FromQuery] int take = DefaultIssueTake, CancellationToken ct = default)
    {
        if (!OverseerFeedbackFilter.TryParse(filter, out var parsed, out var error)) return BadRequest(new { message = error });
        if (!OverseerFeedbackSorts.TryParse(sort, out var sortKey)) return BadRequest(new { message = $"Unknown sort '{sort}'." });

        try
        {
            var (total, issues) = await _query.PageAsync(parsed, sortKey, skip, Math.Clamp(take, 1, MaxTake), ct);
            return Ok(new AdminFeedbackIssuePageDto { TotalCount = total, Rows = await _query.BuildIssueDtosAsync(issues, ct) });
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// The ids of every issue the same filters match, in the list's order, at most <see cref="MaxIssueIdsReturned"/>,
    /// with the total and whether the cap cut the list.
    /// </summary>
    [HttpGet("issues/ids")]
    public async Task<IActionResult> ListIssueIds([FromQuery] AdminFeedbackFilterQuery? filter = null, [FromQuery] string? sort = null,
        CancellationToken ct = default)
    {
        if (!OverseerFeedbackFilter.TryParse(filter, out var parsed, out var error)) return BadRequest(new { message = error });
        if (!OverseerFeedbackSorts.TryParse(sort, out var sortKey)) return BadRequest(new { message = $"Unknown sort '{sort}'." });

        try
        {
            return Ok(await _query.IdsAsync(parsed, sortKey, MaxIssueIdsReturned, ct));
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>The facet options present among the issues in the date range, each with a count of issues.</summary>
    [HttpGet("facets")]
    public async Task<IActionResult> GetFacets([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _query.FacetsAsync(startDate, endDate, ct));
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// A page of one issue's occurrences, newest first, narrowed by the filter's occurrence-level filters unless
    /// <paramref name="all"/> is set. Its issue-level filters are ignored.
    /// </summary>
    [HttpGet("issues/{id:long}/occurrences")]
    public async Task<IActionResult> ListOccurrences(long id, [FromQuery] AdminFeedbackFilterQuery? filter = null,
        [FromQuery] bool all = false, [FromQuery] int skip = 0, [FromQuery] int take = DefaultOccurrenceTake,
        CancellationToken ct = default)
    {
        var parsed = OverseerFeedbackFilter.None;
        if (!all && !OverseerFeedbackFilter.TryParse(filter, out parsed, out var error)) return BadRequest(new { message = error });

        try
        {
            if (!await _db.OverseerFeedbackIssues.AnyAsync(i => i.Id == id, ct))
                return NotFound(new { message = "No such feedback issue." });

            var query = _query.Occurrences(id, parsed);
            int total = await query.CountAsync(ct);
            var rows = await query.Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MaxTake)).ToListAsync(ct);

            return Ok(new AdminFeedbackOccurrencePageDto
            {
                TotalCount = total,
                Rows = await _query.BuildOccurrenceDtosAsync(rows, ct)
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
    /// Triages an issue: sets its status, and its note, resolution and fixed-in version, and records who did it and
    /// when. For each text field an absent value keeps the stored one and an empty one clears it; an over-long value
    /// or a malformed version is 400. An absent <c>resolvedVia</c> keeps the stored list, an empty one clears it, and an
    /// unknown id is 400. <c>regressed</c> is refused, because the system sets it when a resolved issue
    /// recurs.
    /// </summary>
    [HttpPost("issues/{id:long}/status")]
    public async Task<IActionResult> SetStatus(long id, [FromBody] AdminFeedbackStatusUpdateDto? request)
    {
        if (request == null) return BadRequest(new { message = "The request body is required." });
        if (!AdminFeedbackStatuses.TryParse(request.Status, out var status))
            return BadRequest(new { message = $"Unknown status '{request.Status}'." });
        if (status == OverseerFeedbackStatus.Regressed)
            return BadRequest(new { message = "Regressed is set by the system when a resolved issue recurs; it cannot be chosen." });

        if (!FeedbackTriageFields.TryRead(request.Note, MaxTriageNoteLength, "note", out bool hasNote, out string? note, out var error)
            || !FeedbackTriageFields.TryRead(request.Resolution, MaxResolutionLength, "resolution", out bool hasResolution, out string? resolution, out error)
            || !FeedbackTriageFields.TryReadFixedInVersion(request.FixedInVersion, out bool hasVersion, out string? fixedInVersion, out error))
        {
            return BadRequest(new { message = error });
        }
        IReadOnlyList<string>? resolvedVia = null;
        if (request.ResolvedVia != null)
        {
            if (!FeedbackResolvedVia.TryParse(request.ResolvedVia, out var parsedVia, out error)) return BadRequest(new { message = error });
            resolvedVia = parsedVia;
        }

        var issue = await _db.OverseerFeedbackIssues.FirstOrDefaultAsync(i => i.Id == id);
        if (issue == null) return NotFound(new { message = "No such feedback issue." });

        issue.Status = status;
        if (hasNote) issue.TriageNote = note;
        if (hasResolution) issue.Resolution = resolution;
        if (hasVersion) issue.FixedInVersion = fixedInVersion;
        if (resolvedVia != null) issue.ResolvedVia = FeedbackResolvedVia.ToStored(resolvedVia);
        issue.TriagedByUserId = CurrentUserId;
        issue.TriagedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dtos = await _query.BuildIssueDtosAsync(new List<OverseerFeedbackIssue> { issue }, CancellationToken.None);
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

    private static bool IsChatSwitch(string? target)
        => string.Equals(target, "chat", StringComparison.OrdinalIgnoreCase);

    private async Task<AdminFeedbackSwitchesDto> ToSwitchesDtoAsync(CancellationToken ct)
    {
        var chat = await _switch.GetStateAsync(ct);
        var names = await _query.UserNamesAsync(new[] { chat.SetByUserId }, ct);
        return new AdminFeedbackSwitchesDto
        {
            Chat = ToSwitchDto(chat, names)
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
}

using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Overseer.Models;
using Overseer.Services.Security;

namespace Overseer.Controllers;

/// <summary>
/// The admin Users tab: the list, facet counts, per-user actions, the Bulk Actions wizard and the admin log.
/// Errors are <c>{ message, code }</c>: 400 for invalid input, 404, 409 for a refusal whose code names it, 403 for
/// another administrator's bulk preview, 500 for a failed deletion. Reads answer 499 when the client aborts, whatever
/// the exception: SqlClient reports a query canceled mid-execution as a <c>SqlException</c>, not an
/// <see cref="OperationCanceledException"/>. Actions that change an account take no request token, so a dropped
/// connection never stops one part-way.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "AdminOnly")]
public class AdminUsersController : ControllerBase
{
    private readonly AdminUserManagementService _users;
    private readonly AdminBulkUserOperationService _bulk;

    public AdminUsersController(AdminUserManagementService users, AdminBulkUserOperationService bulk)
    {
        _users = users;
        _bulk = bulk;
    }

    private AdminActor Actor => new(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty, User.Identity?.Name ?? string.Empty);

    private IActionResult ToResult<T>(AdminResult<T> result) => result.Status switch
    {
        AdminResultStatus.Ok => Ok(result.Value),
        AdminResultStatus.NotFound => NotFound(new { message = result.Message, code = result.Code }),
        AdminResultStatus.Conflict => Conflict(new { message = result.Message, code = result.Code }),
        AdminResultStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Message, code = result.Code }),
        AdminResultStatus.Failed => StatusCode(StatusCodes.Status500InternalServerError, new { message = result.Message, code = result.Code }),
        _ => BadRequest(new { message = result.Message, code = result.Code })
    };

    private BadRequestObjectResult CriteriaError(string message) => BadRequest(new { message, code = "criteria" });

    // --- List ---

    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] AdminUserListQuery query, CancellationToken ct)
    {
        try
        {
            if (query.Validate() is string error) return CriteriaError(error);
            return Ok(await _users.ListAsync(Actor, query, query.Sort, query.Skip, query.Take, ct));
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpGet("facets")]
    public async Task<IActionResult> Facets([FromQuery] string? search, CancellationToken ct)
    {
        try
        {
            if (search != null && search.Length > AdminUserCriteria.MaxSearchLength)
                return CriteriaError($"The search can be at most {AdminUserCriteria.MaxSearchLength} characters.");
            return Ok(await _users.FacetCountsAsync(search, ct));
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpGet("log")]
    public async Task<IActionResult> Log([FromQuery] string? search, [FromQuery] string? kinds, [FromQuery] string? actor,
        [FromQuery] string? subjectUserId, [FromQuery] long? bulkOperationId, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
    {
        try
        {
            return Ok(await _users.ListLogAsync(search, kinds, actor, subjectUserId, bulkOperationId, skip, take, ct));
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    // --- Bulk ---

    [HttpPost("bulk/preview")]
    public async Task<IActionResult> BulkPreview([FromBody] AdminBulkPreviewRequest request, CancellationToken ct)
    {
        try
        {
            return ToResult(await _bulk.PreviewAsync(Actor, request ?? new AdminBulkPreviewRequest(), ct));
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpGet("bulk")]
    public async Task<IActionResult> BulkHistory([FromQuery] int skip, [FromQuery] int take, [FromQuery] bool includePreviews, CancellationToken ct)
    {
        try
        {
            return Ok(await _bulk.HistoryAsync(skip, take, includePreviews, ct));
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <param name="outcome">pending, changed, skipped, failed or excluded; empty for every item.</param>
    [HttpGet("bulk/{opId:long}")]
    public async Task<IActionResult> BulkGet(long opId, [FromQuery] string? outcome, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
    {
        try
        {
            return ToResult(await _bulk.GetAsync(opId, outcome, skip, take, ct));
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpPost("bulk/{opId:long}/execute")]
    public async Task<IActionResult> BulkExecute(long opId, [FromBody] AdminBulkExecuteRequest request)
        => ToResult(await _bulk.ExecuteAsync(Actor, opId, request ?? new AdminBulkExecuteRequest()));

    [HttpPost("bulk/{opId:long}/cancel")]
    public async Task<IActionResult> BulkCancel(long opId)
        => ToResult(await _bulk.CancelAsync(opId));

    [HttpGet("bulk/{opId:long}/report.csv")]
    public async Task<IActionResult> BulkReport(long opId, CancellationToken ct)
    {
        try
        {
            var result = await _bulk.ReportCsvAsync(opId, ct);
            if (!result.IsOk) return ToResult(result);
            return File(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(result.Value!)).ToArray(),
                "text/csv; charset=utf-8", $"bulk-operation-{opId}.csv");
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    // --- One user ---

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        try
        {
            return ToResult(await _users.GetAsync(Actor, id, ct));
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpPost("{id}/lockout")]
    public async Task<IActionResult> LockOut(string id, [FromBody] AdminLockoutRequest request)
        => ToResult(await _users.LockOutAsync(Actor, id, request?.Until, request?.Reason));

    [HttpDelete("{id}/lockout")]
    public async Task<IActionResult> Unlock(string id) => ToResult(await _users.UnlockAsync(Actor, id));

    [HttpPost("{id}/ban")]
    public async Task<IActionResult> Ban(string id, [FromBody] AdminReasonRequest request)
        => ToResult(await _users.BanAsync(Actor, id, request?.Reason));

    [HttpDelete("{id}/ban")]
    public async Task<IActionResult> Unban(string id) => ToResult(await _users.UnbanAsync(Actor, id));

    [HttpPut("{id}/game-restrictions")]
    public async Task<IActionResult> SetGameRestrictions(string id, [FromBody] AdminGameRestrictionsRequest request)
        => ToResult(await _users.SetGameRestrictionsAsync(Actor, id, request?.GameLogBanned ?? false, request?.BonesBanned ?? false, request?.Reason));

    [HttpPost("{id}/sign-out-everywhere")]
    public async Task<IActionResult> SignOutEverywhere(string id)
        => ToResult(await _users.SignOutEverywhereAsync(Actor, id));

    [HttpPost("{id}/confirm-email")]
    public async Task<IActionResult> ConfirmEmail(string id) => ToResult(await _users.ConfirmEmailAsync(Actor, id));

    [HttpPost("{id}/reset-two-factor")]
    public async Task<IActionResult> ResetTwoFactor(string id, [FromBody] AdminOptionalReasonRequest? request)
        => ToResult(await _users.ResetTwoFactorAsync(Actor, id, request?.Reason));

    /// <summary>Blocks the user from starting AI work in Overseer; the reason (1–256 characters) is shown to them.</summary>
    [HttpPost("{id}/overseer-block")]
    public async Task<IActionResult> BlockUser(string id, [FromBody] AdminReasonRequest request)
        => ToResult(await _users.BlockFromAiChatAsync(Actor, id, request?.Reason));

    [HttpDelete("{id}/overseer-block")]
    public async Task<IActionResult> UnblockUser(string id) => ToResult(await _users.UnblockFromAiChatAsync(Actor, id));

    /// <summary>Holds any deletion of the account for review; the reason (1–512 characters) is never shown to the user.</summary>
    [HttpPost("{id}/account-hold")]
    public async Task<IActionResult> PlaceAccountHold(string id, [FromBody] AdminReasonRequest request)
        => ToResult(await _users.PlaceHoldAsync(Actor, id, request?.Reason));

    /// <summary>Releases the hold; a pending deletion request still needs an explicit decision.</summary>
    [HttpDelete("{id}/account-hold")]
    public async Task<IActionResult> ReleaseAccountHold(string id) => ToResult(await _users.ReleaseHoldAsync(Actor, id));

    [HttpGet("{id}/deletion-impact")]
    public async Task<IActionResult> DeletionImpact(string id, CancellationToken ct)
    {
        try
        {
            return ToResult(await _users.GetDeletionImpactAsync(Actor, id, ct));
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Deletes the account permanently. POST, because it carries the typed name and the acknowledgments.</summary>
    [HttpPost("{id}/delete")]
    public async Task<IActionResult> Delete(string id, [FromBody] AdminDeleteUserRequest request)
        => ToResult(await _users.DeleteAsync(Actor, id, request?.ConfirmUserName, request?.Reason, request?.AcknowledgedCategories));
}

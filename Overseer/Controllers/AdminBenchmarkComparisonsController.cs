namespace Overseer.Controllers;

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;

/// <summary>
/// Numbered model comparisons: identify the comparison of a selection (found or created by its entry
/// set), rename it, and list every comparison with its documents. Its only dependency is
/// <see cref="BenchmarkComparisonIdentityService"/>, which makes no model call.
/// </summary>
[Route("api/admin/benchmark")]
[Authorize(Policy = "AdminOnly")]
[ApiController]
public class AdminBenchmarkComparisonsController : ControllerBase
{
    private readonly BenchmarkComparisonIdentityService _identity;

    public AdminBenchmarkComparisonsController(BenchmarkComparisonIdentityService identity)
    {
        _identity = identity;
    }

    /// <summary>
    /// The comparison of the selection, created and named when it is new; the same selection in any
    /// order is always the same comparison. 400 for no body, and for a selection the comparison
    /// refuses: empty, battery results mixed with runs or groups, or an entry that does not exist.
    /// </summary>
    [HttpPost("model-comparisons/identify")]
    public async Task<IActionResult> Identify([FromBody] BenchmarkComparisonIdentifyRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });

        string? userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var (comparison, error) = await _identity.EnsureAsync(
            request.RunIds, request.GroupIds, request.BatteryRunIds, string.IsNullOrEmpty(userId) ? null : userId, ct);
        if (comparison == null) return BadRequest(new { error = error ?? "The comparison could not be identified." });

        return Ok(ToDto(comparison));
    }

    /// <summary>
    /// Sets the comparison's name, trimmed; an empty or null name resets it to its default name. 404
    /// for an unknown comparison, 400 for no body or a name past
    /// <see cref="BenchmarkComparisonIdentityService.MaxNameLength"/> characters.
    /// </summary>
    [HttpPatch("model-comparisons/{id:int}")]
    public async Task<IActionResult> Rename(int id, [FromBody] BenchmarkComparisonRenameRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });

        var (comparison, error) = await _identity.RenameAsync(id, request.Name, ct);
        if (error == BenchmarkComparisonIdentityService.NotFoundError) return NotFound(new { error });
        if (comparison == null) return BadRequest(new { error });

        return Ok(ToDto(comparison));
    }

    /// <summary>Every comparison, newest first, with how many report documents it has and when the last was written.</summary>
    [HttpGet("model-comparisons")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await _identity.ListAsync(ct);
        return Ok(list.Select(c => new BenchmarkComparisonListItemDto
        {
            Id = c.Id,
            Name = c.DisplayName,
            CustomName = c.Name,
            DefaultName = c.DefaultName,
            EntryCount = c.EntryCount,
            SubjectKind = c.SubjectKind.ToString(),
            DocumentCount = c.DocumentCount,
            LastDocumentAtUtc = c.LastDocumentAtUtc,
            CreatedAtUtc = c.CreatedAtUtc
        }).ToList());
    }

    public static BenchmarkComparisonDto ToDto(BenchmarkComparison comparison) => new()
    {
        Id = comparison.Id,
        Name = comparison.DisplayName,
        CustomName = comparison.Name,
        DefaultName = comparison.DefaultName,
        EntryCount = comparison.EntryCount,
        SubjectKind = comparison.SubjectKind.ToString(),
        EntryKeys = EntryKeys(comparison.EntryKeysJson),
        CreatedAtUtc = comparison.CreatedAtUtc,
        RenamedAtUtc = comparison.RenamedAtUtc
    };

    /// <summary>The stored entry keys; an empty list when the stored JSON is missing or unreadable.</summary>
    private static List<string> EntryKeys(string? entryKeysJson)
    {
        if (string.IsNullOrWhiteSpace(entryKeysJson)) return new List<string>();
        try
        {
            return BenchmarkReportJson.Deserialize<List<string>>(entryKeysJson);
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}

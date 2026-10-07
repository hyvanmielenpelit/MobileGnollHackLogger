namespace Overseer.Controllers;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Overseer.Models;
using Overseer.Services;

/// <summary>
/// The per-provider default API keys that system AI configurations can use instead of their own.
/// A saved key is checked with its provider first, following the key-save contract of
/// <see cref="ApiKeyRefusalDto"/>; responses never carry the key.
/// </summary>
[Route("api/admin/default-api-keys")]
[Authorize(Policy = "AdminOnly")]
[ApiController]
public class AdminDefaultApiKeysController : ControllerBase
{
    private readonly SystemDefaultApiKeyService _service;

    public AdminDefaultApiKeysController(SystemDefaultApiKeyService service)
    {
        _service = service;
    }

    [HttpGet("")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        try
        {
            return Ok(await _service.GetStatusAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpPut("{provider}")]
    public async Task<IActionResult> Save(string provider, [FromBody] SaveDefaultApiKeyRequest request, CancellationToken ct)
    {
        try
        {
            var outcome = await _service.SaveAsync(provider, request?.ApiKey, request?.SaveUnverified ?? false, ct);
            return outcome.Kind switch
            {
                DefaultApiKeySaveKind.Saved => Ok(outcome.Result),
                DefaultApiKeySaveKind.Invalid => BadRequest(ApiKeyRefusalDto.From(outcome.Validation!)),
                DefaultApiKeySaveKind.Unverifiable => Conflict(ApiKeyRefusalDto.From(outcome.Validation!)),
                _ => BadRequest(new { message = outcome.Error })
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpPost("{provider}/verify")]
    public async Task<IActionResult> VerifyAgain(string provider, CancellationToken ct)
    {
        if (!SystemDefaultApiKeyService.TryCanonicalizeProvider(provider, out var canonical))
        {
            return BadRequest(new { message = SystemDefaultApiKeyService.UnsupportedProviderMessage(provider) });
        }

        try
        {
            var status = await _service.VerifyAgainAsync(canonical, ct);
            if (status == null)
            {
                return NotFound(new { message = $"There is no default {canonical} key." });
            }
            return Ok(new DefaultApiKeyVerifyResultDto { Status = status });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpGet("{provider}/deletion-check")]
    public async Task<IActionResult> GetDeletionCheck(string provider, CancellationToken ct)
    {
        try
        {
            var check = await _service.GetDeletionCheckAsync(provider, ct);
            if (check == null)
            {
                return BadRequest(new { message = SystemDefaultApiKeyService.UnsupportedProviderMessage(provider) });
            }
            return Ok(check);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpDelete("{provider}")]
    public async Task<IActionResult> Delete(string provider, CancellationToken ct)
    {
        if (!SystemDefaultApiKeyService.TryCanonicalizeProvider(provider, out var canonical))
        {
            return BadRequest(new { message = SystemDefaultApiKeyService.UnsupportedProviderMessage(provider) });
        }

        try
        {
            var disabledCount = await _service.DeleteAsync(canonical, ct);
            if (disabledCount == null)
            {
                return NotFound(new { message = $"There is no default {canonical} key." });
            }
            return Ok(new DefaultApiKeyDeleteResultDto { DisabledCount = disabledCount.Value });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }
}

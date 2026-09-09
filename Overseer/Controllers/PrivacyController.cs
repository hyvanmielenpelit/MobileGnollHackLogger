using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GnollHackServer.Data.Privacy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MobileGnollHackLogger.Data;
using Overseer.Extensions;
using Overseer.Security;
using System.Security.Claims;

namespace Overseer.Controllers;

/// <summary>
/// The data-subject endpoints: what Overseer holds about you, in a form you can take away.
/// </summary>
/// <remarks>
/// <para>
/// The Razor application already offers a personal-data download, and it now includes
/// conversations too — but it cannot decrypt a confidential one, because the content keyring
/// lives in Overseer's User Secrets under a different <c>UserSecretsId</c>. This endpoint is the
/// complete export: same builder, same shape, with the key.
/// </para>
/// <para>
/// Not folded into <c>ChatController</c>, deliberately. An export is a bulk read of everything a
/// user has ever written, which is a different kind of operation from opening one chat: it earns
/// its own route so it is visible in a log and in a permission review, and its own rate limit so
/// it cannot be used to drain an account quickly.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PrivacyController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly Overseer.Services.Privacy.ContentProtectionService _contentProtection;
    private readonly ILogger<PrivacyController>? _logger;

    public PrivacyController(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        Overseer.Services.Privacy.ContentProtectionService contentProtection,
        ILogger<PrivacyController>? logger = null)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _contentProtection = contentProtection;
        _logger = logger;
    }

    /// <summary>
    /// Every conversation belonging to the caller, with confidential content decrypted.
    /// </summary>
    /// <remarks>
    /// Rate-limited under the attachment policy rather than a policy of its own: both are
    /// infrequent, expensive reads of stored content, and one more named policy would be a knob
    /// with no distinct reason to be set differently.
    /// </remarks>
    [HttpGet("export")]
    [EnableRateLimiting(RateLimitPolicies.Attachment)]
    public async Task<IActionResult> ExportConversations(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        /* The decryptor closes over the scoped ContentProtectionService, so each value decrypts
           under its own session's DEK. A value that is not an envelope passes through unchanged,
           which is what makes an upgraded conversation -- legitimately part plaintext, part
           ciphertext -- export correctly rather than half-garbled. */
        var conversations = await ChatDataExport.BuildAsync(
            _dbContext,
            userId,
            decrypt: (session, stored) => _contentProtection.Decrypt(session, stored),
            cancellationToken: cancellationToken);

        bool written = await ChatAccessAudit.RecordAsync(
            _dbContext,
            ChatAccessAction.Export,
            actorUserId: userId,
            actorUserName: User.Identity?.Name,
            actorWasAdmin: _configuration.IsAdmin(User.Identity?.Name),
            subjectUserId: userId,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            detail: $"{conversations.Count} conversations, decrypted",
            cancellationToken: cancellationToken);

        if (!written)
        {
            _logger?.LogWarning("Could not record an Export audit entry for user {UserId}.", userId);
        }

        var payload = new Dictionary<string, object?>
        {
            ["conversationsNote"] = ChatDataExport.DescribeExport(canDecrypt: true),
            ["conversations"] = conversations
        };

        Response.Headers["Content-Disposition"] = "attachment; filename=OverseerConversations.json";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return new FileContentResult(
            JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions { WriteIndented = true }),
            "application/json");
    }
}

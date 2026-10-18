using System.Security.Claims;
using GnollHackServer.Data.Legal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Security;
using Overseer.Services;
using Overseer.Services.Legal;

namespace Overseer.Controllers;

/// <summary>
/// The Terms of Service documents this host serves, their required versions and their acceptance, and
/// the privacy notice's operator statements.
/// </summary>
[ApiController]
[Route("api/legal")]
public class LegalController : ControllerBase
{
    private readonly LegalDocumentReader _reader;
    private readonly TermsAcceptanceStore _terms;
    private readonly IConfiguration _configuration;

    public LegalController(LegalDocumentReader reader, TermsAcceptanceStore terms, IConfiguration configuration)
    {
        _reader = reader;
        _terms = terms;
        _configuration = configuration;
    }

    public sealed class AcceptTermsRequest
    {
        public string? Version { get; set; }
        public string? ContentSha256 { get; set; }
        public long? SessionId { get; set; }
    }

    /// <summary>
    /// The document as served by this host. When its file cannot be read, <c>markdown</c> is null and the
    /// version is the newest one anyone has accepted.
    /// </summary>
    [HttpGet("terms/{key}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTerms(string key, CancellationToken ct)
    {
        try
        {
            return await GetTermsCoreAsync(key, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    private async Task<IActionResult> GetTermsCoreAsync(string key, CancellationToken ct)
    {
        if (!_reader.IsConfigured(key))
        {
            return NotFound();
        }

        var document = await _reader.GetAsync(key);
        string? version = document?.Version ?? await _terms.GetRequiredVersionAsync(key, ct);
        string? versionLabel = document?.Version != null && document.VersionOrigin == "document"
            ? document.VersionLabel
            : LegalVersion.FormatLabel(version);

        return Ok(new
        {
            key,
            title = document?.Title ?? _reader.GetTitle(key),
            version,
            versionLabel,
            markdown = document?.Markdown,
            contentSha256 = document?.ContentSha256,
            wikiUrl = document?.WikiUrl ?? _reader.GetWikiUrl(key),
            privacyPolicyUrl = document?.PrivacyPolicyUrl ?? _reader.GetPrivacyPolicyUrl(key)
        });
    }

    /// <summary>The required version, or null when the gate is off. The GnollHack app asks this before it opens Overseer.</summary>
    [HttpGet("terms/{key}/version")]
    [AllowAnonymous]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetTermsVersion(string key, CancellationToken ct)
    {
        if (!_reader.IsConfigured(key))
        {
            return NotFound();
        }

        try
        {
            return Ok(new { version = await _terms.GetRequiredVersionAsync(key, ct) });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Records the signed-in user's acceptance, then starts the session's held GnollHack greeting if there
    /// is one for this user.
    /// </summary>
    [HttpPost("terms/{key}/accept")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> AcceptTerms(
        string key,
        [FromBody] AcceptTermsRequest request,
        [FromServices] PendingGreetingStore? pendingGreetings = null,
        [FromServices] HandoffGreetingStarter? greetingStarter = null,
        [FromServices] UserManager<ApplicationUser>? userManager = null,
        [FromServices] ApplicationDbContext? dbContext = null,
        CancellationToken ct = default)
    {
        try
        {
            return await AcceptTermsCoreAsync(key, request, pendingGreetings, greetingStarter, userManager, dbContext, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    private async Task<IActionResult> AcceptTermsCoreAsync(string key, AcceptTermsRequest request, PendingGreetingStore? pendingGreetings,
        HandoffGreetingStarter? greetingStarter, UserManager<ApplicationUser>? userManager, ApplicationDbContext? dbContext, CancellationToken ct)
    {
        if (!_reader.IsConfigured(key))
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var result = await _terms.RecordAsync(userId, key, request.Version ?? string.Empty, LegalDocumentKeys.SourceWeb,
            request.ContentSha256, clientVersion: null, ct);
        if (result == TermsRecordResult.StaleVersion)
        {
            return Conflict(new { error = "terms_version_changed", requiredVersion = await _terms.GetRequiredVersionAsync(key, ct) });
        }

        bool greetingStarted = false;
        if (key == LegalDocumentKeys.OverseerTerms && request.SessionId is long sessionId && pendingGreetings != null && greetingStarter != null)
        {
            string? prompt = pendingGreetings.TryTake(sessionId, userId);
            if (prompt != null)
            {
                var user = userManager == null ? null : await userManager.FindByIdAsync(userId);
                bool ownsSession = dbContext == null
                    || await dbContext.ChatSession.AnyAsync(s => s.Id == sessionId && s.AspNetUserId == userId, ct);
                if (user != null && ownsSession)
                {
                    greetingStarted = await greetingStarter.TryStartAsync(user,
                        Overseer.Services.Privacy.SessionRef.Persistent(sessionId), prompt,
                        HttpContext?.Connection.RemoteIpAddress?.ToString());
                }
            }
        }

        return Ok(new { accepted = true, greetingStarted });
    }

    public sealed record OperatorStatement(string Text, string? Source, string? SourceUrl);

    /// <summary>
    /// The configured <c>PrivacyNotice:OperatorStatements</c>, keyed as the privacy notice expects. Empty when
    /// nothing is configured; an entry without text is left out.
    /// </summary>
    [HttpGet("operator")]
    [AllowAnonymous]
    public IActionResult GetOperatorStatements()
    {
        var statements = new Dictionary<string, OperatorStatement>(StringComparer.Ordinal);
        foreach (var child in _configuration.GetSection("PrivacyNotice:OperatorStatements").GetChildren())
        {
            string? text = child["Text"]?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            string? source = child["Source"]?.Trim();
            string? sourceUrl = child["SourceUrl"]?.Trim();
            statements[child.Key] = new OperatorStatement(text,
                string.IsNullOrEmpty(source) ? null : source,
                string.IsNullOrEmpty(sourceUrl) ? null : sourceUrl);
        }

        return Ok(new { statements });
    }
}

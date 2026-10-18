using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MobileGnollHackLogger.Data;
using System.IO;
using Overseer.Services;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OngoingChatManager _ongoingChatManager;
    private readonly ChatRetentionService _chatRetentionService;
    private readonly Overseer.Services.Privacy.ConfidentialPolicyResolver _confidentialPolicyResolver;
    private readonly Overseer.Services.Privacy.ContentProtectionService _contentProtection;
    private readonly ILogger<SessionController> _logger;

    public SessionController(SignInManager<ApplicationUser> signInManager, ApplicationDbContext dbContext, IMemoryCache cache, IConfiguration configuration, IServiceScopeFactory scopeFactory, OngoingChatManager ongoingChatManager, ChatRetentionService chatRetentionService, Overseer.Services.Privacy.ConfidentialPolicyResolver confidentialPolicyResolver, Overseer.Services.Privacy.ContentProtectionService contentProtection, ILogger<SessionController> logger)
    {
        _signInManager = signInManager;
        _dbContext = dbContext;
        _cache = cache;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _ongoingChatManager = ongoingChatManager;
        _chatRetentionService = chatRetentionService;
        _confidentialPolicyResolver = confidentialPolicyResolver;
        _contentProtection = contentProtection;
        _logger = logger;
    }

    private const string InvalidCredentialsMessage = "Invalid credentials.";

    /* Every sign-in refusal is a 401, the status GnollHack's Overseer page stops retrying on
       (with 400 and 404); code is throttled, locked_out, banned, email_not_confirmed or
       invalid_credentials. Only a caller with the correct password is told banned or
       email_not_confirmed. */
    private UnauthorizedObjectResult SignInRefused(string code, string message)
        => Unauthorized(new { code, message });

    [HttpPost("create")]
    [IgnoreAntiforgeryToken] // CRITICAL: Called by a native client without standard CSRF tokens
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Overseer.Security.RateLimitPolicies.Login)]
    public async Task<IActionResult> Create(
        [FromForm] CreateSessionRequest request,
        [FromServices] Overseer.Services.Security.OverseerAccessService? overseerAccess = null,
        [FromServices] Overseer.Services.Security.ISecurityEventRecorder? securityEvents = null,
        [FromServices] Overseer.Services.Telemetry.IActivityCounter? activityCounter = null,
        [FromServices] GnollHackServer.Data.Security.GameApiSignInThrottle? signInThrottle = null,
        [FromServices] Microsoft.Extensions.Options.IOptions<Overseer.Models.AiUsageGuardOptions>? guardOptions = null,
        [FromServices] Overseer.Security.UserTurnRateLimiter? turnLimiter = null,
        [FromServices] Overseer.Security.UserTurnConcurrencyGate? turnGate = null,
        [FromServices] GnollHackServer.Data.Legal.TermsAcceptanceStore? terms = null,
        [FromServices] Overseer.Services.Legal.PendingGreetingStore? pendingGreetings = null)
    {
        var expectedSecret = _configuration["AntiForgeryToken"];
        if (string.IsNullOrEmpty(expectedSecret) || request.AntiForgeryToken != expectedSecret)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Invalid credentials.");
        }

        string? remoteIp = HttpContext?.Connection.RemoteIpAddress?.ToString();

        /* GnollHack calls this with its stored credentials and retries, so failures count toward
           the game-style throttle and never toward Identity lockout. Checked before the user is
           looked up: a throttled request costs no lookup and no password hash. */
        var decision = signInThrottle?.Check(request.UserName, remoteIp);
        if (decision is { Allowed: false } throttled)
        {
            securityEvents?.Record(new Overseer.Services.Security.SecurityEventEntry(
                SecurityEventKind.RateLimitRejected, IpAddress: remoteIp, Code: "session-create", Detail: throttled.Reason));
            return SignInRefused("throttled", GnollHackServer.Data.Security.GameApiSignInResponses.For(
                GnollHackServer.Data.Security.GameApiSignInOutcome.Throttled, throttled.RetryAfter).Body);
        }

        var user = await _signInManager.UserManager.FindByNameAsync(request.UserName);
        if (user == null)
        {
            signInThrottle?.RecordFailure(request.UserName, remoteIp);
            // The attempted name is never stored.
            securityEvents?.Record(new Overseer.Services.Security.SecurityEventEntry(
                SecurityEventKind.LoginFailed, IpAddress: remoteIp, Code: "session-create", Detail: "unknown-user"));
            return SignInRefused("invalid_credentials", InvalidCredentialsMessage);
        }

        // A locked-out account is still refused: the lockout is checked whatever the flag says.
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);

        // Identity refuses a banned or unconfirmed account before it checks the password, so the password is checked here.
        bool? passwordCorrect = result.IsNotAllowed
            ? await _signInManager.UserManager.CheckPasswordAsync(user, request.Password)
            : null;
        var (outcome, countsAsFailure) = GnollHackServer.Data.Security.GameApiSignInClassifier.Classify(
            result, passwordCorrect, isBanned: user.IsBanned == true, emailConfirmed: user.EmailConfirmed);

        if (outcome != GnollHackServer.Data.Security.GameApiSignInOutcome.Success)
        {
            if (countsAsFailure)
            {
                signInThrottle?.RecordFailure(request.UserName, remoteIp);
            }

            switch (outcome)
            {
                case GnollHackServer.Data.Security.GameApiSignInOutcome.LockedOut:
                {
                    securityEvents?.Record(new Overseer.Services.Security.SecurityEventEntry(
                        SecurityEventKind.LoginLockedOut, UserId: user.Id, IpAddress: remoteIp, Code: "session-create"));
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    TimeSpan? remaining = user.LockoutEnd is DateTimeOffset end ? end - now : null;
                    return SignInRefused("locked_out", GnollHackServer.Data.Security.GameApiSignInResponses.For(outcome,
                        remaining is TimeSpan r && r > TimeSpan.Zero ? r : TimeSpan.FromMinutes(1), now).Body);
                }
                case GnollHackServer.Data.Security.GameApiSignInOutcome.Banned:
                    return SignInRefused("banned", GnollHackServer.Data.Security.GameApiSignInResponses.For(outcome).Body);
                case GnollHackServer.Data.Security.GameApiSignInOutcome.EmailNotConfirmed:
                    return SignInRefused("email_not_confirmed", GnollHackServer.Data.Security.GameApiSignInResponses.For(outcome).Body);
                default:
                    securityEvents?.Record(new Overseer.Services.Security.SecurityEventEntry(
                        SecurityEventKind.LoginFailed, UserId: user.Id, IpAddress: remoteIp, Code: "session-create"));
                    return SignInRefused("invalid_credentials", InvalidCredentialsMessage);
            }
        }

        signInThrottle?.RecordSuccess(request.UserName, remoteIp);
        await GnollHackServer.Data.Security.AccountActivity.TouchSignInAsync(_dbContext, user.Id, DateTime.UtcNow, _logger);

        // Anonymous until the password passes, so the block is checked here rather than by a filter.
        var block = overseerAccess == null ? null : await overseerAccess.GetBlockAsync(user.Id);
        if (block != null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, Overseer.Services.Security.OverseerAccessService.BlockedBody(block));
        }

        var session = new ChatSession
        {
            AspNetUserId = user.Id,
            Title = string.IsNullOrWhiteSpace(request.Title) ? "GnollHack Session" : request.Title,
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow,
            ClientSettings = request.OverseerSettings,
            IsGnollHackSession = request.IsGnollHackSession,
            IsConfidential = request.IsConfidential
        };

        /* Snapshotted at creation with the two retention scalars, as every other creation path
           does, so a later change to the user's defaults or the administrator's floor cannot
           retroactively weaken what was promised about this session. */
        if (request.IsConfidential)
        {
            var confidentialSettings = await _dbContext.UserAiSettings.FindAsync(user.Id);
            Overseer.Services.Privacy.ConfidentialPolicyResolver.ApplyToSession(
                session, _confidentialPolicyResolver.Resolve(confidentialSettings));
        }

        /* Inserted under a fixed title when the session stores encrypted: the requested title is
           enveloped after the first save, and its plaintext never reaches the database. */
        bool encryptContent = Overseer.Services.Privacy.ConfidentialPolicyResolver.StoresEncrypted(session);
        var startingTitle = session.Title;
        if (encryptContent)
            session.Title = ChatController.ConfidentialPlaceholderTitle;

        _dbContext.ChatSession.Add(session);
        await _dbContext.SaveChangesAsync();

        if (activityCounter != null)
            await activityCounter.TryAddAsync(user.Id, new Overseer.Services.Telemetry.ActivityDelta(
                Chats: 1,
                GnollHackChats: request.IsGnollHackSession ? 1 : 0,
                ConfidentialChats: request.IsConfidential ? 1 : 0), DateTime.UtcNow);

        /* The DEK is created after the session has its identity value, because the id is the
           envelope's associated data: a key wrapped onto an unsaved session produces rows
           nothing can decrypt. */
        if (encryptContent)
        {
            _contentProtection.EnsureSessionKey(session);
            session.Title = _contentProtection.Encrypt(session, startingTitle);
            await _dbContext.SaveChangesAsync();
        }

        string? Protect(string? value)
            => encryptContent ? _contentProtection.Encrypt(session, value) : value;

        await _chatRetentionService.EnforceUserSessionQuotaAsync(user.Id);

        /* Current apps send both fields; older ones send the raw dump only. Falling back on an
           empty result rather than on the field being absent means a whitespace-only
           SnapshotText does not suppress a usable SnapshotHtml. */
        string snapshot = DumpHtmlSanitizer.PrepareFlattenedSnapshot(request.SnapshotText);
        string snapshotSource = "text";
        if (snapshot.Length == 0 && !string.IsNullOrWhiteSpace(request.SnapshotHtml))
        {
            snapshot = Overseer.Services.ChatService.SanitizeSnapshotForLlm(request.SnapshotHtml);
            snapshotSource = "html";
        }

        if (snapshot.Length > 0)
        {
            var systemMsg = new ChatMessage
            {
                ChatSessionId = session.Id,
                Role = "system",
                Content = Protect(Overseer.Services.ChatService.GameSnapshotPrefix + "\n" + snapshot),
                IsGameSnapshot = true,
                TimestampUtc = DateTime.UtcNow
            };
            _dbContext.ChatMessage.Add(systemMsg);
            await _dbContext.SaveChangesAsync();

            /* Which field the app used, so the HTML path can be retired once no app reports it.
               Never the content, the user or the title - and no length for a confidential
               session, whose payload size is a side-channel fingerprint. */
            if (session.IsConfidential)
                _logger.LogInformation("Game snapshot stored: source={Source}", snapshotSource);
            else
                _logger.LogInformation("Game snapshot stored: source={Source}, length={Length}",
                    snapshotSource, snapshot.Length);
        }

        // Save additional context channels to disk and/or as system messages
        var baseDir = _configuration["ConversationsDataLocation"];
        if (!string.IsNullOrEmpty(baseDir))
        {
            var sessionDir = Path.Combine(baseDir, session.Id.ToString());
            if (!Directory.Exists(sessionDir))
                Directory.CreateDirectory(sessionDir);

            /* A context file lands on disk enveloped in a confidential session, under a name
               carrying the .enc suffix. The suffix makes the file's state visible; the magic
               bytes are what the reader trusts, which is what lets an upgraded session hold
               both kinds. */
            async Task WriteContextFileAsync(string fileName, string text, ChatMessage owner)
            {
                string relPath = Path.Combine(session.Id.ToString(), fileName);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);

                if (encryptContent)
                {
                    relPath += Overseer.Services.Privacy.ContentProtectionService.EncryptedFileSuffix;
                    bytes = _contentProtection.EncryptFile(session, bytes);
                }

                await System.IO.File.WriteAllBytesAsync(Path.Combine(baseDir, relPath), bytes);

                // Registered as an attachment so it is deleted with the session.
                _dbContext.ChatMessageAttachment.Add(new ChatMessageAttachment
                {
                    ChatMessage = owner,
                    FileName = Protect(fileName),
                    ContentType = "text/plain",
                    RelativePath = relPath
                });
            }

            if (!string.IsNullOrWhiteSpace(request.MessageHistory))
            {
                // Insert a truncated summary as a system message so the AI knows it's available
                var preview = request.MessageHistory.Length > 2000
                    ? request.MessageHistory.Substring(0, 2000) + "\n\n[... truncated — full history available in session files ...]"
                    : request.MessageHistory;

                var msg = new ChatMessage
                {
                    ChatSessionId = session.Id,
                    Role = "system",
                    Content = Protect(Overseer.Services.ChatService.MessageHistoryPrefix
                        + " (last messages shown):\n" + preview),
                    IsMessageHistory = true,
                    TimestampUtc = DateTime.UtcNow
                };
                _dbContext.ChatMessage.Add(msg);

                // Saved to disk in full, because the history can be very large.
                await WriteContextFileAsync("message_history.txt", request.MessageHistory, msg);
            }

            if (!string.IsNullOrWhiteSpace(request.DirectoryManifest))
            {
                var msg = new ChatMessage
                {
                    ChatSessionId = session.Id,
                    Role = "system",
                    Content = Protect("Game Directory Manifest:\n" + request.DirectoryManifest),
                    TimestampUtc = DateTime.UtcNow
                };
                _dbContext.ChatMessage.Add(msg);

                await WriteContextFileAsync("directory_manifest.txt", request.DirectoryManifest, msg);
            }



            await _dbContext.SaveChangesAsync();
        }

        var token = Guid.NewGuid().ToString("N");
        var cacheKey = $"handoff_{token}";
        _cache.Set(cacheKey, new HandoffData { UserId = user.Id, SessionId = session.Id }, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
        });

        /* Truncated rather than refused: GnollHack shows any 400 from this endpoint as a
           credentials problem. */
        int maxInitialPromptCharacters = guardOptions?.Value.MaxInitialPromptCharacters ?? 0;
        if (maxInitialPromptCharacters > 0 && request.InitialPrompt != null
            && request.InitialPrompt.Length > maxInitialPromptCharacters)
        {
            request.InitialPrompt = request.InitialPrompt.Substring(0, maxInitialPromptCharacters);
        }

        bool termsAccepted = await ApplyTermsAsync(terms, user.Id, request);

        if (!string.IsNullOrWhiteSpace(request.InitialPrompt))
        {
            if (termsAccepted)
            {
                var starter = new HandoffGreetingStarter(_dbContext, _configuration, _scopeFactory, _ongoingChatManager,
                    turnLimiter, turnGate, securityEvents);
                await starter.TryStartAsync(user, Overseer.Services.Privacy.SessionRef.Persistent(session.Id), request.InitialPrompt, remoteIp);
            }
            else
            {
                // Started by LegalController once the user accepts the Terms in the chat.
                pendingGreetings?.Hold(session.Id, user.Id, request.InitialPrompt);
            }
        }

        return Ok(new
        {
            sessionId = session.Id,
            handoffToken = token
        });
    }

    /// <summary>
    /// Records the app's acceptance when it sent one, and says whether the user has accepted the required
    /// Terms. Never fails: current app builds retry every error except 400, 401 and 404.
    /// </summary>
    private async Task<bool> ApplyTermsAsync(GnollHackServer.Data.Legal.TermsAcceptanceStore? terms, string userId, CreateSessionRequest request)
    {
        if (terms == null)
        {
            return true;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(request.AcceptedTermsVersion))
            {
                await terms.RecordAsync(userId, GnollHackServer.Data.Legal.LegalDocumentKeys.OverseerTerms,
                    request.AcceptedTermsVersion.Trim(), GnollHackServer.Data.Legal.LegalDocumentKeys.SourceGnollHackApp,
                    contentSha256: null, clientVersion: ReadClientVersion(request.OverseerSettings));
            }

            var status = await terms.GetStatusAsync(userId, GnollHackServer.Data.Legal.LegalDocumentKeys.OverseerTerms);
            return status.IsAccepted;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not apply the Terms of Service acceptance at session creation.");
            return false;
        }
    }

    /// <summary>"GHVersion PortVersion" from the app's OverseerSettings, capped at 64 characters, or null.</summary>
    internal static string? ReadClientVersion(string? overseerSettings)
    {
        string? ghVersion = ClientSettingsReader.ReadString(overseerSettings, ClientSettingsReader.GnollHackVersionKey);
        string? portVersion = ClientSettingsReader.ReadString(overseerSettings, "PortVersion");
        string text = string.Join(" ", new[] { ghVersion, portVersion }.Where(v => !string.IsNullOrEmpty(v)));
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length <= 64 ? text : text[..64];
    }
}

public class CreateSessionRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AntiForgeryToken { get; set; } = string.Empty;

    /// <summary>Raw GnollHack dump HTML. Sent by app versions that leave sanitizing to the server.</summary>
    public string? SnapshotHtml { get; set; }

    /// <summary>
    /// The snapshot already flattened to text by the app. Takes precedence over
    /// <see cref="SnapshotHtml"/> when both are present, which is every request from a
    /// current app.
    /// </summary>
    public string? SnapshotText { get; set; }

    public string? MessageHistory { get; set; }        // NEW: Full 16384-message history (plain text)
    public string? DirectoryManifest { get; set; }     // NEW: Game directory file listing (tab-separated)

    public string? InitialPrompt { get; set; }         // NEW: Prompt to immediately start AI generation
    public string? OverseerSettings { get; set; }
    public string? Title { get; set; }                 // NEW: Contextual title for the session
    public bool IsGnollHackSession { get; set; }

    /// <summary>
    /// Creates the session in Confidentiality Mode: its content is stored enveloped, internet
    /// tools are off, no AI-made title is generated, and it is excluded from search.
    /// </summary>
    /// <remarks>
    /// The server accepts this so the game client can adopt it without a server change; a
    /// client that omits it creates a Standard session, which the user can upgrade from the
    /// chat window. There is no incognito option here: the client needs a session id back, and
    /// an incognito session has no row to give one.
    /// </remarks>
    public bool IsConfidential { get; set; }

    /// <summary>
    /// The Gnoll Overseer Terms version the player accepted in the app, recorded when it is the version
    /// required now. Apps that predate it send nothing, and their users accept in the chat.
    /// </summary>
    public string? AcceptedTermsVersion { get; set; }
}

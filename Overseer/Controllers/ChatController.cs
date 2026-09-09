using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Models;
using Overseer.Extensions;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using System.IO;
using Microsoft.Extensions.Caching.Memory;
using GnollHackServer.Data;
using Azure.Communication.Email;
using System.Text;
using Microsoft.AspNetCore.Identity;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.RateLimiting;
using Overseer.Security;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ChatService _chatService;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;
    private readonly EmailSender _emailSender;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly OngoingChatManager _ongoingChatManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SettingsService _settingsService;
    private readonly ChatRetentionService _chatRetentionService;
    private readonly Overseer.Services.Agents.SubAgentCatalogService _subAgentCatalogService;
    private readonly Overseer.Services.Privacy.AttachmentValidator _attachmentValidator;
    private readonly Overseer.Services.Privacy.ConfidentialityPostureService _confidentialityPostureService;
    private readonly Overseer.Services.Privacy.ConfidentialPolicyResolver _confidentialPolicyResolver;
    private readonly Overseer.Services.Privacy.ContentProtectionService _contentProtection;
    private readonly Overseer.Services.Privacy.EphemeralSessionStore _ephemeralSessions;
    private readonly ILogger<ChatController>? _logger;

    /* The aggregate request-body bound for a chat turn.

       Note the direction: this RAISES the limit. Kestrel's default is 30 MB, which is less
       than the five 15 MB attachments AttachmentValidator and the picker both permit once
       base64 expands them by 4/3 -- so today a user attaching two large images gets an opaque
       413 from the server rather than the per-file error A4 exists to give them. Making the
       bound explicit and consistent with the per-file limits is what makes those limits true;
       the per-file size, the count and the extension allowlist are the controls that actually
       bound an upload, and all three are configurable. This is a constant because
       RequestSizeLimit is an attribute argument and cannot read configuration. */
    /// <summary>
    /// Cap on a session title in plaintext characters. The column is wider only to hold the
    /// envelope; this is the business rule.
    /// </summary>
    public const int MaxPlaintextTitleLength = 256;

    private const long SendRequestBodyByteLimit = 134_217_728; // 128 MB

    public ChatController(
        ApplicationDbContext dbContext,
        ChatService chatService,
        IConfiguration configuration,
        IMemoryCache memoryCache,
        EmailSender emailSender,
        UserManager<ApplicationUser> userManager,
        OngoingChatManager ongoingChatManager,
        IServiceScopeFactory scopeFactory,
        SettingsService settingsService,
        ChatRetentionService chatRetentionService,
        Overseer.Services.Agents.SubAgentCatalogService subAgentCatalogService,
        Overseer.Services.Privacy.AttachmentValidator attachmentValidator,
        Overseer.Services.Privacy.ConfidentialityPostureService confidentialityPostureService,
        Overseer.Services.Privacy.ConfidentialPolicyResolver confidentialPolicyResolver,
        Overseer.Services.Privacy.ContentProtectionService contentProtection,
        Overseer.Services.Privacy.EphemeralSessionStore ephemeralSessions,
        ILogger<ChatController>? logger = null)
    {
        _dbContext = dbContext;
        _chatService = chatService;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _emailSender = emailSender;
        _userManager = userManager;
        _ongoingChatManager = ongoingChatManager;
        _scopeFactory = scopeFactory;
        _settingsService = settingsService;
        _chatRetentionService = chatRetentionService;
        _subAgentCatalogService = subAgentCatalogService;
        _attachmentValidator = attachmentValidator;
        _confidentialityPostureService = confidentialityPostureService;
        _confidentialPolicyResolver = confidentialPolicyResolver;
        _contentProtection = contentProtection;
        _ephemeralSessions = ephemeralSessions;
        _logger = logger;
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] int skip = 0, [FromQuery] int? take = null, [FromQuery] string? search = null)
    {
        var swTotal = Stopwatch.StartNew();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        int confidentialExcludedCount = 0;
        int pageSize = _configuration.GetValue<int>("ConversationListPageSize", 20);
        int effectiveTake = Math.Min(Math.Max(take ?? pageSize, pageSize), 500);

        await _chatRetentionService.EnforceUserSessionQuotaAsync(userId);

        var swDb = Stopwatch.StartNew();
        int totalActive = await _dbContext.ChatSession.CountAsync(s => s.AspNetUserId == userId && !s.IsDeleted);
        int pinnedCount = await _dbContext.ChatSession.CountAsync(s => s.AspNetUserId == userId && !s.IsDeleted && s.IsPinned);
        int unpinnedActive = Math.Max(0, totalActive - pinnedCount);

        var query = _dbContext.ChatSession
            .Where(s => s.AspNetUserId == userId && !s.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            /* Confidential sessions are excluded from search, and the count of them is
               reported so the omission is never silent. The predicate matches on Title as
               well as on message Content, so excluding the session covers both -- and it has
               to, because in an encrypted session neither one is searchable text. */
            query = query.Where(s => !s.IsConfidential).Where(s =>
                (s.Title != null && EF.Functions.Like(s.Title, $"%{term}%")) ||
                _dbContext.ChatMessage.Any(m => m.ChatSessionId == s.Id && !m.IsHidden && m.Role != "system" && m.Content != null && EF.Functions.Like(m.Content, $"%{term}%"))
            );
            confidentialExcludedCount = await _dbContext.ChatSession
                .CountAsync(s => s.AspNetUserId == userId && s.IsDeleted == false && s.IsConfidential);
        }

        var rawSessions = await query
            .OrderByDescending(s => s.IsPinned)
            .ThenByDescending(s => s.LastMessageUtc)
            .Skip(skip)
            .Take(effectiveTake + 1)
            .Select(s => new
            {
                s.Id, s.Title, s.LastMessageUtc, s.IsGnollHackSession, s.IsPinned,
                s.IsConfidential, s.EncryptedContentKey, s.ContentKeyNonce, s.ContentKeyTag, s.ContentKeyVersion
            })
            .ToListAsync();

        /* A confidential session's title is stored enveloped, so projecting it straight into
           the sidebar would render "enc:v1:..." to the user. Decrypted after the projection,
           in memory, one AES unwrap per session -- and the page is bounded by effectiveTake,
           so this is a handful of operations, not a scan. */
        var sessions = rawSessions.Select(s => new
        {
            s.Id,
            Title = DecryptListTitle(s.Id, s.Title, s.IsConfidential,
                s.EncryptedContentKey, s.ContentKeyNonce, s.ContentKeyTag, s.ContentKeyVersion),
            s.LastMessageUtc,
            s.IsGnollHackSession,
            s.IsPinned
        }).ToList();
        swDb.Stop();

        bool hasMore = sessions.Count > effectiveTake;
        if (hasMore)
        {
            sessions.RemoveAt(sessions.Count - 1);
        }

        swTotal.Stop();

        Response.Headers.Append("Access-Control-Expose-Headers", "Server-Timing");
        Response.Headers.Append("Server-Timing", $"total;dur={swTotal.ElapsedMilliseconds}, db;dur={swDb.ElapsedMilliseconds}");

        return Ok(new
        {
            sessions = sessions,
            hasMore = hasMore,
            activeCount = totalActive,
            pinnedCount = pinnedCount,
            totalCount = totalActive,
            maxQuota = _configuration.GetValue<int>("ChatRetentionSettings:MaxActiveSessionsPerUser", 50),
            maxPinned = _configuration.GetValue<int>("ChatRetentionSettings:MaxPinnedSessionsPerUser", 5),
            /* How many confidential chats the search skipped. Reported so the client can say
               so: a search that silently omits results is worse than one that finds nothing. */
            confidentialExcludedCount = confidentialExcludedCount
        });
    }

    public class UpdateTitleRequest
    {
        public string Title { get; set; } = string.Empty;
    }

    [HttpPut("sessions/{id}/title")]
    public async Task<IActionResult> UpdateSessionTitle(long id, [FromBody] UpdateTitleRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        
        var session = await _dbContext.ChatSession
            .FirstOrDefaultAsync(s => s.Id == id && s.AspNetUserId == userId);
            
        if (session == null) return NotFound();
        
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title cannot be empty.");
            
        if (System.Text.RegularExpressions.Regex.IsMatch(request.Title, @"[<>{}[\]\\/]"))
            return BadRequest("Title contains illegal characters.");

        string title = request.Title.Trim();

        /* The plaintext cap, which used to be enforced only by the column being 256 characters
           wide. The column is now 2048 to make room for an envelope, so without this check a
           2048-character title on a session later upgraded to confidential would need 10,973
           characters and throw on save -- in the rename path, with the user watching. */
        if (title.Length > MaxPlaintextTitleLength)
            return BadRequest($"Title cannot exceed {MaxPlaintextTitleLength} characters.");

        /* Encrypted before the write. A manual rename that stored plaintext would silently
           undo the guarantee for the one field a user is most likely to type something
           identifying into. The validation above runs on the plaintext and is unaffected --
           note that base64 legitimately contains a forward slash, so nothing may re-validate
           the stored value against that character class. */
        if (session.IsConfidential)
        {
            _contentProtection.EnsureSessionKey(session);
            session.Title = _contentProtection.Encrypt(session, title);
        }
        else
        {
            session.Title = title;
        }

        await _dbContext.SaveChangesAsync();
        
        return Ok();
    }

    public class SetConfidentialRequest
    {
        /// <summary>Only <c>true</c> is accepted. False is refused with 409 — the latch is one-way.</summary>
        public bool IsConfidential { get; set; }
    }

    /// <summary>
    /// Upgrades a session to Confidentiality Mode. **One-way**: confidential to normal is a
    /// <c>409</c>.
    /// </summary>
    /// <remarks>
    /// Downgrading would decrypt history stored under a stronger promise, and it could not
    /// recall content already sent to a provider under it — so there is nothing a downgrade
    /// could honestly mean. The refusal is a 409 rather than a 400 because the request is
    /// well-formed; it conflicts with the session's state.
    ///
    /// The upgrade is **not retroactive**. Earlier turns stay as they were stored, and their
    /// attachment files stay on disk under the names they already have. The response says so
    /// explicitly so the client can warn before the user commits.
    /// </remarks>
    [HttpPut("sessions/{id}/confidential")]
    public async Task<IActionResult> SetSessionConfidential(long id, [FromBody] SetConfidentialRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var session = await _dbContext.ChatSession
            .FirstOrDefaultAsync(s => s.Id == id && s.AspNetUserId == userId);

        if (session == null) return NotFound(new { error = "Session not found." });

        if (!request.IsConfidential)
        {
            return Conflict(new
            {
                error = "A confidential chat cannot be made normal again. Its stored content was written under "
                    + "a stronger promise, and what has already been sent to a model provider under that promise "
                    + "cannot be recalled."
            });
        }

        if (session.IsConfidential)
        {
            // Already there. Idempotent rather than an error: the client may retry.
            return Ok(new { isConfidential = true, alreadyConfidential = true });
        }

        var settings = await _dbContext.UserAiSettings.FindAsync(userId);
        var policy = _confidentialPolicyResolver.Resolve(settings);

        session.IsConfidential = true;
        session.ConfidentialUpgradedUtc = DateTime.UtcNow;
        Overseer.Services.Privacy.ConfidentialPolicyResolver.ApplyToSession(session, policy);

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            isConfidential = true,
            upgradedUtc = session.ConfidentialUpgradedUtc,
            /* Stated in the response, not only in the UI copy: an upgrade protects what comes
               next, and a client that does not say so leaves the user with a reasonable and
               wrong belief about what just happened. */
            retroactive = false,
            notice = "Earlier turns in this chat stay as they were already stored. Confidentiality applies "
                + "from here on."
        });
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSession(string sessionId)
    {
        var swTotal = Stopwatch.StartNew();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!Overseer.Services.Privacy.SessionRef.TryParse(sessionId, out var sessionRef)) return NotFound();

        if (sessionRef.IsEphemeral)
        {
            var held = _ephemeralSessions.Get(sessionRef, userId);
            /* Indistinguishable, on purpose, from a reference this user never owned: a closed
               or expired incognito chat is gone, and there is nothing to say about it that
               would not also confirm it had existed. */
            if (held == null) return NotFound();

            /* An incognito session is audited too, and the row carries only its reference. That
               a conversation was read is not the same class of fact as what was in it, and the
               mode's promise is about the latter. */
            await RecordAccessAsync(
                ChatAccessAction.SessionRead,
                userId,
                sessionRef: sessionRef.ToWireString(),
                wasConfidential: true,
                detail: $"{held.MessageCount} messages, incognito");

            return Ok(BuildEphemeralSessionPayload(held));
        }

        long id = sessionRef.PersistentId;

        var swDb = Stopwatch.StartNew();
        var session = await _dbContext.ChatSession
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.AspNetUserId == userId);
        swDb.Stop();
        var sessionMs = swDb.ElapsedMilliseconds;

        if (session == null) return NotFound();

        swDb.Restart();
        var messages = await _dbContext.ChatMessage
            .Where(m => m.ChatSessionId == id && m.Role != "system" && !m.IsHidden)
            .OrderBy(m => m.TimestampUtc)
            .Select(m => new { 
                m.Id, 
                m.Role, 
                m.Content, 
                m.TimestampUtc,
                m.TimeToFirstTokenMs,
                m.TotalDurationMs,
                m.ProviderUsed,
                m.ModelUsed,
                m.ModelDisplayNameUsed,
                m.ThinkingLevelUsed,
                m.ReasoningModeUsed,
                m.ServiceTierUsed,
                m.ActualServiceTierUsed,
                m.ContextPromptTokens,
                m.ContextOutputTokens,
                m.ContextWindowTokens,
                m.ContextInputLimitTokens,
                m.InputTokens,
                m.OutputTokens,
                m.CacheReadTokens,
                m.CacheCreationTokens,
                m.EstimatedCost,
                m.PricingSource,
                m.SystemAiConfigurationIdUsed
            })
            .ToListAsync();
        swDb.Stop();
        var messagesMs = swDb.ElapsedMilliseconds;

        var messageIds = messages.Select(m => m.Id).ToList();

        swDb.Restart();
        var attachments = messageIds.Count > 0
            ? await _dbContext.ChatMessageAttachment
                .Where(a => messageIds.Contains(a.ChatMessageId))
                .Select(a => new { a.ChatMessageId, a.Id, a.FileName, a.ContentType })
                .AsNoTracking()
                .ToListAsync()
            : [];
        swDb.Stop();
        var attachmentsMs = swDb.ElapsedMilliseconds;

        swDb.Restart();
        var toolCalls = messageIds.Count > 0
            ? await _dbContext.ChatMessageToolCall
                .Where(tc => messageIds.Contains(tc.ChatMessageId))
                .OrderBy(tc => tc.SortOrder)
                .Select(tc => new {
                    tc.ChatMessageId,
                    id = tc.ToolCallId,
                    name = tc.Name,
                    displayName = tc.DisplayName,
                    argsText = tc.ArgsText,
                    status = tc.Status,
                    result = tc.Result,
                    error = tc.Error,
                    agentName = tc.AgentName,
                    parentToolCallId = tc.ParentToolCallId,
                    depth = tc.Depth
                })
                .AsNoTracking()
                .ToListAsync()
            : [];
        swDb.Stop();
        var toolCallsMs = swDb.ElapsedMilliseconds;

        var attachmentsLookup = attachments.ToLookup(a => a.ChatMessageId);
        var toolCallsLookup = toolCalls.ToLookup(tc => tc.ChatMessageId);

        List<UserAiModel>? userModels = null;
        List<(SystemAiApiConfiguration Config, int ResolvedRole)>? systemModels = null;

        swDb.Restart();
        if (messages.Any(m => m.Role == "assistant" && !string.IsNullOrEmpty(m.ModelUsed)))
        {
            userModels = await _dbContext.UserAiModels
                .Where(um => um.AspNetUserId == userId)
                .AsNoTracking()
                .ToListAsync();
                
            systemModels = await _settingsService.GetResolvedSystemModelsAsync(userId);
        }
        swDb.Stop();
        var modelsMs = swDb.ElapsedMilliseconds;

        // ConfigurationExtensions.IsAdmin is the project's single definition of "admin" and is what
        // AdminRequirement uses, so the read path and the authorization policy cannot drift apart.
        bool isAdmin = _configuration.IsAdmin(User.Identity?.Name);

        var swAsm = Stopwatch.StartNew();
        /* One closure over the loaded session, so every field decrypts under that session's own
            DEK. A non-envelope value passes through unchanged, which is what makes an upgraded
            session -- legitimately part plaintext, part ciphertext -- read correctly. */
        string? Decrypt(string? stored) => _contentProtection.Decrypt(session, stored);

        var formattedMessages = messages.Select(m => {
            string? modelDisplayName = m.ModelDisplayNameUsed;
            string? thinkingLevel = m.ThinkingLevelUsed;
            string? reasoningMode = m.ReasoningModeUsed;
            string? serviceTier = m.ServiceTierUsed;
            string? actualServiceTier = m.ActualServiceTierUsed;
            if (m.Role == "assistant" && !string.IsNullOrEmpty(m.ModelUsed)) {
                if (string.IsNullOrEmpty(modelDisplayName) || string.IsNullOrEmpty(thinkingLevel) || string.IsNullOrEmpty(reasoningMode) || string.IsNullOrEmpty(serviceTier)) {
                    var um = userModels?.FirstOrDefault(x => x.ModelId == m.ModelUsed);
                    if (um != null) {
                        if (string.IsNullOrEmpty(modelDisplayName)) {
                            if (!string.IsNullOrEmpty(um.DisplayName)) {
                                modelDisplayName = um.DisplayName;
                            } else {
                                modelDisplayName = m.ModelUsed;
                            }
                        }
                        thinkingLevel ??= um.ThinkingLevel;
                        reasoningMode ??= um.ReasoningMode;
                        serviceTier ??= um.ServiceTier;
                    } else if (systemModels != null) {
                        var sm = systemModels.FirstOrDefault(x => x.Config.ModelId == m.ModelUsed);
                        if (sm.Config != null) {
                            if (string.IsNullOrEmpty(modelDisplayName)) {
                                modelDisplayName = sm.Config.DisplayName ?? m.ModelUsed;
                            }
                            thinkingLevel ??= sm.Config.ThinkingLevel;
                            reasoningMode ??= sm.Config.ReasoningMode;
                            serviceTier ??= sm.Config.ServiceTier;
                        } else if (string.IsNullOrEmpty(modelDisplayName)) {
                            modelDisplayName = m.ModelUsed;
                        }
                    }
                }
            }

            /* FileName, argsText, result and error are all in the encrypted set. ContentType
                is not, by the choice recorded in the framework document. */
            var msgAttachments = attachmentsLookup[m.Id]
                .Select(a => new { a.Id, FileName = Decrypt(a.FileName), a.ContentType })
                .ToList();

            var msgToolCalls = toolCallsLookup[m.Id]
                .Select(tc => new {
                    tc.id,
                    tc.name,
                    tc.displayName,
                    argsText = Decrypt(tc.argsText),
                    tc.status,
                    result = Decrypt(tc.result),
                    error = Decrypt(tc.error),
                    tc.agentName,
                    tc.parentToolCallId,
                    tc.depth
                })
                .ToList();

            return new {
                m.Id,
                m.Role,
                Content = Decrypt(m.Content),
                m.TimestampUtc,
                m.TimeToFirstTokenMs,
                m.TotalDurationMs,
                Attachments = msgAttachments,
                ToolCalls = msgToolCalls,
                ModelDisplayName = modelDisplayName,
                ThinkingLevel = thinkingLevel,
                ReasoningMode = reasoningMode,
                ServiceTier = serviceTier,
                ActualServiceTier = actualServiceTier,
                m.ContextPromptTokens,
                m.ContextOutputTokens,
                m.ContextWindowTokens,
                m.ContextInputLimitTokens,
                m.InputTokens,
                m.OutputTokens,
                m.CacheReadTokens,
                m.CacheCreationTokens,
                // An operator-funded reply's cost is an operator figure: a regular user is shown no price at
                // all for it, never zero, which would read as "this reply was free to produce". IsOperatorCost
                // travels beside it so the client can tell "withheld" from "unpriced" and keep the PARTIAL
                // badge off. Replies saved before SystemAiConfigurationIdUsed existed carry null attribution
                // and are therefore treated as user-funded — the accepted D1-A limitation.
                EstimatedCost = (isAdmin || !m.SystemAiConfigurationIdUsed.HasValue) ? m.EstimatedCost : null,
                PricingSource = (isAdmin || !m.SystemAiConfigurationIdUsed.HasValue) ? m.PricingSource : null,
                IsOperatorCost = m.SystemAiConfigurationIdUsed.HasValue
            };
        }).ToList();
        swAsm.Stop();
        var assemblyMs = swAsm.ElapsedMilliseconds;

        var ongoing = _ongoingChatManager.TryGet(sessionRef);
        bool showDebugLog = _configuration.ShouldShowDebugLog(User.Identity?.Name);

        bool hasOngoing = ongoing != null;
        object? ongoingData = null;
        int? lastEventSeqNo = ongoing?.EventSequence;

        if (ongoing != null)
        {
            if (ongoing.IsCompleted)
            {
                bool messageInDb = (ongoing.SavedMessageId.HasValue && messages.Any(m => m.Id == ongoing.SavedMessageId.Value))
                    || messages.Any(m => m.Role == "assistant" && m.TimestampUtc >= ongoing.StartedAtUtc.AddSeconds(-2));

                if (messageInDb)
                {
                    hasOngoing = false;
                }
                else
                {
                    ongoingData = new
                    {
                        events = ongoing.AccumulatedEvents
                            .Where(e => showDebugLog || e.Type != "debug")
                            .OrderBy(e => e.SeqNo)
                            .Select(e => new { type = e.Type, data = e.Data, seqNo = e.SeqNo }).ToList()
                    };
                }
            }
            else
            {
                ongoingData = new
                {
                    events = ongoing.AccumulatedEvents
                        .Where(e => showDebugLog || e.Type != "debug")
                        .OrderBy(e => e.SeqNo)
                        .Select(e => new { type = e.Type, data = e.Data, seqNo = e.SeqNo }).ToList()
                };
            }
        }

        bool hasGameSnapshot = await _dbContext.ChatMessage
            .AnyAsync(m => m.ChatSessionId == id && m.Role == "system" && m.IsGameSnapshot);

        /* The privacy badge, from the policy this session was created or upgraded under.
           The posture is still PostureResolution.Nothing here: it belongs to whichever key
           funds a given turn, which a session-load request has not chosen yet, so the badge
           reports orange until the turn resolves it. Red still wins over that, because an
           inactive control is a property of the session rather than of the key. */
        var sessionPolicy = session.IsConfidential
            ? Overseer.Services.Privacy.ConfidentialPolicyResolver.ReadSnapshot(session)
            : null;
        var privateBadge = _confidentialityPostureService.ResolveBadge(
            session.IsConfidential,
            Overseer.Services.Privacy.PostureResolution.Nothing,
            sessionPolicy?.ToControlState() ?? Overseer.Services.Privacy.ConfidentialControlState.NoneActive);

        var payload = new
        {
            session.Id,
            SessionRef = sessionRef.ToWireString(),
            Title = Decrypt(session.Title),
            session.IsGnollHackSession,
            // The wire name stays totalEstimatedCost; its meaning is "the total this viewer is entitled to
            // see". Renaming it would force a client change for no gain — the client already treats it as an
            // opaque authoritative figure.
            TotalEstimatedCost = isAdmin ? session.TotalEstimatedCost : session.TotalUserEstimatedCost,
            hasGameSnapshot,
            session.IsConfidential,
            IsEphemeral = false,
            PrivateBadge = privateBadge.State == Overseer.Services.Privacy.PrivateBadgeState.None
                ? null
                : new { State = privateBadge.State.ToString().ToLowerInvariant(), privateBadge.Label, privateBadge.Tooltip },
            Messages = formattedMessages,
            HasOngoingGeneration = hasOngoing,
            OngoingGeneration = ongoingData,
            LastEventSeqNo = lastEventSeqNo
        };

        await RecordAccessAsync(
            ChatAccessAction.SessionRead,
            userId,
            chatSessionId: id,
            sessionRef: sessionRef.ToWireString(),
            wasConfidential: session.IsConfidential,
            detail: $"{formattedMessages.Count} messages");

        return Ok(payload);
    }

    /// <summary>
    /// The same shape <see cref="GetSession"/> returns for a persisted session, assembled from
    /// the ephemeral store.
    /// </summary>
    /// <remarks>
    /// A separate builder rather than a branch threaded through the persisted path, because
    /// almost none of that path applies: there are no EF projections to run, no timings worth
    /// reporting on an in-memory read, and nothing to decrypt -- an ephemeral session is not
    /// encrypted, since it has no rest to protect. What has to match exactly is the wire shape,
    /// so the client renders one kind of transcript.
    /// </remarks>
    private object BuildEphemeralSessionPayload(Overseer.Services.Privacy.EphemeralSession held)
    {
        bool isAdmin = _configuration.IsAdmin(User.Identity?.Name);
        var session = held.Session;
        var sessionRef = held.Ref;

        var attachments = held.Attachments;
        var messages = held.Messages
            .Where(m => m.Role != "system" && !m.IsHidden)
            .OrderBy(m => m.TimestampUtc)
            .Select(m => new
            {
                m.Id,
                m.Role,
                Content = m.Content,
                m.TimestampUtc,
                m.TimeToFirstTokenMs,
                m.TotalDurationMs,
                Attachments = attachments
                    .Where(a => a.ChatMessageId == m.Id)
                    .Select(a => new { a.Id, a.FileName, a.ContentType })
                    .ToList(),
                ToolCalls = m.ToolCalls
                    .OrderBy(tc => tc.SortOrder)
                    .Select(tc => new
                    {
                        id = tc.ToolCallId,
                        name = tc.Name,
                        displayName = tc.DisplayName,
                        argsText = tc.ArgsText,
                        status = tc.Status,
                        result = tc.Result,
                        error = tc.Error,
                        agentName = tc.AgentName,
                        parentToolCallId = tc.ParentToolCallId,
                        depth = tc.Depth
                    })
                    .ToList(),
                ModelDisplayName = m.ModelDisplayNameUsed ?? m.ModelUsed,
                ThinkingLevel = m.ThinkingLevelUsed,
                ReasoningMode = m.ReasoningModeUsed,
                ServiceTier = m.ServiceTierUsed,
                ActualServiceTier = m.ActualServiceTierUsed,
                m.ContextPromptTokens,
                m.ContextOutputTokens,
                m.ContextWindowTokens,
                m.ContextInputLimitTokens,
                m.InputTokens,
                m.OutputTokens,
                m.CacheReadTokens,
                m.CacheCreationTokens,
                EstimatedCost = (isAdmin || !m.SystemAiConfigurationIdUsed.HasValue) ? m.EstimatedCost : null,
                PricingSource = (isAdmin || !m.SystemAiConfigurationIdUsed.HasValue) ? m.PricingSource : null,
                IsOperatorCost = m.SystemAiConfigurationIdUsed.HasValue
            })
            .ToList();

        var ongoing = _ongoingChatManager.TryGet(sessionRef);
        bool showDebugLog = _configuration.ShouldShowDebugLog(User.Identity?.Name);
        bool hasOngoing = ongoing != null;
        object? ongoingData = null;

        if (ongoing != null)
        {
            bool alreadyStored = ongoing.IsCompleted
                && ((ongoing.SavedMessageId.HasValue && messages.Any(m => m.Id == ongoing.SavedMessageId.Value))
                    || messages.Any(m => m.Role == "assistant" && m.TimestampUtc >= ongoing.StartedAtUtc.AddSeconds(-2)));

            if (alreadyStored)
            {
                hasOngoing = false;
            }
            else
            {
                ongoingData = new
                {
                    events = ongoing.AccumulatedEvents
                        .Where(e => showDebugLog || e.Type != "debug")
                        .OrderBy(e => e.SeqNo)
                        .Select(e => new { type = e.Type, data = e.Data, seqNo = e.SeqNo }).ToList()
                };
            }
        }

        var sessionPolicy = session.IsConfidential
            ? Overseer.Services.Privacy.ConfidentialPolicyResolver.ReadSnapshot(session)
            : null;
        var privateBadge = _confidentialityPostureService.ResolveBadge(
            session.IsConfidential,
            Overseer.Services.Privacy.PostureResolution.Nothing,
            sessionPolicy?.ToControlState() ?? Overseer.Services.Privacy.ConfidentialControlState.NoneActive);

        return new
        {
            Id = 0L,
            SessionRef = sessionRef.ToWireString(),
            Title = session.Title,
            session.IsGnollHackSession,
            TotalEstimatedCost = isAdmin ? session.TotalEstimatedCost : session.TotalUserEstimatedCost,
            hasGameSnapshot = held.Messages.Any(m => m.Role == "system" && m.IsGameSnapshot),
            session.IsConfidential,
            IsEphemeral = true,
            EphemeralExpiresUtc = held.LastAccessUtc + _ephemeralSessions.Timeout,
            PrivateBadge = privateBadge.State == Overseer.Services.Privacy.PrivateBadgeState.None
                ? null
                : new { State = privateBadge.State.ToString().ToLowerInvariant(), privateBadge.Label, privateBadge.Tooltip },
            Messages = messages,
            HasOngoingGeneration = hasOngoing,
            OngoingGeneration = ongoingData,
            LastEventSeqNo = ongoing?.EventSequence
        };
    }

    /// <summary>
    /// Discards an ephemeral session and overwrites what it held.
    /// </summary>
    /// <remarks>
    /// Explicit closing exists alongside the sliding timeout because a browser that goes away
    /// without saying so is the normal case, not the exception -- the timeout is what actually
    /// bounds the lifetime, and this is what lets a user end it now. Either way the content is
    /// unrecoverable: there is no trash for an incognito chat, because there is nothing to move
    /// into one.
    /// </remarks>
    [HttpPost("sessions/{sessionId}/ephemeral/close")]
    public IActionResult CloseEphemeralSession(string sessionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!Overseer.Services.Privacy.SessionRef.TryParse(sessionId, out var sessionRef) || !sessionRef.IsEphemeral)
        {
            return BadRequest(new { error = "Not an incognito session reference." });
        }

        /* Cancelling first: a turn still streaming holds the plaintext of this session in its
           own locals, and closing the store underneath it would leave that turn talking to a
           session that no longer exists. */
        _ongoingChatManager.TryCancelAndRemove(sessionRef);

        return _ephemeralSessions.Close(sessionRef, userId)
            ? Ok()
            : NotFound();
    }

    /// <summary>
    /// Serves an attachment held in an ephemeral session's memory.
    /// </summary>
    /// <remarks>
    /// A route of its own rather than an extension of <see cref="GetAttachment"/>, because the
    /// id is not a <c>ChatMessageAttachment</c> key: it is an index within one session, so it
    /// is only meaningful under that session's reference. Sharing the numeric route would make
    /// two different id spaces indistinguishable to the reader and to the authorisation check.
    /// </remarks>
    [HttpGet("sessions/{sessionId}/attachments/{attachmentId:int}")]
    [EnableRateLimiting(RateLimitPolicies.Attachment)]
    public async Task<IActionResult> GetEphemeralAttachment(string sessionId, int attachmentId, [FromQuery] bool inline = false)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!Overseer.Services.Privacy.SessionRef.TryParse(sessionId, out var sessionRef) || !sessionRef.IsEphemeral)
        {
            return NotFound();
        }

        var held = _ephemeralSessions.Get(sessionRef, userId);
        if (held == null) return NotFound();

        var attachment = held.FindAttachment(attachmentId);
        if (attachment == null) return NotFound();

        /* Same hardening as the persisted path: an inline render is allowed only for an image,
           the stored content type is never echoed back for anything else, and the filename
           reaches the header only through Content-Disposition on a download. */
        bool isImage = attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        /* The attachment id here is a per-session index rather than a ChatMessageAttachment key,
           so it goes in the detail instead of the id column -- a reviewer reading the table must
           not take it for a row that can be looked up. */
        await RecordAccessAsync(
            ChatAccessAction.AttachmentRead,
            userId,
            sessionRef: sessionRef.ToWireString(),
            wasConfidential: true,
            detail: $"incognito attachment {attachment.Id}, {(inline && isImage ? "inline" : "download")}");

        if (inline && isImage)
        {
            return File(attachment.Bytes, attachment.ContentType);
        }

        return File(attachment.Bytes, "application/octet-stream", attachment.FileName);
    }

    [HttpDelete("sessions/{id}")]
    public async Task<IActionResult> DeleteSession(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        
        var success = await _chatRetentionService.SoftDeleteSessionAsync(id, userId, "User");
        if (!success) return NotFound();

        return Ok();
    }

    [HttpPut("sessions/{id}/pin")]
    public async Task<IActionResult> TogglePin(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var isPinned = await _chatRetentionService.TogglePinSessionAsync(id, userId);
            if (isPinned == null) return NotFound();
            return Ok(new { isPinned = isPinned.Value });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    public class BulkDeleteSessionsRequest
    {
        public bool IncludePinned { get; set; }
    }

    [HttpPost("sessions/bulk-delete")]
    public async Task<IActionResult> BulkDeleteSessions([FromBody] BulkDeleteSessionsRequest? request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        bool includePinned = request?.IncludePinned ?? false;
        int count = await _chatRetentionService.BulkSoftDeleteSessionsAsync(userId, includePinned, "User");
        return Ok(new { count });
    }

    [HttpPost("sessions/unpin-all")]
    public async Task<IActionResult> UnpinAllSessions()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        int count = await _chatRetentionService.UnpinAllSessionsAsync(userId);
        return Ok(new { count });
    }

    [HttpGet("sessions/trash")]
    public async Task<IActionResult> GetTrashSessions([FromQuery] string? search = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var query = _dbContext.ChatSession
            .Where(s => s.AspNetUserId == userId && s.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            /* Confidential sessions are excluded from search, and the count of them is
               reported so the omission is never silent. The predicate matches on Title as
               well as on message Content, so excluding the session covers both -- and it has
               to, because in an encrypted session neither one is searchable text. */
            query = query.Where(s => !s.IsConfidential).Where(s =>
                (s.Title != null && EF.Functions.Like(s.Title, $"%{term}%")) ||
                _dbContext.ChatMessage.Any(m => m.ChatSessionId == s.Id && !m.IsHidden && m.Role != "system" && m.Content != null && EF.Functions.Like(m.Content, $"%{term}%"))
            );
            /* No count reported here: this endpoint returns a bare array, and a confidential
               session with immediate purge -- the default -- never reaches the trash at all. */
        }

        var sessions = await query
            .OrderByDescending(s => s.DeletedUtc)
            .Select(s => new TrashSessionDto
            {
                Id = s.Id,
                Title = s.Title,
                CreatedUtc = s.CreatedUtc,
                LastMessageUtc = s.LastMessageUtc,
                DeletedUtc = s.DeletedUtc,
                DeletionReason = s.DeletionReason,
                IsPinned = s.IsPinned,
                IsGnollHackSession = s.IsGnollHackSession,
                DaysRemaining = Math.Max(0, 30 - (int)EF.Functions.DateDiffDay(s.DeletedUtc ?? DateTime.UtcNow, DateTime.UtcNow)),
                MessageCount = _dbContext.ChatMessage.Count(m => m.ChatSessionId == s.Id && m.Role != "system" && !m.IsHidden)
            })
            .ToListAsync();

        /* The trash list needs the same title decryption as the sidebar. A confidential session
           reaching the trash at all means immediate purge was turned off for it, which is
           possible but not the default -- so this loads no key material in the common case. */
        var trashKeys = await query
            .Where(s => s.IsConfidential)
            .Select(s => new
            {
                s.Id, s.EncryptedContentKey, s.ContentKeyNonce, s.ContentKeyTag, s.ContentKeyVersion
            })
            .ToDictionaryAsync(s => s.Id);

        foreach (var dto in sessions)
        {
            if (trashKeys.TryGetValue(dto.Id, out var keys))
            {
                dto.Title = DecryptListTitle(
                    dto.Id, dto.Title, isConfidential: true,
                    keys.EncryptedContentKey, keys.ContentKeyNonce, keys.ContentKeyTag, keys.ContentKeyVersion);
            }
        }

        return Ok(sessions);
    }

    /// <summary>
    /// Decrypts a title from a list projection, where the whole session entity was not loaded.
    /// </summary>
    /// <remarks>
    /// The list queries project only the columns they need, so this rebuilds the minimum
    /// <see cref="ChatSession"/> the decryptor requires — the id, which is the associated data,
    /// and the wrapped DEK. Loading whole entities to render a sidebar would be the alternative
    /// and is worse: it would drag every message-count subquery's parent row into memory.
    /// </remarks>
    private string? DecryptListTitle(
        long sessionId, string? title, bool isConfidential,
        string? encryptedContentKey, string? nonce, string? tag, string? version)
    {
        if (!isConfidential || !Overseer.Services.Privacy.ContentProtectionService.IsEncrypted(title))
            return title;

        var shim = new ChatSession
        {
            Id = sessionId,
            EncryptedContentKey = encryptedContentKey,
            ContentKeyNonce = nonce,
            ContentKeyTag = tag,
            ContentKeyVersion = version
        };

        return _contentProtection.Decrypt(shim, title);
    }

    [HttpPost("sessions/{id}/restore")]
    public async Task<IActionResult> RestoreSession(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var success = await _chatRetentionService.RestoreSessionAsync(id, userId);
            if (!success) return NotFound();
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("sessions/{id}/permanent")]
    public async Task<IActionResult> PermanentDeleteSession(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var session = await _dbContext.ChatSession.FirstOrDefaultAsync(s => s.Id == id && s.AspNetUserId == userId);
        if (session == null) return NotFound();

        await _chatRetentionService.PermanentlyPurgeSessionsAsync(new List<long> { id });
        return Ok();
    }

    [HttpPost("sessions/trash/empty")]
    public async Task<IActionResult> EmptyTrash()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var trashIds = await _dbContext.ChatSession
            .Where(s => s.AspNetUserId == userId && s.IsDeleted)
            .Select(s => s.Id)
            .ToListAsync();

        if (trashIds.Count > 0)
        {
            await _chatRetentionService.PermanentlyPurgeSessionsAsync(trashIds);
        }

        return Ok(new { count = trashIds.Count });
    }

    [HttpGet("attachments/{id}")]
    [EnableRateLimiting(RateLimitPolicies.Attachment)]
    public async Task<IActionResult> GetAttachment(long id, [FromQuery] bool inline = false)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var attachment = await _dbContext.ChatMessageAttachment
            .Include(a => a.ChatMessage)
            .ThenInclude(m => m!.ChatSession)
            .FirstOrDefaultAsync(a => a.Id == id);
            
        if (attachment == null || attachment.ChatMessage?.ChatSession?.AspNetUserId != userId)
            return NotFound();
            
        var baseDir = _configuration["ConversationsDataLocation"];
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(attachment.RelativePath))
            return NotFound();
            
        var filePath = Path.Combine(baseDir, attachment.RelativePath);
        if (!System.IO.File.Exists(filePath))
            return NotFound();

        /* The stored content type is whatever the client declared at upload, so it is never
           echoed back unless it is on the allowlist -- and only a genuinely safe image type is
           served inline. Everything else downloads, whatever ?inline says. */
        string servedContentType = _attachmentValidator.ResolveServedContentType(attachment.ContentType);
        bool serveInline = inline && _attachmentValidator.IsInlineSafeContentType(attachment.ContentType);

        Response.Headers["X-Content-Type-Options"] = "nosniff";

        var owningSession = attachment.ChatMessage!.ChatSession!;

        /* The filename is in the encrypted set, so the Content-Disposition header needs it
           decrypted -- otherwise a confidential session's download saves as "enc:v1:...". */
        string downloadName = _contentProtection.Decrypt(owningSession, attachment.FileName) ?? "attachment";

        /* An encrypted attachment cannot be streamed with PhysicalFile: the bytes on disk are
           ciphertext. Read, decrypt, and return the plaintext from memory. Bounded by the
           attachment size cap, which A4 enforces at upload.

           A file without the magic bytes is legacy and DecryptFile returns it unchanged, so an
           upgraded session's older attachments keep working -- the reason the format is
           self-describing rather than gated on the session flag. */
        /* Recorded before the bytes go out, and once for both branches below: an attachment
           read is the single most sensitive read in the application, and it is the one F-7
           singled out as having no journal at all. */
        await RecordAccessAsync(
            ChatAccessAction.AttachmentRead,
            userId,
            chatSessionId: owningSession.Id,
            attachmentId: attachment.Id,
            sessionRef: Overseer.Services.Privacy.SessionRef.Persistent(owningSession.Id).ToWireString(),
            wasConfidential: owningSession.IsConfidential,
            detail: serveInline ? "inline" : "download");

        if (owningSession.IsConfidential)
        {
            byte[] raw = await System.IO.File.ReadAllBytesAsync(filePath);
            if (Overseer.Services.Privacy.ContentProtectionService.IsEncryptedFile(raw))
            {
                byte[] plaintext = _contentProtection.DecryptFile(owningSession, raw);
                return serveInline
                    ? File(plaintext, servedContentType)
                    : File(plaintext, servedContentType, downloadName);
            }
        }

        if (serveInline)
            return PhysicalFile(filePath, servedContentType);

        return PhysicalFile(filePath, servedContentType, downloadName);
    }

    [HttpPost("sessions/attach-snapshot")]
    public async Task<IActionResult> AttachSnapshot([FromBody] AttachGameSnapshotRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.SnapshotText))
        {
            return BadRequest(new { error = "Snapshot text must not be empty." });
        }

        string snapshotText = request.SnapshotText;
        if (snapshotText.Length > 60200)
        {
            snapshotText = snapshotText.Substring(0, 60200);
        }

        long sessionId;
        if (request.SessionId.HasValue && request.SessionId.Value > 0)
        {
            sessionId = request.SessionId.Value;
            var session = await _dbContext.ChatSession.FindAsync(sessionId);
            if (session == null || session.AspNetUserId != userId)
                return NotFound(new { error = "Session not found." });
        }
        else
        {
            var session = new ChatSession
            {
                AspNetUserId = userId,
                Title = "GnollHack Session",
                CreatedUtc = DateTime.UtcNow,
                LastMessageUtc = DateTime.UtcNow,
                IsGnollHackSession = true,
                ClientSettings = "{\"BoolData\":{\"isGameOn\":true}}"
            };
            _dbContext.ChatSession.Add(session);
            await _dbContext.SaveChangesAsync();
            sessionId = session.Id;

            await _chatRetentionService.EnforceUserSessionQuotaAsync(userId);
        }

        // Rewrite any existing snapshot system message's content to the supersession marker.
        var existingSnapshots = await _dbContext.ChatMessage
            .Where(m => m.ChatSessionId == sessionId && m.Role == "system" && m.IsGameSnapshot)
            .ToListAsync();

        foreach (var existing in existingSnapshots)
        {
            existing.Content = "[Game state snapshot superseded by the updated snapshot below]";
            // The flag is the row's current state, not the fact that it once held a snapshot.
            // Leaving it set would re-select these rows on the next attach and hand the marker
            // text to StripGameSnapshotPrefix.
            existing.IsGameSnapshot = false;
        }

        // Insert the new system message
        string normalized = DumpHtmlSanitizer.NormalizeFlattenedText(snapshotText);
        var systemMsg = new ChatMessage
        {
            ChatSessionId = sessionId,
            Role = "system",
            Content = ChatService.GameSnapshotPrefix + "\n" + normalized,
            IsGameSnapshot = true,
            TimestampUtc = DateTime.UtcNow
        };
        _dbContext.ChatMessage.Add(systemMsg);
        await _dbContext.SaveChangesAsync();

        return Ok(new { sessionId, hasGameSnapshot = true });
    }

    [HttpPost("send")]
    [EnableRateLimiting(RateLimitPolicies.Chat)]
    /* The aggregate body bound. AttachmentValidator caps each attachment and their number;
       this caps the whole request, base64 expansion included, so an oversized send is
       refused by Kestrel before the controller allocates anything. */
    [RequestSizeLimit(SendRequestBodyByteLimit)]
    public async Task<IActionResult> Send([FromBody] SendMessageRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        request.Message = System.Text.RegularExpressions.Regex.Replace(
            request.Message ?? "", @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "").Trim();

        if (string.IsNullOrEmpty(request.Message) && (request.Attachments == null || request.Attachments.Count == 0))
        {
            return BadRequest("Message cannot be empty");
        }

        if (request.IsEphemeral && !request.IsConfidential)
        {
            /* Refused rather than silently corrected. Incognito is a stricter form of
               Confidentiality Mode, so a client asking for one without the other has
               misunderstood the pair, and quietly turning confidentiality on would hide that
               from whoever has to debug it. */
            return BadRequest("An incognito session is always confidential.");
        }

        Overseer.Services.Privacy.SessionRef sessionRef;
        if (!string.IsNullOrEmpty(request.SessionId))
        {
            if (!Overseer.Services.Privacy.SessionRef.TryParse(request.SessionId, out sessionRef))
                return NotFound("Session not found");

            if (sessionRef.IsEphemeral)
            {
                if (!_ephemeralSessions.IsOwnedBy(sessionRef, userId))
                    return NotFound("Session not found");
            }
            else
            {
                var session = await _dbContext.ChatSession.FindAsync(sessionRef.PersistentId);
                if (session == null || session.AspNetUserId != userId)
                    return NotFound("Session not found");
            }
        }
        else if (request.IsEphemeral)
        {
            /* The same ChatSession shape a persisted session gets, and the same policy
               snapshot resolved by the same resolver -- it just never reaches a DbContext. That
               is what lets the whole confidential path downstream read the session's promises
               without knowing which kind of session it is holding. */
            var template = new ChatSession
            {
                AspNetUserId = userId,
                Title = "Incognito chat",
                CreatedUtc = DateTime.UtcNow,
                LastMessageUtc = DateTime.UtcNow,
                IsConfidential = true
            };

            var ephemeralSettings = await _dbContext.UserAiSettings.FindAsync(userId);
            Overseer.Services.Privacy.ConfidentialPolicyResolver.ApplyToSession(
                template, _confidentialPolicyResolver.Resolve(ephemeralSettings));

            /* Neither retention scalar means anything here: nothing is stored, so nothing
               expires on a schedule. The sliding timeout in the store is the only lifetime. */
            template.EffectiveRetentionDays = 0;
            template.ImmediatePurgeOnDelete = true;

            sessionRef = _ephemeralSessions.Create(userId, template).Ref;
        }
        else
        {
            var session = new ChatSession
            {
                AspNetUserId = userId,
                Title = request.Message.Length > 50 ? request.Message.Substring(0, 47) + "..." : request.Message,
                CreatedUtc = DateTime.UtcNow,
                LastMessageUtc = DateTime.UtcNow,
                IsConfidential = request.IsConfidential
            };

            /* The policy is snapshotted at creation, and the two retention scalars materialised
               with it, so a later change to the user's defaults or the administrator's floor
               cannot retroactively weaken what was promised about this session. */
            if (request.IsConfidential)
            {
                var confidentialSettings = await _dbContext.UserAiSettings.FindAsync(userId);
                Overseer.Services.Privacy.ConfidentialPolicyResolver.ApplyToSession(
                    session, _confidentialPolicyResolver.Resolve(confidentialSettings));
            }

            _dbContext.ChatSession.Add(session);
            await _dbContext.SaveChangesAsync();
            sessionRef = Overseer.Services.Privacy.SessionRef.Persistent(session.Id);

            await _chatRetentionService.EnforceUserSessionQuotaAsync(userId);
        }

        var settings = await _dbContext.UserAiSettings.FindAsync(userId);
        int timeoutSeconds = settings?.RequestTimeout ?? _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Default", 300);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        if (!_ongoingChatManager.TryStart(sessionRef, cts, out _))
            return Conflict("Generation already in progress for this session.");

        var message = request.Message;
        var attachments = request.Attachments;
        var userModelId = request.UserModelId;
        var systemModelId = request.SystemModelId;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var chatService = scope.ServiceProvider.GetRequiredService<ChatService>();
                await chatService.GenerateAndBroadcastMessageAsync(sessionRef, message, attachments, userId, false, cts.Token, userModelId, systemModelId, request.HasGreeted);
            }
            finally
            {
                _ongoingChatManager.Complete(sessionRef);
            }
        });

        /* A string in both cases. A persisted session still serialises as its decimal id, so
           the field means what it always did for the sessions that already existed. */
        return Ok(new { sessionId = sessionRef.ToWireString() });
    }

    [HttpPost("report")]
    public async Task<IActionResult> ReportMessage([FromBody] ReportMessageRequest request)
    {
        bool showDebugLog = _configuration.ShouldShowDebugLog(User.Identity?.Name);
        var debugLogs = new List<string>();
        if (showDebugLog) debugLogs.Add($"Starting ReportMessage for MessageId: {request.MessageId}");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) 
        {
            if (showDebugLog) debugLogs.Add("User ID not found in claims.");
            return Unauthorized();
        }
        if (showDebugLog) debugLogs.Add($"User ID: {userId}");

        // Rate limiting: max 10 per day
        string cacheKey = $"ReportMessageCount_{userId}";
        if (!_memoryCache.TryGetValue(cacheKey, out int reportCount))
        {
            reportCount = 0;
        }

        if (reportCount >= 20)
        {
            return BadRequest("You have reached the maximum number of reports for today.");
        }

        var message = await _dbContext.ChatMessage
            .Include(m => m.ChatSession)
            .FirstOrDefaultAsync(m => m.Id == request.MessageId);

        if (message == null || message.ChatSession?.AspNetUserId != userId)
        {
            return NotFound("Message not found.");
        }

        /* Reporting emails the whole transcript plus the reporter's name and address to an
           operator mailbox. That is a deliberate egress of exactly what a confidential session
           promises not to send anywhere, so the mode refuses it outright rather than sending a
           redacted version nobody could act on. */
        if (message.ChatSession.IsConfidential)
        {
            return BadRequest(new
            {
                message = "A message in a confidential chat cannot be reported: reporting sends the whole "
                    + "conversation to an operator mailbox. Copy just the part you want to report into a "
                    + "normal chat, or describe it in a support message."
            });
        }

        var user = await _userManager.FindByIdAsync(userId);
        var userName = user?.UserName ?? "Unknown";
        var userEmail = user?.Email ?? "Unknown";

        var allMessages = await _dbContext.ChatMessage
            .Where(m => m.ChatSessionId == message.ChatSessionId)
            .OrderBy(m => m.TimestampUtc)
            .ToListAsync();

        var mdBuilder = new StringBuilder();
        mdBuilder.AppendLine($"# Conversation: {message.ChatSession?.Title ?? "Untitled"}");
        mdBuilder.AppendLine($"**Session ID:** {message.ChatSessionId}");
        mdBuilder.AppendLine();

        bool hasMoreMessages = false;
        foreach (var m in allMessages)
        {
            string roleName = m.Role != null && m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "OVERSEER" : (m.Role?.ToUpper() ?? "");
            string hiddenText = m.IsHidden ? " (HIDDEN)" : "";
            string modelDetails = "";
            if (!string.IsNullOrEmpty(m.ModelUsed))
            {
                var extra = new List<string>();
                if (!string.IsNullOrEmpty(m.ModelDisplayNameUsed)) extra.Add($"display: {m.ModelDisplayNameUsed}");
                if (!string.IsNullOrEmpty(m.ThinkingLevelUsed)) extra.Add($"thinking: {m.ThinkingLevelUsed}");
                if (!string.IsNullOrEmpty(m.ReasoningModeUsed)) extra.Add($"reasoning: {m.ReasoningModeUsed}");
                if (!string.IsNullOrEmpty(m.ServiceTierUsed)) extra.Add($"service tier: {m.ServiceTierUsed}");
                if (!string.IsNullOrEmpty(m.ActualServiceTierUsed) && !m.ActualServiceTierUsed.Equals(m.ServiceTierUsed, StringComparison.OrdinalIgnoreCase))
                {
                    extra.Add($"service tier served: {m.ActualServiceTierUsed}");
                }
                modelDetails = $" [{m.ModelUsed}" + (extra.Count > 0 ? $" ({string.Join(", ", extra)})" : "") + "]";
            }
            mdBuilder.AppendLine($"### {roleName}{hiddenText}{modelDetails} ({m.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC)");
            mdBuilder.AppendLine(m.Content);
            mdBuilder.AppendLine();
            mdBuilder.AppendLine("---");
            mdBuilder.AppendLine();

            if (m.Id == message.Id)
            {
                if (m != allMessages.Last())
                {
                    hasMoreMessages = true;
                }
                break;
            }
        }

        if (hasMoreMessages)
        {
            mdBuilder.AppendLine("## NOTE");
            mdBuilder.AppendLine("There are more messages in the chat after the reported message, but they are not included in this file.");
            mdBuilder.AppendLine();
        }

        string toAddress = _configuration["ReportEmailAddress"] ?? "gnollhack@hyvanmielenpelit.fi";

        string htmlBody = $@"
            <html>
            <body>
                <h2>Context</h2>
                <p><strong>Message ID:</strong> {message.Id}</p>
                <p><strong>User Name:</strong> {userName}</p>
                <p><strong>User Email:</strong> {userEmail}</p>
                <p><strong>Conversation Timestamp:</strong> {message.ChatSession?.CreatedUtc:yyyy-MM-dd HH:mm:ss} UTC</p>
                <p><strong>Message Timestamp:</strong> {message.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC</p>
                <p><strong>Session ID:</strong> {message.ChatSessionId}</p>
                <p><strong>Initiated by GnollHack:</strong> {(message.ChatSession?.Title?.StartsWith("GnollHack") == true ? "Yes" : "No")}</p>

                <h2>Reported Message</h2>
                <div style='background-color: #f4f4f4; padding: 10px; border-left: 4px solid #ccc;'>
                    {System.Net.WebUtility.HtmlEncode(message.Content ?? "").Replace("\n", "<br/>")}
                </div>

                <h2>Debug Data</h2>
                <p><strong>Role:</strong> {message.Role}</p>
                <p><strong>Provider:</strong> {message.ProviderUsed ?? "N/A"}</p>
                <p><strong>Model:</strong> {message.ModelUsed ?? "N/A"}</p>
                <p><strong>Model Display Name:</strong> {message.ModelDisplayNameUsed ?? "N/A"}</p>
                <p><strong>Thinking Level:</strong> {message.ThinkingLevelUsed ?? "N/A"}</p>
                <p><strong>Reasoning Mode:</strong> {message.ReasoningModeUsed ?? "N/A"}</p>
                <p><strong>Service Tier:</strong> {message.ServiceTierUsed ?? "N/A"} (served: {message.ActualServiceTierUsed ?? "not reported"})</p>
                <p><strong>Time to First Token (ms):</strong> {message.TimeToFirstTokenMs?.ToString() ?? "N/A"}</p>
                <p><strong>Total Duration (ms):</strong> {message.TotalDurationMs?.ToString() ?? "N/A"}</p>
                <p><strong>Context Prompt / Output Tokens:</strong> {message.ContextPromptTokens?.ToString() ?? "N/A"} / {message.ContextOutputTokens?.ToString() ?? "N/A"}</p>
                <p><strong>Context Window / Input Limit:</strong> {message.ContextWindowTokens?.ToString() ?? "N/A"} / {message.ContextInputLimitTokens?.ToString() ?? "N/A"}</p>
            </body>
            </html>
        ";

        var emailContent = new EmailContent("Overseer Message Report")
        {
            Html = htmlBody
        };

        var attachmentContent = BinaryData.FromString(mdBuilder.ToString());
        var attachment = new EmailAttachment("conversation.md", "text/markdown", attachmentContent);
        var emailMessage = new EmailMessage("donotreply@gnollhack.com", toAddress, emailContent);
        emailMessage.Attachments.Add(attachment);

        try
        {
            if (showDebugLog) debugLogs.Add($"Sending email to {toAddress}...");
            await _emailSender.SendAsync(Azure.WaitUntil.Started, emailMessage);
            if (showDebugLog) debugLogs.Add("Email SendAsync completed successfully.");

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromDays(1))
                .SetSize(1);
            _memoryCache.Set(cacheKey, reportCount + 1, cacheEntryOptions);
            return Ok(showDebugLog ? (object)new { debugLogs } : new { });
        }
        catch (Exception ex)
        {
            if (showDebugLog) debugLogs.Add($"Exception in SendAsync: {ex}");
            if (showDebugLog) return StatusCode(500, new { message = $"Failed to send report email: {ex.Message}", debugLogs });
            else return StatusCode(500, new { message = $"Failed to send report email: {ex.Message}" });
        }
    }

    [HttpGet("subagents")]
    public IActionResult GetSubAgents()
    {
        var agents = _subAgentCatalogService.GetEnabledSubAgents()
            .Select(a => new
            {
                name = a.Name,
                displayName = a.DisplayName,
                description = a.Description,
                allowedTools = a.AllowedTools,
                maxIterations = a.MaxIterations,
                isEnabled = a.IsEnabled
            });
        return Ok(agents);
    }

    [HttpPost("sessions/{sessionId}/subagents/{toolCallId}/cancel")]
    public async Task<IActionResult> CancelSubAgent(string sessionId, string toolCallId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!Overseer.Services.Privacy.SessionRef.TryParse(sessionId, out var sessionRef))
            return NotFound(new { error = "Session not found." });
        if (!await OwnsSessionAsync(sessionRef, userId))
            return NotFound(new { error = "Session not found." });

        bool canceled = _ongoingChatManager.TryCancelSubAgent(sessionRef, toolCallId);
        if (canceled)
        {
            return Ok(new { success = true, message = "Subagent canceled." });
        }

        return Ok(new { success = false, message = "Subagent not active or already finished." });
    }

    [HttpPost("sessions/{sessionId}/cancel")]
    public async Task<IActionResult> CancelGeneration(string sessionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!Overseer.Services.Privacy.SessionRef.TryParse(sessionId, out var sessionRef))
            return NotFound(new { error = "Session not found." });
        if (!await OwnsSessionAsync(sessionRef, userId))
            return NotFound(new { error = "Session not found." });

        bool canceled = _ongoingChatManager.TryCancelAndRemove(sessionRef);
        return Ok(new { success = canceled });
    }

    /// <summary>
    /// Records an access to a conversation or an attachment.
    /// </summary>
    /// <remarks>
    /// Called after the authorisation checks and after the data has been assembled, so a refused
    /// request writes nothing: a journal of attempts is a different feature with a different
    /// noise profile, and conflating the two makes both harder to read. A failed write is logged
    /// and never fails the request — the reasoning is in <c>ChatAccessAudit</c>.
    /// </remarks>
    private async Task RecordAccessAsync(
        ChatAccessAction action,
        string userId,
        long? chatSessionId = null,
        long? attachmentId = null,
        string? sessionRef = null,
        bool wasConfidential = false,
        string? detail = null)
    {
        bool written = await GnollHackServer.Data.Privacy.ChatAccessAudit.RecordAsync(
            _dbContext,
            action,
            actorUserId: userId,
            actorUserName: User.Identity?.Name,
            actorWasAdmin: _configuration.IsAdmin(User.Identity?.Name),
            subjectUserId: userId,
            chatSessionId: chatSessionId,
            attachmentId: attachmentId,
            sessionRef: sessionRef,
            wasConfidential: wasConfidential,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            detail: detail);

        if (!written)
        {
            _logger?.LogWarning(
                "Could not record a {Action} audit entry for session {SessionRef}.", action, sessionRef);
        }
    }

    /// <summary>
    /// Whether this user owns the session a reference names, whichever store holds it. The
    /// counterpart of the same check in <c>ChatHub</c>.
    /// </summary>
    private async Task<bool> OwnsSessionAsync(Overseer.Services.Privacy.SessionRef sessionRef, string userId)
    {
        if (sessionRef.IsEphemeral) return _ephemeralSessions.IsOwnedBy(sessionRef, userId);
        if (!sessionRef.IsPersistent) return false;

        return await _dbContext.ChatSession
            .AnyAsync(s => s.Id == sessionRef.PersistentId && s.AspNetUserId == userId);
    }
}

public class ReportMessageRequest
{
    public long MessageId { get; set; }
}

public class SendMessageAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Base64Data { get; set; } = string.Empty;
}

public class SendMessageRequest
{
    /// <summary>
    /// The session to continue, in <see cref="Overseer.Services.Privacy.SessionRef"/> wire form, or null to
    /// start a new one.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(Overseer.Services.Privacy.SessionRef.LenientJsonConverter))]
    public string? SessionId { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<SendMessageAttachment>? Attachments { get; set; }
    
    public long? UserModelId { get; set; }
    public long? SystemModelId { get; set; }
    public bool HasGreeted { get; set; }

    /// <summary>
    /// Creates the session in Confidentiality Mode. Only meaningful when no session id is
    /// supplied — an existing session is upgraded through the confidential endpoint, which is
    /// where the one-way latch lives.
    /// </summary>
    public bool IsConfidential { get; set; }

    /// <summary>
    /// Creates the session in RAM only — no session, message, tool-call or attachment row, and
    /// no file. Implies <see cref="IsConfidential"/>, and like it is meaningful only when no
    /// session id is supplied.
    /// </summary>
    /// <remarks>
    /// There is no upgrade path to this, and there cannot be: an existing session's rows are
    /// already written, so "make this chat ephemeral" could only ever mean "delete it and start
    /// again". The decision belongs at creation.
    /// </remarks>
    public bool IsEphemeral { get; set; }
}

public class AttachGameSnapshotRequest
{
    public long? SessionId { get; set; }
    public string SnapshotText { get; set; } = string.Empty;
    public string? SourceGnollHackVersion { get; set; }
}

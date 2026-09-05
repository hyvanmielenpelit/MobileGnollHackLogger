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
        Overseer.Services.Agents.SubAgentCatalogService subAgentCatalogService)
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
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] int skip = 0, [FromQuery] int? take = null, [FromQuery] string? search = null)
    {
        var swTotal = Stopwatch.StartNew();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

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
            query = query.Where(s =>
                (s.Title != null && EF.Functions.Like(s.Title, $"%{term}%")) ||
                _dbContext.ChatMessage.Any(m => m.ChatSessionId == s.Id && !m.IsHidden && m.Role != "system" && m.Content != null && EF.Functions.Like(m.Content, $"%{term}%"))
            );
        }

        var sessions = await query
            .OrderByDescending(s => s.IsPinned)
            .ThenByDescending(s => s.LastMessageUtc)
            .Skip(skip)
            .Take(effectiveTake + 1)
            .Select(s => new { s.Id, s.Title, s.LastMessageUtc, s.IsGnollHackSession, s.IsPinned })
            .ToListAsync();
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
            maxPinned = _configuration.GetValue<int>("ChatRetentionSettings:MaxPinnedSessionsPerUser", 5)
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
            
        session.Title = request.Title.Trim();
        await _dbContext.SaveChangesAsync();
        
        return Ok();
    }

    [HttpGet("sessions/{id}")]
    public async Task<IActionResult> GetSession(long id)
    {
        var swTotal = Stopwatch.StartNew();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

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
                m.CostCurrency
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

        var swAsm = Stopwatch.StartNew();
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

            var msgAttachments = attachmentsLookup[m.Id]
                .Select(a => new { a.Id, a.FileName, a.ContentType })
                .ToList();

            var msgToolCalls = toolCallsLookup[m.Id]
                .Select(tc => new {
                    tc.id,
                    tc.name,
                    tc.displayName,
                    tc.argsText,
                    tc.status,
                    tc.result,
                    tc.error,
                    tc.agentName,
                    tc.parentToolCallId,
                    tc.depth
                })
                .ToList();

            return new {
                m.Id,
                m.Role,
                m.Content,
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
                m.EstimatedCost,
                m.PricingSource,
                m.CostCurrency
            };
        }).ToList();
        swAsm.Stop();
        var assemblyMs = swAsm.ElapsedMilliseconds;

        var ongoing = _ongoingChatManager.TryGet(id);
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

        string p0 = ChatService.GameSnapshotLikePatterns[0];
        string p1 = ChatService.GameSnapshotLikePatterns[1];
        // Note: SQL Server's default collation is case-insensitive, matching
        // ChatService.IsGameSnapshotMessage's OrdinalIgnoreCase check.
        // A case-sensitive collation would silently diverge.
        bool hasGameSnapshot = await _dbContext.ChatMessage
            .AnyAsync(m => m.ChatSessionId == id && m.Role == "system" &&
                (EF.Functions.Like(m.Content, p0) || EF.Functions.Like(m.Content, p1)));

        return Ok(new
        {
            session.Id,
            session.Title,
            session.IsGnollHackSession,
            hasGameSnapshot,
            Messages = formattedMessages,
            HasOngoingGeneration = hasOngoing,
            OngoingGeneration = ongoingData,
            LastEventSeqNo = lastEventSeqNo
        });
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
            query = query.Where(s =>
                (s.Title != null && EF.Functions.Like(s.Title, $"%{term}%")) ||
                _dbContext.ChatMessage.Any(m => m.ChatSessionId == s.Id && !m.IsHidden && m.Role != "system" && m.Content != null && EF.Functions.Like(m.Content, $"%{term}%"))
            );
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

        return Ok(sessions);
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
            
        if (inline)
            return PhysicalFile(filePath, attachment.ContentType ?? "application/octet-stream");
        else
            return PhysicalFile(filePath, attachment.ContentType ?? "application/octet-stream", attachment.FileName);
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
        string p0 = ChatService.GameSnapshotLikePatterns[0];
        string p1 = ChatService.GameSnapshotLikePatterns[1];
        var existingSnapshots = await _dbContext.ChatMessage
            .Where(m => m.ChatSessionId == sessionId && m.Role == "system" &&
                (EF.Functions.Like(m.Content, p0) || EF.Functions.Like(m.Content, p1)))
            .ToListAsync();

        foreach (var existing in existingSnapshots)
        {
            existing.Content = "[Game state snapshot superseded by the updated snapshot below]";
        }

        // Insert the new system message
        string normalized = DumpHtmlSanitizer.NormalizeFlattenedText(snapshotText);
        var systemMsg = new ChatMessage
        {
            ChatSessionId = sessionId,
            Role = "system",
            Content = ChatService.GameSnapshotPrefix + "\n" + normalized,
            TimestampUtc = DateTime.UtcNow
        };
        _dbContext.ChatMessage.Add(systemMsg);
        await _dbContext.SaveChangesAsync();

        return Ok(new { sessionId, hasGameSnapshot = true });
    }

    [HttpPost("send")]
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

        long sessionId;
        if (request.SessionId.HasValue && request.SessionId.Value > 0)
        {
            sessionId = request.SessionId.Value;
            var session = await _dbContext.ChatSession.FindAsync(sessionId);
            if (session == null || session.AspNetUserId != userId)
                return NotFound("Session not found");
        }
        else
        {
            var session = new ChatSession
            {
                AspNetUserId = userId,
                Title = request.Message.Length > 50 ? request.Message.Substring(0, 47) + "..." : request.Message,
                CreatedUtc = DateTime.UtcNow,
                LastMessageUtc = DateTime.UtcNow
            };
            _dbContext.ChatSession.Add(session);
            await _dbContext.SaveChangesAsync();
            sessionId = session.Id;

            await _chatRetentionService.EnforceUserSessionQuotaAsync(userId);
        }

        var settings = await _dbContext.UserAiSettings.FindAsync(userId);
        int timeoutSeconds = settings?.RequestTimeout ?? _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Default", 300);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        if (!_ongoingChatManager.TryStart(sessionId, cts, out _))
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
                await chatService.GenerateAndBroadcastMessageAsync(sessionId, message, attachments, userId, false, cts.Token, userModelId, systemModelId, request.HasGreeted);
            }
            finally
            {
                _ongoingChatManager.Complete(sessionId);
            }
        });

        return Ok(new { sessionId = sessionId });
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
    public async Task<IActionResult> CancelSubAgent(long sessionId, string toolCallId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var session = await _dbContext.ChatSession.FirstOrDefaultAsync(s => s.Id == sessionId && s.AspNetUserId == userId);
        if (session == null) return NotFound(new { error = "Session not found." });

        bool canceled = _ongoingChatManager.TryCancelSubAgent(sessionId, toolCallId);
        if (canceled)
        {
            return Ok(new { success = true, message = "Subagent canceled." });
        }

        return Ok(new { success = false, message = "Subagent not active or already finished." });
    }

    [HttpPost("sessions/{sessionId}/cancel")]
    public async Task<IActionResult> CancelGeneration(long sessionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var session = await _dbContext.ChatSession.FirstOrDefaultAsync(s => s.Id == sessionId && s.AspNetUserId == userId);
        if (session == null) return NotFound(new { error = "Session not found." });

        bool canceled = _ongoingChatManager.TryCancelAndRemove(sessionId);
        return Ok(new { success = canceled });
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
    public long? SessionId { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<SendMessageAttachment>? Attachments { get; set; }
    
    public long? UserModelId { get; set; }
    public long? SystemModelId { get; set; }
    public bool HasGreeted { get; set; }
}

public class AttachGameSnapshotRequest
{
    public long? SessionId { get; set; }
    public string SnapshotText { get; set; } = string.Empty;
    public string? SourceGnollHackVersion { get; set; }
}

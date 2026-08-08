using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Extensions;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using System.IO;
using Microsoft.Extensions.Caching.Memory;
using GnollHackServer.Data;
using Azure.Communication.Email;
using System.Text;
using Microsoft.AspNetCore.Identity;

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

    public ChatController(ApplicationDbContext dbContext, ChatService chatService, IConfiguration configuration, IMemoryCache memoryCache, EmailSender emailSender, UserManager<ApplicationUser> userManager, OngoingChatManager ongoingChatManager, IServiceScopeFactory scopeFactory)
    {
        _dbContext = dbContext;
        _chatService = chatService;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _emailSender = emailSender;
        _userManager = userManager;
        _ongoingChatManager = ongoingChatManager;
        _scopeFactory = scopeFactory;
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        int pageSize = _configuration.GetValue<int>("ConversationListPageSize", 20);
        int effectiveTake = Math.Min(Math.Max(take ?? pageSize, pageSize), 500);

        var sessions = await _dbContext.ChatSession
            .Where(s => s.AspNetUserId == userId)
            .OrderByDescending(s => s.LastMessageUtc)
            .Skip(skip)
            .Take(effectiveTake)
            .Select(s => new { s.Id, s.Title, s.LastMessageUtc, s.IsGnollHackSession })
            .ToListAsync();

        var totalCount = await _dbContext.ChatSession.CountAsync(s => s.AspNetUserId == userId);

        return Ok(new
        {
            sessions = sessions,
            hasMore = (skip + sessions.Count) < totalCount
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
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var session = await _dbContext.ChatSession
            .FirstOrDefaultAsync(s => s.Id == id && s.AspNetUserId == userId);

        if (session == null) return NotFound();
        
        var userModels = await _dbContext.UserAiModels
            .Where(um => um.AspNetUserId == userId)
            .ToListAsync();

        var messages = await _dbContext.ChatMessage
            .Where(m => m.ChatSessionId == id && m.Role != "system" && !m.IsHidden)
            .OrderBy(m => m.TimestampUtc)
            .Select(m => new { 
                m.Id, 
                m.Role, 
                m.Content, 
                m.TimestampUtc,
                m.TimeToFirstTokenMs,
                m.ProviderUsed,
                m.ModelUsed,
                Attachments = _dbContext.ChatMessageAttachment
                    .Where(a => a.ChatMessageId == m.Id)
                    .Select(a => new { a.Id, a.FileName, a.ContentType })
                    .ToList(),
                ToolCalls = _dbContext.ChatMessageToolCall
                    .Where(tc => tc.ChatMessageId == m.Id)
                    .OrderBy(tc => tc.SortOrder)
                    .Select(tc => new {
                        id = tc.ToolCallId,
                        name = tc.Name,
                        displayName = tc.DisplayName,
                        argsText = tc.ArgsText,
                        status = tc.Status,
                        result = tc.Result,
                        error = tc.Error
                    })
                    .ToList()
            })
            .ToListAsync();

        var formattedMessages = messages.Select(m => {
            string? modelDisplayName = null;
            string? thinkingLevel = null;
            if (m.Role == "assistant" && !string.IsNullOrEmpty(m.ModelUsed)) {
                var um = userModels.FirstOrDefault(x => x.ModelId == m.ModelUsed);
                if (um != null) {
                    if (!string.IsNullOrEmpty(um.DisplayName)) {
                        modelDisplayName = um.DisplayName;
                    } else {
                        modelDisplayName = m.ModelUsed;
                    }
                    thinkingLevel = um.ThinkingLevel;
                } else {
                    modelDisplayName = m.ModelUsed;
                }
            }
            return new {
                m.Id,
                m.Role,
                m.Content,
                m.TimestampUtc,
                m.TimeToFirstTokenMs,
                m.Attachments,
                m.ToolCalls,
                ModelDisplayName = modelDisplayName,
                ThinkingLevel = thinkingLevel
            };
        }).ToList();

        var ongoing = _ongoingChatManager.TryGet(id);
        bool showDebugLog = _configuration.ShouldShowDebugLog(User.Identity?.Name);
        
        return Ok(new
        {
            session.Id,
            session.Title,
            session.IsGnollHackSession,
            Messages = formattedMessages,
            HasOngoingGeneration = ongoing != null,
            OngoingGeneration = ongoing != null ? new {
                events = ongoing.AccumulatedEvents
                    .Where(e => showDebugLog || e.Type != "debug")
                    .Select(e => new { type = e.Type, data = e.Data }).ToList()
            } : null
        });
    }

    [HttpDelete("sessions/{id}")]
    public async Task<IActionResult> DeleteSession(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var session = await _dbContext.ChatSession.FirstOrDefaultAsync(s => s.Id == id && s.AspNetUserId == userId);
        
        if (session == null) return NotFound();

        var baseDir = _configuration["ConversationsDataLocation"];
        if (!string.IsNullOrEmpty(baseDir))
        {
            var attachmentPaths = await _dbContext.ChatMessageAttachment
                .Where(a => a.ChatMessage != null && a.ChatMessage.ChatSessionId == id)
                .Select(a => a.RelativePath)
                .ToListAsync();

            foreach (var path in attachmentPaths)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    var filePath = Path.Combine(baseDir, path);
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }
            }
        }

        _dbContext.ChatSession.Remove(session);
        await _dbContext.SaveChangesAsync();

        return Ok();
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
            mdBuilder.AppendLine($"### {roleName}{hiddenText} ({m.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC)");
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

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;
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

    public ChatController(ApplicationDbContext dbContext, ChatService chatService, IConfiguration configuration, IMemoryCache memoryCache, EmailSender emailSender, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _chatService = chatService;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _emailSender = emailSender;
        _userManager = userManager;
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var sessions = await _dbContext.ChatSession
            .Where(s => s.AspNetUserId == userId)
            .OrderByDescending(s => s.LastMessageUtc)
            .Select(s => new { s.Id, s.Title, s.LastMessageUtc })
            .ToListAsync();

        return Ok(sessions);
    }

    [HttpGet("sessions/{id}")]
    public async Task<IActionResult> GetSession(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var session = await _dbContext.ChatSession
            .FirstOrDefaultAsync(s => s.Id == id && s.AspNetUserId == userId);

        if (session == null) return NotFound();
        
        var messages = await _dbContext.ChatMessage
            .Where(m => m.ChatSessionId == id && m.Role != "system" && !m.IsHidden)
            .OrderBy(m => m.TimestampUtc)
            .Select(m => new { 
                m.Id, 
                m.Role, 
                m.Content, 
                m.TimestampUtc,
                Attachments = _dbContext.ChatMessageAttachment
                    .Where(a => a.ChatMessageId == m.Id)
                    .Select(a => new { a.Id, a.FileName, a.ContentType })
                    .ToList()
            })
            .ToListAsync();

        return Ok(new
        {
            session.Id,
            session.Title,
            Messages = messages
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
            .ThenInclude(m => m.ChatSession)
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
    public async Task Send([FromBody] SendMessageRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        
        try 
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                Response.StatusCode = 401;
                return;
            }
            await foreach (var chatEvent in _chatService.StreamMessageAsync(request.SessionId, request.Message, request.Attachments, userId, false, cancellationToken))
            {
                if (chatEvent.Type == "chunk")
                {
                    var formattedChunk = chatEvent.Data.Replace("\n", "\ndata: ");
                    await Response.WriteAsync($"data: {formattedChunk}\n\n", cancellationToken);
                }
                else
                {
                    var formattedData = chatEvent.Data.Replace("\n", " ");
                    await Response.WriteAsync($"event: {chatEvent.Type}\ndata: {formattedData}\n\n", cancellationToken);
                }
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Send SSE error event so the client doesn't hang
            var errorMessage = ex.Message.Replace("\n", " ");
            await Response.WriteAsync($"event: error\ndata: {errorMessage}\n\n");
            await Response.Body.FlushAsync();
        }
    }

    [HttpPost("report")]
    public async Task<IActionResult> ReportMessage([FromBody] ReportMessageRequest request)
    {
        var debugLogs = new List<string>();
        debugLogs.Add($"Starting ReportMessage for MessageId: {request.MessageId}");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) 
        {
            debugLogs.Add("User ID not found in claims.");
            return Unauthorized();
        }
        debugLogs.Add($"User ID: {userId}");

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
            string roleName = m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "OVERSEER" : m.Role.ToUpper();
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
                    {System.Net.WebUtility.HtmlEncode(message.Content).Replace("\n", "<br/>")}
                </div>

                <h2>Debug Data</h2>
                <p><strong>Role:</strong> {message.Role}</p>
            </body>
            </html>
        ";

        var emailMessage = new EmailMessage(
            senderAddress: "DoNotReply@gnollhack.com",
            recipientAddress: toAddress,
            content: new EmailContent("Overseer Message Report")
            {
                Html = htmlBody
            });

        var attachmentContent = BinaryData.FromString(mdBuilder.ToString());
        var attachment = new EmailAttachment("conversation.md", "text/markdown", attachmentContent);
        emailMessage.Attachments.Add(attachment);

        try
        {
            debugLogs.Add($"Sending email to {toAddress}...");
            await _emailSender.SendAsync(Azure.WaitUntil.Started, emailMessage);
            debugLogs.Add("Email SendAsync completed successfully.");

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromDays(1))
                .SetSize(1);
            _memoryCache.Set(cacheKey, reportCount + 1, cacheEntryOptions);
            return Ok(new { debugLogs });
        }
        catch (Exception ex)
        {
            debugLogs.Add($"Exception in SendAsync: {ex}");
            return StatusCode(500, new { message = $"Failed to send report email: {ex.Message}", debugLogs });
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
}

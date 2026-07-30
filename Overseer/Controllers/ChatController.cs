using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ChatService _chatService;
    private readonly IConfiguration _configuration;

    public ChatController(ApplicationDbContext dbContext, ChatService chatService, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _chatService = chatService;
        _configuration = configuration;
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
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
        var session = await _dbContext.ChatSession
            .FirstOrDefaultAsync(s => s.Id == id && s.AspNetUserId == userId);

        if (session == null) return NotFound();
        
        var messages = await _dbContext.ChatMessage
            .Where(m => m.ChatSessionId == id)
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
    public async Task<IActionResult> GetAttachment(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
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
            
        return PhysicalFile(filePath, attachment.ContentType ?? "application/octet-stream");
    }

    [HttpPost("send")]
    public async Task Send([FromBody] SendMessageRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        
        try 
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            await foreach (var chatEvent in _chatService.StreamMessageAsync(request.SessionId, request.Message, request.Attachments, userId, cancellationToken))
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

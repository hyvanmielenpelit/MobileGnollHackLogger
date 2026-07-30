using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using System.Security.Claims;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ChatService _chatService;

    public ChatController(ApplicationDbContext dbContext, ChatService chatService)
    {
        _dbContext = dbContext;
        _chatService = chatService;
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
            .Where(m => m.ChatSessionId == session.Id)
            .OrderBy(m => m.TimestampUtc)
            .Select(m => new { m.Role, m.Content, m.TimestampUtc })
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

        _dbContext.ChatSession.Remove(session);
        await _dbContext.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("send")]
    public async Task Send([FromBody] SendMessageRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        
        try 
        {
            await foreach (var chunk in _chatService.StreamMessageAsync(request.SessionId, request.Message, User, cancellationToken))
            {
                var formattedChunk = chunk.Replace("\n", "\ndata: ");
                await Response.WriteAsync($"data: {formattedChunk}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Send SSE error event so the client doesn't hang
            var errorMessage = ex.Message.Replace("\n", " ");
            await Response.WriteAsync($"event: error\ndata: {{\"message\": \"{errorMessage}\"}}\n\n");
            await Response.Body.FlushAsync();
        }
    }
}

public class SendMessageRequest
{
    public long? SessionId { get; set; }
    public string Message { get; set; } = string.Empty;
}

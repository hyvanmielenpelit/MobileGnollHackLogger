using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

    public SessionController(SignInManager<ApplicationUser> signInManager, ApplicationDbContext dbContext, IMemoryCache cache, IConfiguration configuration, IServiceScopeFactory scopeFactory)
    {
        _signInManager = signInManager;
        _dbContext = dbContext;
        _cache = cache;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
    }

    [HttpPost("create")]
    [IgnoreAntiforgeryToken] // CRITICAL: Called by a native client without standard CSRF tokens
    public async Task<IActionResult> Create([FromForm] CreateSessionRequest request)
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

        var user = await _signInManager.UserManager.FindByNameAsync(request.UserName);
        if (user == null)
        {
            return Unauthorized();
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Unauthorized();
        }

        var session = new ChatSession
        {
            AspNetUserId = user.Id,
            Title = "MAUI Client Handoff",
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow,
            ClientSettings = request.OverseerSettings
        };
        _dbContext.ChatSession.Add(session);
        await _dbContext.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(request.SnapshotHtml))
        {
            var systemMsg = new ChatMessage
            {
                ChatSessionId = session.Id,
                Role = "system",
                Content = "Game Context Snapshot:\n" + request.SnapshotHtml,
                TimestampUtc = DateTime.UtcNow
            };
            _dbContext.ChatMessage.Add(systemMsg);
            await _dbContext.SaveChangesAsync();
        }

        // Save additional context channels to disk and/or as system messages
        var baseDir = _configuration["ConversationsDataLocation"];
        if (!string.IsNullOrEmpty(baseDir))
        {
            var sessionDir = Path.Combine(baseDir, session.Id.ToString());
            if (!Directory.Exists(sessionDir))
                Directory.CreateDirectory(sessionDir);

            if (!string.IsNullOrWhiteSpace(request.MessageHistory))
            {
                // Save full history to disk (can be very large)
                await System.IO.File.WriteAllTextAsync(
                    Path.Combine(sessionDir, "message_history.txt"),
                    request.MessageHistory);

                // Insert a truncated summary as a system message so the AI knows it's available
                var preview = request.MessageHistory.Length > 2000
                    ? request.MessageHistory.Substring(0, 2000) + "\n\n[... truncated — full history available in session files ...]"
                    : request.MessageHistory;

                var msg = new ChatMessage
                {
                    ChatSessionId = session.Id,
                    Role = "system",
                    Content = "Full Message History (last messages shown):\n" + preview,
                    TimestampUtc = DateTime.UtcNow
                };
                _dbContext.ChatMessage.Add(msg);
                
                // IMPORTANT: Register as an attachment so it gets deleted when the session is deleted
                _dbContext.ChatMessageAttachment.Add(new ChatMessageAttachment
                {
                    ChatMessage = msg,
                    FileName = "message_history.txt",
                    ContentType = "text/plain",
                    RelativePath = Path.Combine(session.Id.ToString(), "message_history.txt")
                });
            }

            if (!string.IsNullOrWhiteSpace(request.DirectoryManifest))
            {
                await System.IO.File.WriteAllTextAsync(
                    Path.Combine(sessionDir, "directory_manifest.txt"),
                    request.DirectoryManifest);

                var msg = new ChatMessage
                {
                    ChatSessionId = session.Id,
                    Role = "system",
                    Content = "Game Directory Manifest:\n" + request.DirectoryManifest,
                    TimestampUtc = DateTime.UtcNow
                };
                _dbContext.ChatMessage.Add(msg);

                _dbContext.ChatMessageAttachment.Add(new ChatMessageAttachment
                {
                    ChatMessage = msg,
                    FileName = "directory_manifest.txt",
                    ContentType = "text/plain",
                    RelativePath = Path.Combine(session.Id.ToString(), "directory_manifest.txt")
                });
            }

            if (!string.IsNullOrWhiteSpace(request.DebugData))
            {
                await System.IO.File.WriteAllTextAsync(
                    Path.Combine(sessionDir, "debug_data.json"),
                    request.DebugData);

                var msg = new ChatMessage
                {
                    ChatSessionId = session.Id,
                    Role = "system",
                    Content = "Developer Debug Data:\n" + request.DebugData,
                    TimestampUtc = DateTime.UtcNow
                };
                _dbContext.ChatMessage.Add(msg);

                _dbContext.ChatMessageAttachment.Add(new ChatMessageAttachment
                {
                    ChatMessage = msg,
                    FileName = "debug_data.json",
                    ContentType = "application/json",
                    RelativePath = Path.Combine(session.Id.ToString(), "debug_data.json")
                });
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

        if (!string.IsNullOrWhiteSpace(request.InitialPrompt))
        {
            var userId = user.Id;
            var sessionId = session.Id;
            var initialPrompt = request.InitialPrompt;

            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var chatService = scope.ServiceProvider.GetRequiredService<ChatService>();
                await chatService.GenerateAndBroadcastMessageAsync(sessionId, initialPrompt, null, userId, CancellationToken.None);
            });
        }

        return Ok(new
        {
            sessionId = session.Id,
            handoffToken = token
        });
    }
}

public class CreateSessionRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AntiForgeryToken { get; set; } = string.Empty;
    public string? SnapshotHtml { get; set; }
    public string? MessageHistory { get; set; }        // NEW: Full 16384-message history (plain text)
    public string? DirectoryManifest { get; set; }     // NEW: Game directory file listing (tab-separated)
    public string? DebugData { get; set; }             // NEW: Runtime debug JSON (only when DeveloperMode is on)
    public string? InitialPrompt { get; set; }         // NEW: Prompt to immediately start AI generation
    public string? OverseerSettings { get; set; }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MobileGnollHackLogger.Data;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;

    public SessionController(SignInManager<ApplicationUser> signInManager, ApplicationDbContext dbContext, IMemoryCache cache, IConfiguration configuration)
    {
        _signInManager = signInManager;
        _dbContext = dbContext;
        _cache = cache;
        _configuration = configuration;
    }

    [HttpPost("create")]
    [IgnoreAntiforgeryToken] // CRITICAL: Called by a native client without standard CSRF tokens
    public async Task<IActionResult> Create([FromForm] CreateSessionRequest request)
    {
        var expectedSecret = _configuration["MauiClientSecret"];
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
            LastMessageUtc = DateTime.UtcNow
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

        var token = Guid.NewGuid().ToString("N");
        var cacheKey = $"handoff_{token}";
        _cache.Set(cacheKey, new HandoffData { UserId = user.Id, SessionId = session.Id }, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
        });

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
}

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
    private readonly OngoingChatManager _ongoingChatManager;
    private readonly ChatRetentionService _chatRetentionService;
    private readonly Overseer.Services.Privacy.ConfidentialPolicyResolver _confidentialPolicyResolver;
    private readonly Overseer.Services.Privacy.ContentProtectionService _contentProtection;

    public SessionController(SignInManager<ApplicationUser> signInManager, ApplicationDbContext dbContext, IMemoryCache cache, IConfiguration configuration, IServiceScopeFactory scopeFactory, OngoingChatManager ongoingChatManager, ChatRetentionService chatRetentionService, Overseer.Services.Privacy.ConfidentialPolicyResolver confidentialPolicyResolver, Overseer.Services.Privacy.ContentProtectionService contentProtection)
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

        _dbContext.ChatSession.Add(session);
        await _dbContext.SaveChangesAsync();

        /* The DEK is created after the session has its identity value, because the id is the
           envelope's associated data: a key wrapped onto an unsaved session produces rows
           nothing can decrypt. */
        if (session.IsConfidential)
            _contentProtection.EnsureSessionKey(session);

        string? Protect(string? value)
            => session.IsConfidential ? _contentProtection.Encrypt(session, value) : value;

        await _chatRetentionService.EnforceUserSessionQuotaAsync(user.Id);

        if (!string.IsNullOrWhiteSpace(request.SnapshotHtml))
        {
            var sanitized = Overseer.Services.ChatService.SanitizeSnapshotForLlm(request.SnapshotHtml);
            var systemMsg = new ChatMessage
            {
                ChatSessionId = session.Id,
                Role = "system",
                Content = Protect(Overseer.Services.ChatService.GameSnapshotPrefix + "\n" + sanitized),
                IsGameSnapshot = true,
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

            /* A context file lands on disk enveloped in a confidential session, under a name
               carrying the .enc suffix. The suffix makes the file's state visible; the magic
               bytes are what the reader trusts, which is what lets an upgraded session hold
               both kinds. */
            async Task WriteContextFileAsync(string fileName, string text, ChatMessage owner)
            {
                string relPath = Path.Combine(session.Id.ToString(), fileName);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);

                if (session.IsConfidential)
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

        if (!string.IsNullOrWhiteSpace(request.InitialPrompt))
        {
            var userId = user.Id;
            var sessionRef = Overseer.Services.Privacy.SessionRef.Persistent(session.Id);
            var initialPrompt = request.InitialPrompt;

            var settings = await _dbContext.UserAiSettings.FindAsync(userId);
            int timeoutSeconds = settings?.RequestTimeout ?? _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Default", 300);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            _ongoingChatManager.TryStart(sessionRef, cts, out _);

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var chatService = scope.ServiceProvider.GetRequiredService<ChatService>();
                    await chatService.GenerateAndBroadcastMessageAsync(sessionRef, initialPrompt, null, userId, true, cts.Token);
                }
                finally
                {
                    _ongoingChatManager.Complete(sessionRef);
                }
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
}

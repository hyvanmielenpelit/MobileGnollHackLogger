using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace Overseer.Services;

/// <summary>
/// Starts the hidden greeting turn of a GnollHack handoff in the background, under the same rate limit,
/// concurrency gate and timeout as any other turn.
/// </summary>
public sealed class HandoffGreetingStarter
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OngoingChatManager _ongoingChatManager;
    private readonly Overseer.Security.UserTurnRateLimiter? _turnLimiter;
    private readonly Overseer.Security.UserTurnConcurrencyGate? _turnGate;
    private readonly Overseer.Services.Security.ISecurityEventRecorder? _securityEvents;
    private readonly ILogger<HandoffGreetingStarter>? _logger;

    public HandoffGreetingStarter(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        OngoingChatManager ongoingChatManager,
        Overseer.Security.UserTurnRateLimiter? turnLimiter = null,
        Overseer.Security.UserTurnConcurrencyGate? turnGate = null,
        Overseer.Services.Security.ISecurityEventRecorder? securityEvents = null,
        ILogger<HandoffGreetingStarter>? logger = null)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _ongoingChatManager = ongoingChatManager;
        _turnLimiter = turnLimiter;
        _turnGate = turnGate;
        _securityEvents = securityEvents;
        _logger = logger;
    }

    /// <summary>Whether the turn was started.</summary>
    public async Task<bool> TryStartAsync(ApplicationUser user, Overseer.Services.Privacy.SessionRef sessionRef, string initialPrompt, string? remoteIp)
    {
        var userId = user.Id;

        /* The handoff turn names no model, so ChatService runs it on the user's own model
           when they have one; only otherwise is it operator-funded. The rate limit applies
           to every handoff turn, the concurrency gate to operator-funded ones only. When
           either refuses, the session and its handoff token are still returned, without the
           greeting. */
        bool operatorFunded = _turnGate != null
            && !await _dbContext.UserAiModels.AnyAsync(m => m.AspNetUserId == userId);

        var settings = await _dbContext.UserAiSettings.FindAsync(userId);
        int timeoutSeconds = settings?.RequestTimeout ?? _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Default", 300);

        bool startTurn = true;
        if (_turnLimiter != null && !_turnLimiter.TryAcquire(user.UserName ?? userId))
        {
            _securityEvents?.Record(new Overseer.Services.Security.SecurityEventEntry(
                SecurityEventKind.RateLimitRejected, UserId: userId, IpAddress: remoteIp, Code: "session-create-turn"));
            startTurn = false;
        }

        bool gateEntered = false;
        if (startTurn && operatorFunded)
        {
            gateEntered = _turnGate!.TryEnter(userId);
            startTurn = gateEntered;
        }

        if (!startTurn)
        {
            return false;
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        if (_ongoingChatManager.TryStart(sessionRef, cts, out var ongoingState))
        {
            ongoingState.DeadlineUtc = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        }

        // The gate slot this turn holds, released by the background task's finally.
        var heldGate = gateEntered ? _turnGate : null;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var chatService = scope.ServiceProvider.GetRequiredService<ChatService>();
                await chatService.GenerateAndBroadcastMessageAsync(sessionRef, initialPrompt, null, userId, true, cts.Token);
            }
            catch (Exception ex)
            {
                // Detached from the request: the session's confidentiality is no longer resolvable, so telemetry fails closed.
                using (Overseer.Services.Privacy.ConfidentialExecutionScope.Enter(true))
                {
                    _logger?.LogError(ex, "Handoff greeting for session {SessionId} faulted.", sessionRef.ToWireString());
                }

                _ongoingChatManager.Fail(sessionRef, "The generation failed unexpectedly.");
            }
            finally
            {
                _ongoingChatManager.Complete(sessionRef);
                heldGate?.Exit(userId);
            }
        });

        return true;
    }
}

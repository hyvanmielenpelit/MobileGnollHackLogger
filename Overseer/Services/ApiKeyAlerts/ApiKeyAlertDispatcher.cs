using System.Reflection;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.ApiKeyAlerts;

public enum ApiKeyAlertOutcome
{
    /// <summary>The email was sent.</summary>
    Sent,
    /// <summary>An email for this key went out within the throttle window; the occurrence was counted.</summary>
    Suppressed,
    /// <summary>No recipient is configured; the occurrence was counted.</summary>
    NotConfigured,
    /// <summary>Building or sending the email failed; the occurrence was counted and the next one tries again.</summary>
    SendFailed,
    /// <summary>Another Overseer instance inserted this key's state first; the report was dropped.</summary>
    Dropped
}

/// <summary>
/// Reads <see cref="ApiKeyAlertService"/>'s queue one report at a time, which keeps the per-key
/// throttle's read-modify-write race-free within the process. Per report: counts the occurrence,
/// applies the throttle, gathers the configuration details and sends the email.
/// </summary>
public sealed class ApiKeyAlertDispatcher : BackgroundService
{
    public const string SystemApiKeyAssociatedData = "SYSTEM_API_KEY";

    private readonly ApiKeyAlertService _service;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ApiKeyAlertDispatcher> _logger;
    private readonly TimeProvider _timeProvider;

    public ApiKeyAlertDispatcher(
        ApiKeyAlertService service,
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        ILogger<ApiKeyAlertDispatcher> logger,
        TimeProvider? timeProvider = null)
    {
        _service = service;
        _scopeFactory = scopeFactory;
        _environment = environment;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var report in _service.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(report, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process an API key failure alert for {Provider} System AI Config {ConfigId}.",
                        report.Provider, report.SystemAiApiConfigurationId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task<ApiKeyAlertOutcome> ProcessAsync(ApiKeyFailureReport report, CancellationToken ct)
    {
        var options = _service.Options;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // 1. Count the occurrence.
        var state = await db.ApiKeyFailureAlertStates.FirstOrDefaultAsync(s => s.KeyFingerprint == report.KeyFingerprint, ct);
        bool isNew = state == null;
        if (state == null)
        {
            state = new ApiKeyFailureAlertState { KeyFingerprint = report.KeyFingerprint };
            db.ApiKeyFailureAlertStates.Add(state);
        }

        if (state.OccurrencesSinceLastEmail <= 0)
        {
            state.OccurrencesSinceLastEmail = 0;
            state.FirstOccurredUtc = report.OccurredUtc;
        }
        state.OccurrencesSinceLastEmail++;
        state.Provider = report.Provider;
        state.LastOccurredUtc = report.OccurredUtc;
        state.LastFailureKind = report.Kind.ToString();
        state.LastSystemAiApiConfigurationId = report.SystemAiApiConfigurationId;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (isNew)
        {
            _logger.LogWarning(ex,
                "API key failure alert state for {Provider} was inserted by another instance; this report is dropped.", report.Provider);
            return ApiKeyAlertOutcome.Dropped;
        }

        // 2. Throttle.
        if (state.LastEmailSentUtc is DateTime lastSent && report.OccurredUtc - lastSent < options.ThrottleWindow)
        {
            _logger.LogInformation(
                "API key failure alert for {Provider} System AI Config {ConfigId} suppressed: an email went out at {LastSent:o}; {Count} occurrence(s) since.",
                report.Provider, report.SystemAiApiConfigurationId, lastSent, state.OccurrencesSinceLastEmail);
            return ApiKeyAlertOutcome.Suppressed;
        }

        if (string.IsNullOrWhiteSpace(options.RecipientEmail))
        {
            _logger.LogError(
                "API key failure alert for {Provider} System AI Config {ConfigId} ({Kind}) not sent: ApiKeyAlerts:RecipientEmail is not configured.",
                report.Provider, report.SystemAiApiConfigurationId, report.Kind);
            return ApiKeyAlertOutcome.NotConfigured;
        }

        try
        {
            // 3. Gather the details.
            var model = await BuildModelAsync(scope.ServiceProvider, db, report, state, ct);

            // 4. Build and send.
            var (subject, html) = ApiKeyAlertEmailBuilder.Build(model);
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            await sender.SendEmailAsync(options.RecipientEmail, subject, html);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send the API key failure alert for {Provider} System AI Config {ConfigId} ({Kind}); the next occurrence tries again.",
                report.Provider, report.SystemAiApiConfigurationId, report.Kind);
            return ApiKeyAlertOutcome.SendFailed;
        }

        // 5. Record the email.
        state.LastEmailSentUtc = _timeProvider.GetUtcNow().UtcDateTime;
        state.OccurrencesSinceLastEmail = 0;
        await db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "API key failure alert sent for {Provider} System AI Config {ConfigId} ({Kind}).",
            report.Provider, report.SystemAiApiConfigurationId, report.Kind);
        return ApiKeyAlertOutcome.Sent;
    }

    private async Task<ApiKeyAlertEmailModel> BuildModelAsync(
        IServiceProvider services, ApplicationDbContext db, ApiKeyFailureReport report, ApiKeyFailureAlertState state, CancellationToken ct)
    {
        var config = await db.SystemAiApiConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == report.SystemAiApiConfigurationId, ct);

        string provider = config?.Provider ?? report.Provider;

        SystemDefaultApiKey? defaultKey = null;
        if (config?.UseDefaultApiKey == true && SystemDefaultApiKeyService.TryCanonicalizeProvider(provider, out var canonical))
        {
            defaultKey = await db.SystemDefaultApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Provider == canonical, ct);
        }

        var sharing = await FindSharingConfigsAsync(services, db, provider, report.KeyFingerprint, ct);

        bool? isConfidential = null;
        bool? isGnollHack = null;
        if (report.Context.Session.IsPersistent)
        {
            long sessionId = report.Context.Session.PersistentId;
            var flags = await db.ChatSession.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => new { s.IsConfidential, s.IsGnollHackSession })
                .FirstOrDefaultAsync(ct);
            isConfidential = flags?.IsConfidential;
            isGnollHack = flags?.IsGnollHackSession;
        }

        return new ApiKeyAlertEmailModel
        {
            Report = report,
            Config = config,
            DefaultKey = defaultKey,
            SharingConfigs = sharing,
            SessionIsConfidential = isConfidential,
            SessionIsGnollHack = isGnollHack,
            OccurrencesSinceLastEmail = state.OccurrencesSinceLastEmail,
            FirstOccurredUtc = state.FirstOccurredUtc,
            PreviousEmailSentUtc = state.LastEmailSentUtc,
            EnvironmentName = _environment.EnvironmentName,
            MachineName = Environment.MachineName,
            OverseerVersion = typeof(ApiKeyAlertDispatcher).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            ThrottleWindow = _service.Options.ThrottleWindow,
            GeneratedUtc = _timeProvider.GetUtcNow().UtcDateTime
        };
    }

    /// <summary>
    /// The configurations of this provider whose key fingerprints match. Each key is decrypted only
    /// to be fingerprinted and is discarded at once. Null when the keys cannot be decrypted at all.
    /// </summary>
    private async Task<IReadOnlyList<ApiKeyAlertSharingConfig>?> FindSharingConfigsAsync(
        IServiceProvider services, ApplicationDbContext db, string provider, string fingerprint, CancellationToken ct)
    {
        var crypto = services.GetService<CryptoService>();
        if (crypto == null) return null;

        var candidates = (await db.SystemAiApiConfigurations.AsNoTracking()
                .Where(c => c.EncryptedApiKey != null && c.EncryptedApiKey != "")
                .Select(c => new
                {
                    c.Id, c.Provider, c.DisplayName, c.ModelId, c.IsEnabled, c.UseDefaultApiKey,
                    c.EncryptedApiKey, c.ApiKeyNonce, c.ApiKeyTag
                })
                .ToListAsync(ct))
            .Where(c => string.Equals(c.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Id);

        var result = new List<ApiKeyAlertSharingConfig>();
        foreach (var c in candidates)
        {
            bool matches;
            bool unreadable = false;
            try
            {
                matches = ApiKeyFailureReport.Fingerprint(
                    crypto.Decrypt(c.EncryptedApiKey!, c.ApiKeyNonce ?? string.Empty, c.ApiKeyTag ?? string.Empty, SystemApiKeyAssociatedData))
                    == fingerprint;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("The key of System AI Config {ConfigId} could not be decrypted for the alert's sharing check: {Error}",
                    c.Id, ex.GetType().Name);
                matches = true;
                unreadable = true;
            }

            if (matches)
                result.Add(new ApiKeyAlertSharingConfig(c.Id, c.DisplayName, c.ModelId, c.IsEnabled, c.UseDefaultApiKey, unreadable));
        }
        return result;
    }
}

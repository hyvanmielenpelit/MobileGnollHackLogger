using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Providers;
using Overseer.Services.Privacy;
using Overseer.Extensions;
using System.Security.Claims;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settingsService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ModelMetadataService _modelMetadataService;
    private readonly RecommendedModelService _recommendedModelService;
    private readonly IAuthorizationService _authorizationService;
    private readonly Overseer.Services.Privacy.EndpointPolicy _endpointPolicy;
    private readonly Overseer.Services.Privacy.ConfidentialPolicyResolver _confidentialPolicyResolver;
    private readonly Overseer.Services.Privacy.Dlp.DlpScannerService _dlpScanner;
    private readonly Overseer.Services.Privacy.AttachmentValidator _attachmentValidator;
    private readonly IEnumerable<IAiProvider> _aiProviders;
    private readonly ModelPricingService? _modelPricingService;

    public SettingsController(
        SettingsService settingsService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ModelMetadataService modelMetadataService,
        RecommendedModelService recommendedModelService,
        IAuthorizationService authorizationService,
        Overseer.Services.Privacy.EndpointPolicy endpointPolicy,
        Overseer.Services.Privacy.ConfidentialPolicyResolver confidentialPolicyResolver,
        Overseer.Services.Privacy.Dlp.DlpScannerService dlpScanner,
        Overseer.Services.Privacy.AttachmentValidator attachmentValidator,
        IEnumerable<IAiProvider> aiProviders,
        ModelPricingService? modelPricingService = null)
    {
        _settingsService = settingsService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _modelMetadataService = modelMetadataService;
        _recommendedModelService = recommendedModelService;
        _authorizationService = authorizationService;
        _endpointPolicy = endpointPolicy;
        _confidentialPolicyResolver = confidentialPolicyResolver;
        _dlpScanner = dlpScanner;
        _attachmentValidator = attachmentValidator;
        _aiProviders = aiProviders;
        _modelPricingService = modelPricingService;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetSettings()
    {
        var swTotal = Stopwatch.StartNew();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var swQuery = Stopwatch.StartNew();
        var settings = await _settingsService.GetSettingsAsync(userId);
        var dlpPolicy = _dlpScanner.Resolve(settings);
        swQuery.Stop();
        var settingsMs = swQuery.ElapsedMilliseconds;
        
        swQuery.Restart();
        var apiKeysStatus = await _settingsService.GetApiKeysStatusAsync(userId);
        swQuery.Stop();
        var apiKeysMs = swQuery.ElapsedMilliseconds;

        swQuery.Restart();
        var userModels = await _settingsService.GetUserModelsAsync(userId);
        swQuery.Stop();
        var userModelsMs = swQuery.ElapsedMilliseconds;

        swQuery.Restart();
        var systemConfigs = await _settingsService.GetResolvedSystemModelsAsync(userId);
        swQuery.Stop();
        var sysModelsMs = swQuery.ElapsedMilliseconds;

        bool hasSystemModel = systemConfigs.Any();
        
        bool hasApiKey = apiKeysStatus.Any(s => (bool)((dynamic)s).HasKey) || hasSystemModel;
        bool hasModel = userModels.Any() || hasSystemModel;
        
        bool showDebugLog = _configuration.ShouldShowDebugLog(User.Identity?.Name);

        swTotal.Stop();

        Response.Headers.Append("Access-Control-Expose-Headers", "Server-Timing");
        Response.Headers.Append("Server-Timing", $"total;dur={swTotal.ElapsedMilliseconds}, settings;dur={settingsMs}, apikeys;dur={apiKeysMs}, usermodels;dur={userModelsMs}, sysmodels;dur={sysModelsMs}");
        
        return Ok(new
        {
            hasApiKey = hasApiKey,
            hasModel = hasModel,
            configuredProviders = apiKeysStatus.Where(s => (bool)((dynamic)s).HasKey).Select(s => (string)((dynamic)s).Provider).ToList(),
            maxAttachmentSize = _configuration.GetValue<long>("MaxAttachmentSize", 15728640),
            /* The file picker's accept list, served from the same allowlist the validator
               enforces. It used to be a literal in the template kept "in step" by a comment,
               which is a promise rather than a mechanism: the two drifting apart gives a user a
               file dialog offering exactly what the server then refuses. */
            attachmentAcceptExtensions = _attachmentValidator.AllowedExtensions,
            spoilerFreeMode = settings?.SpoilerFreeMode ?? true,
            showSourceCodeReferences = settings?.ShowSourceCodeReferences ?? false,
            maxResultLength = settings?.MaxResultLength,
            maxCallsPerSession = settings?.MaxCallsPerSession,
            maxToolIterations = settings?.MaxToolIterations,
            maxParallelToolCalls = settings?.MaxParallelToolCalls,
            enableWebSearch = settings?.EnableWebSearch ?? true,
            enableToolUse = settings?.EnableToolUse ?? true,
            enableSubAgents = settings?.EnableSubAgents ?? false,
            enableClientTools = settings?.EnableClientTools ?? true,
            enableGameActions = settings?.EnableGameActions ?? false,
            showThoughtsAndTools = settings?.ShowThoughtsAndTools ?? 0,
            showParallelBadge = settings?.ShowParallelBadge ?? true,
            showContextWindowUsage = settings?.ShowContextWindowUsage ?? true,
            showChatCost = settings?.ShowChatCost ?? true,
            parallelBadgeEnabled = _configuration.GetValue<bool>("ParallelExecutionSettings:ShowBadge", true),
            requestTimeout = settings?.RequestTimeout,
            showDebugLog = showDebugLog,
            performanceLimits = new {
                // These defaultValue fallbacks are what the settings UI labels "Default", so a
                // fallback disagreeing with appsettings.json would show the user a number the chat
                // does not actually use. Keep them in step with AiPerformanceSettings.
                maxResultLength = new {
                    min = _configuration.GetValue<int>("AiPerformanceSettings:MaxResultLength:Min", 500),
                    max = _configuration.GetValue<int>("AiPerformanceSettings:MaxResultLength:Max", 100000),
                    defaultValue = _configuration.GetValue<int>("AiPerformanceSettings:MaxResultLength:Default", 10000)
                },
                maxCallsPerSession = new {
                    min = _configuration.GetValue<int>("AiPerformanceSettings:MaxCallsPerSession:Min", 1),
                    max = _configuration.GetValue<int>("AiPerformanceSettings:MaxCallsPerSession:Max", 500),
                    defaultValue = _configuration.GetValue<int>("AiPerformanceSettings:MaxCallsPerSession:Default", 150)
                },
                maxToolIterations = new {
                    min = _configuration.GetValue<int>("AiPerformanceSettings:MaxToolIterations:Min", 1),
                    max = _configuration.GetValue<int>("AiPerformanceSettings:MaxToolIterations:Max", 100),
                    defaultValue = _configuration.GetValue<int>("AiPerformanceSettings:MaxToolIterations:Default", 22)
                },
                maxParallelToolCalls = new {
                    min = _configuration.GetValue<int>("AiPerformanceSettings:MaxParallelToolCalls:Min", 1),
                    max = _configuration.GetValue<int>("AiPerformanceSettings:MaxParallelToolCalls:Max", 10),
                    defaultValue = _configuration.GetValue<int>("AiPerformanceSettings:MaxParallelToolCalls:Default", 6)
                },
                requestTimeout = new {
                    min = _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Min", 5),
                    max = _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Max", 3600),
                    defaultValue = _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Default", 300)
                }
            },
            titleGenerationModelId = settings?.TitleGenerationModelId,
            titleGenerationSystemModelId = settings?.TitleGenerationSystemModelId,
            titleGenerationDisabled = settings?.TitleGenerationDisabled ?? false,

            /* The user's own confidential preferences, and the administrator's floor as a
               separate object. The client needs both: the effective value is the stricter of
               the two, so a control the floor already fixes has to be shown as fixed rather
               than accepting a setting that silently has no effect. */
            confidentialPersistence = settings?.ConfidentialPersistence
                ?? Overseer.Services.Privacy.ConfidentialPersistence.Encrypted.ToString(),
            confidentialRetentionDays = settings?.ConfidentialRetentionDays ?? 30,
            confidentialDisableToolEgress = settings?.ConfidentialDisableToolEgress ?? true,
            confidentialDisableTitleGeneration = settings?.ConfidentialDisableTitleGeneration ?? true,
            confidentialDisablePromptCache = settings?.ConfidentialDisablePromptCache ?? true,
            confidentialImmediatePurge = settings?.ConfidentialImmediatePurge ?? true,
            confidentialModelGate = settings?.ConfidentialModelGate
                ?? Overseer.Services.Privacy.ConfidentialityGateMode.UserDecides.ToString(),
            confidentialFirstUseNoticeAcknowledged = settings?.ConfidentialFirstUseNoticeAcknowledged ?? false,
            confidentialFloor = new
            {
                persistence = _confidentialPolicyResolver.Floor.Persistence.ToString(),
                retentionDays = _confidentialPolicyResolver.Floor.RetentionDays,
                disableToolEgress = _confidentialPolicyResolver.Floor.DisableToolEgress,
                disableTitleGeneration = _confidentialPolicyResolver.Floor.DisableTitleGeneration,
                disablePromptCache = _confidentialPolicyResolver.Floor.DisablePromptCache,
                immediatePurge = _confidentialPolicyResolver.Floor.ImmediatePurge,
                modelGate = _confidentialPolicyResolver.Floor.ModelGate.ToString()
            },

            /* The RESOLVED masking policy, not the raw preferences -- unlike the confidential
               block above, whose controls the client clamps itself. A class the administrator
               forces on reads back as on, so the switch shows what actually happens rather
               than what the user last asked for, and dlpFloor says which ones they cannot
               change. */
            dlpMaskApiKeys = dlpPolicy.ApiKeys,
            dlpMaskPrivateKeys = dlpPolicy.PrivateKeys,
            dlpMaskTokens = dlpPolicy.Tokens,
            dlpMaskCreditCards = dlpPolicy.CreditCards,
            dlpMaskSsns = dlpPolicy.Ssns,
            dlpMaskEmails = dlpPolicy.Emails,
            dlpMaskPhoneNumbers = dlpPolicy.PhoneNumbers,
            dlpFloor = new
            {
                apiKeys = _dlpScanner.Floor.ApiKeys,
                privateKeys = _dlpScanner.Floor.PrivateKeys,
                tokens = _dlpScanner.Floor.Tokens,
                creditCards = _dlpScanner.Floor.CreditCards,
                ssns = _dlpScanner.Floor.Ssns,
                emails = _dlpScanner.Floor.Emails,
                phoneNumbers = _dlpScanner.Floor.PhoneNumbers
            }
        });
    }

    /// <summary>Saves the user's outbound-masking switches.</summary>
    /// <remarks>
    /// Its own endpoint rather than more fields on <c>PUT /api/settings</c>, because these are
    /// the one group of settings that changes what leaves the server: keeping the route
    /// separate keeps that visible in a log and in a permission review.
    /// </remarks>
    [HttpPost("dlp")]
    public async Task<IActionResult> UpdateDlpSettings([FromBody] UpdateDlpSettingsRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _settingsService.SaveDlpSettingsAsync(
            userId,
            request.DlpMaskApiKeys,
            request.DlpMaskPrivateKeys,
            request.DlpMaskTokens,
            request.DlpMaskCreditCards,
            request.DlpMaskSsns,
            request.DlpMaskEmails,
            request.DlpMaskPhoneNumbers);

        return Ok();
    }

    /// <summary>Marks the confidentiality first-use notice as seen, so it is not shown again.</summary>
    [HttpPost("confidential-notice-acknowledged")]
    public async Task<IActionResult> AcknowledgeConfidentialNotice()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _settingsService.AcknowledgeConfidentialNoticeAsync(userId);
        return Ok();
    }

    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        // Enforce Tier 3 -> Tier 4 dependency
        if (request.EnableClientTools.HasValue && !request.EnableClientTools.Value)
        {
            request.EnableGameActions = false;
        }

        // Validate limits
        if (request.MaxResultLength.HasValue)
        {
            int min = _configuration.GetValue<int>("AiPerformanceSettings:MaxResultLength:Min", 500);
            int max = _configuration.GetValue<int>("AiPerformanceSettings:MaxResultLength:Max", 100000);
            if (request.MaxResultLength.Value < min || request.MaxResultLength.Value > max)
                return BadRequest($"MaxResultLength must be between {min} and {max}");
        }

        if (request.MaxCallsPerSession.HasValue)
        {
            int min = _configuration.GetValue<int>("AiPerformanceSettings:MaxCallsPerSession:Min", 1);
            int max = _configuration.GetValue<int>("AiPerformanceSettings:MaxCallsPerSession:Max", 500);
            if (request.MaxCallsPerSession.Value < min || request.MaxCallsPerSession.Value > max)
                return BadRequest($"MaxCallsPerSession must be between {min} and {max}");
        }

        if (request.MaxToolIterations.HasValue)
        {
            int min = _configuration.GetValue<int>("AiPerformanceSettings:MaxToolIterations:Min", 1);
            int max = _configuration.GetValue<int>("AiPerformanceSettings:MaxToolIterations:Max", 100);
            if (request.MaxToolIterations.Value < min || request.MaxToolIterations.Value > max)
                return BadRequest($"MaxToolIterations must be between {min} and {max}");
        }

        if (request.MaxParallelToolCalls.HasValue)
        {
            int min = _configuration.GetValue<int>("AiPerformanceSettings:MaxParallelToolCalls:Min", 1);
            int max = _configuration.GetValue<int>("AiPerformanceSettings:MaxParallelToolCalls:Max", 10);
            if (request.MaxParallelToolCalls.Value < min || request.MaxParallelToolCalls.Value > max)
                return BadRequest($"MaxParallelToolCalls must be between {min} and {max}");
        }

        if (request.RequestTimeout.HasValue)
        {
            int min = _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Min", 5);
            int max = _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Max", 3600);
            if (request.RequestTimeout.Value < min || request.RequestTimeout.Value > max)
                return BadRequest($"RequestTimeout must be between {min} and {max}");
        }

        await _settingsService.SaveSettingsAsync(userId, request.SpoilerFreeMode, request.EnableWebSearch, request.EnableToolUse, request.EnableClientTools, request.EnableGameActions, request.ShowSourceCodeReferences, request.MaxResultLength, request.MaxCallsPerSession, request.MaxToolIterations, request.MaxParallelToolCalls, request.ShowThoughtsAndTools, request.RequestTimeout, request.EnableSubAgents, request.ShowParallelBadge, request.ShowContextWindowUsage, request.ShowChatCost);

        await _settingsService.SaveConfidentialSettingsAsync(
            userId,
            request.ConfidentialPersistence,
            request.ConfidentialRetentionDays,
            request.ConfidentialDisableToolEgress,
            request.ConfidentialDisableTitleGeneration,
            request.ConfidentialDisablePromptCache,
            request.ConfidentialImmediatePurge,
            request.ConfidentialModelGate);
        
        return Ok();
    }

    [HttpPut("titlemodel")]
    public async Task<IActionResult> UpdateTitleModel([FromBody] UpdateTitleModelRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        try
        {
            await _settingsService.SaveTitleGenerationModelAsync(userId, request.ModelId, request.IsSystem, request.Disabled);
            return Ok();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }


    [HttpGet("apikeys")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetApiKeys()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var statuses = await _settingsService.GetApiKeysStatusAsync(userId);
        return Ok(statuses);
    }

    [HttpPut("apikeys")]
    public async Task<IActionResult> SaveApiKey([FromBody] SaveApiKeyRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();
        if (string.IsNullOrEmpty(request.Provider) || string.IsNullOrEmpty(request.ApiKey)) return BadRequest();

        await _settingsService.SaveApiKeyForProviderAsync(userId, request.Provider, request.ApiKey);
        return Ok();
    }

    [HttpDelete("apikeys/{provider}")]
    public async Task<IActionResult> DeleteApiKeyForProvider(string provider)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _settingsService.DeleteApiKeyForProviderAsync(userId, provider);
        return Ok();
    }

    [HttpPut("apikeys/{provider}/parallel")]
    public async Task<IActionResult> SetApiKeyParallelMode(string provider, [FromBody] SetApiKeyParallelModeRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (request.Mode < 0 || request.Mode > 2)
            return BadRequest("Mode must be 0 (Disabled), 1 (OnRequest), or 2 (Enabled).");

        if (!SettingsService.SupportedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase))
            return BadRequest($"Unsupported provider '{provider}'. Supported providers are: {string.Join(", ", SettingsService.SupportedProviders)}.");

        var matchedProvider = SettingsService.SupportedProviders.First(p => p.Equals(provider, StringComparison.OrdinalIgnoreCase));

        await _settingsService.SaveApiKeyParallelModeAsync(userId, matchedProvider, request.Mode);
        return Ok();
    }

    /// <summary>
    /// Records the posture the user declares for their own provider account.
    /// </summary>
    /// <remarks>
    /// What is stored is a claim, not a fact: Overseer cannot see the user's agreement with
    /// their provider and never treats this as equivalent to an operator-verified posture on a
    /// system configuration. The badge resolver enforces that, and the UI says so.
    /// </remarks>
    [HttpPut("apikeys/{provider}/posture")]
    public async Task<IActionResult> SetApiKeyPosture(string provider, [FromBody] SetApiKeyPostureRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (!TryMatchProvider(provider, out var matchedProvider, out var providerError))
            return BadRequest(providerError);

        /* An unrecognised name is refused rather than parsed down to Unknown. The parser
           degrades safely for a value already in the database, which is the right behaviour
           when reading; on the way in, silently storing something other than what the caller
           asked for is how a posture ends up meaning nothing. */
        string? posture = null;
        if (!string.IsNullOrWhiteSpace(request.Posture))
        {
            if (!Enum.TryParse<Overseer.Services.Privacy.ProviderConfidentialityPosture>(
                    request.Posture.Trim(), ignoreCase: true, out var parsed))
            {
                return BadRequest(
                    $"Unrecognised confidentiality posture '{request.Posture}'. Valid values are: "
                    + string.Join(", ", Enum.GetNames<Overseer.Services.Privacy.ProviderConfidentialityPosture>()) + ".");
            }

            posture = parsed.ToStoredValue();
        }

        if (request.Note != null && request.Note.Length > 1024)
            return BadRequest("The note cannot exceed 1024 characters.");

        await _settingsService.SaveApiKeyPostureAsync(userId, matchedProvider, posture, request.Note);
        return Ok();
    }

    /// <summary>
    /// Records whether the user considers this key suitable for confidential sessions.
    /// </summary>
    /// <remarks>
    /// Null returns the key to undecided, which is what the AskWhenUnclear gate prompts about.
    /// False is a decision and refuses the key; it is not the same as never having answered.
    /// </remarks>
    [HttpPut("apikeys/{provider}/confidential-trust")]
    public async Task<IActionResult> SetApiKeyConfidentialTrust(
        string provider, [FromBody] SetApiKeyConfidentialTrustRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (!TryMatchProvider(provider, out var matchedProvider, out var providerError))
            return BadRequest(providerError);

        await _settingsService.SaveApiKeyConfidentialTrustAsync(userId, matchedProvider, request.Trusted);
        return Ok();
    }

    /// <summary>
    /// Would set a custom endpoint on the user's own key. Refused with <c>400</c> while
    /// <c>PrivacySettings:CustomEndpoints:AllowUserSuppliedBaseUrl</c> is false, which is its
    /// value for this framework version.
    /// </summary>
    /// <remarks>
    /// The endpoint exists precisely so the refusal is explicit. Accepting the value and then
    /// ignoring it — which is what silently dropping it amounts to — would leave a user
    /// believing their traffic goes somewhere it does not, and a base URL is the one setting
    /// where that belief is a security question rather than a preference.
    ///
    /// A user may declare what their provider's terms are; they may not decide where the
    /// server sends its outbound requests. That is an administrator's decision because the
    /// server's network is the administrator's, not the user's.
    /// </remarks>
    [HttpPut("apikeys/{provider}/endpoint")]
    public async Task<IActionResult> SetApiKeyEndpoint(string provider, [FromBody] SetApiKeyEndpointRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (!TryMatchProvider(provider, out var matchedProvider, out var providerError))
            return BadRequest(providerError);

        if (!_endpointPolicy.AllowUserSuppliedBaseUrl)
        {
            return BadRequest(new
            {
                message = "Custom AI endpoints are configured by an administrator, not per user. "
                    + "Your key's requests go to the provider's official endpoint."
            });
        }

        var check = _endpointPolicy.Validate(request.BaseUrl, request.CustomHeadersJson, request.ApiVersion);
        if (!check.IsValid)
            return BadRequest(new { message = check.Error });

        await _settingsService.SaveApiKeyEndpointAsync(
            userId, matchedProvider, request.BaseUrl, request.CustomHeadersJson, request.ApiVersion);
        return Ok();
    }

    /* Case-insensitive match against the supported list, returning the canonical casing --
       the provider is part of the key row's identity, so "openai" and "OpenAI" must not become
       two rows. */
    private static bool TryMatchProvider(string provider, out string matched, out string error)
    {
        if (!SettingsService.SupportedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase))
        {
            matched = string.Empty;
            error = $"Unsupported provider '{provider}'. Supported providers are: "
                + string.Join(", ", SettingsService.SupportedProviders) + ".";
            return false;
        }

        matched = SettingsService.SupportedProviders.First(p => p.Equals(provider, StringComparison.OrdinalIgnoreCase));
        error = string.Empty;
        return true;
    }

    [HttpGet("usermodels")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetUserModels()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var apiKeysStatus = await _settingsService.GetApiKeysStatusAsync(userId);
        var keyModeMap = apiKeysStatus.ToDictionary(
            s => (string)((dynamic)s).Provider,
            s => (int)((dynamic)s).ParallelExecutionMode,
            StringComparer.OrdinalIgnoreCase);

        /* A user's own model has no posture of its own: it runs on that user's key, so the
           posture is whatever they declared for that provider. Self-declared throughout, which
           is why the DTO reports it with no verification date -- a user key can never carry
           one. */
        var keyPostureMap = apiKeysStatus.ToDictionary(
            s => (string)((dynamic)s).Provider,
            s => (string?)((dynamic)s).ConfidentialityPosture,
            StringComparer.OrdinalIgnoreCase);

        var models = await _settingsService.GetUserModelsAsync(userId);
        var dtos = models.Select(m => {
            var resolvedPricing = _modelPricingService?.Resolve(m);
            return new {
                Id = m.Id,
                Provider = m.Provider,
                ModelId = m.ModelId,
                DisplayName = (string?)m.DisplayName,
                DisplayNameMode = (string?)m.DisplayNameMode,
                ThinkingLevel = (string?)m.ThinkingLevel,
                OrderIndex = m.OrderIndex,
                MaxInputTokens = (int?)m.MaxInputTokens,
                MaxOutputTokens = (int?)m.MaxOutputTokens,
                ReasoningMode = m.ReasoningMode,
                ReasoningSummary = m.ReasoningSummary,
                ServiceTier = m.ServiceTier,
                IsSystem = false,
                ModelRole = 3,
                ParallelExecutionMode = keyModeMap.TryGetValue(m.Provider, out var mode) ? mode : 2,
                ConfidentialityPosture = keyPostureMap.TryGetValue(m.Provider, out var keyPosture) ? keyPosture : null,
                PostureVerifiedUtc = (DateTime?)null,
                DataRegion = (string?)null,
                PricingMode = m.PricingMode,
                InputPricePerMillion = m.InputPricePerMillion,
                OutputPricePerMillion = m.OutputPricePerMillion,
                CachedInputPricePerMillion = m.CachedInputPricePerMillion,
                EffectiveInputPricePerMillion = resolvedPricing?.InputPerMillion,
                EffectiveOutputPricePerMillion = resolvedPricing?.OutputPerMillion,
                EffectiveCachedInputPricePerMillion = resolvedPricing?.CachedInputPerMillion,
                PricingSource = resolvedPricing != null ? (resolvedPricing.Source == ModelPricingSource.Custom ? "custom" : "catalog") : "unknown",
                PricingAsOf = resolvedPricing?.AsOf,
                // All null / false for a flat, unscheduled model, which is most of them. A custom price
                // override is always flat by design, so these are null there too.
                EffectiveLongContextThresholdTokens = resolvedPricing?.LongContext?.ThresholdInputTokens,
                EffectiveLongContextInputPricePerMillion = resolvedPricing?.LongContext?.InputPerMillion,
                EffectiveLongContextOutputPricePerMillion = resolvedPricing?.LongContext?.OutputPerMillion,
                EffectiveServiceTierMultipliers = resolvedPricing?.ServiceTierMultipliers,
                PricingScheduledChangeFrom = resolvedPricing?.ScheduledChange?.EffectiveFrom.ToString("yyyy-MM-dd"),
                PricingScheduledChangeInputPricePerMillion = resolvedPricing?.ScheduledChange?.InputPerMillion,
                PricingScheduledChangeOutputPricePerMillion = resolvedPricing?.ScheduledChange?.OutputPerMillion,
                PricingScheduledChangeNote = resolvedPricing?.ScheduledChange?.Note,
                PricingScheduleElapsed = resolvedPricing?.ScheduleElapsed ?? false
            };
        }).ToList();

        var systemConfigs = await _settingsService.GetResolvedSystemModelsAsync(userId);
        var sysDtos = systemConfigs.Select(x => {
            var resolvedPricing = _modelPricingService?.Resolve(x.Config);
            return new {
                Id = x.Config.Id,
                Provider = x.Config.Provider,
                ModelId = x.Config.ModelId,
                DisplayName = (string?)x.Config.DisplayName,
                DisplayNameMode = (string?)x.Config.DisplayNameMode,
                ThinkingLevel = (string?)x.Config.ThinkingLevel,
                OrderIndex = x.Config.OrderIndex,
                MaxInputTokens = (int?)x.Config.MaxInputTokens,
                MaxOutputTokens = (int?)x.Config.MaxOutputTokens,
                ReasoningMode = x.Config.ReasoningMode,
                ReasoningSummary = x.Config.ReasoningSummary,
                ServiceTier = x.Config.ServiceTier,
                IsSystem = true,
                ModelRole = x.ResolvedRole,
                ParallelExecutionMode = (int)x.Config.ParallelExecutionMode,
                ConfidentialityPosture = x.Config.ConfidentialityPosture,
                PostureVerifiedUtc = x.Config.PostureVerifiedUtc,
                DataRegion = x.Config.DataRegion,
                PricingMode = x.Config.PricingMode,
                InputPricePerMillion = x.Config.InputPricePerMillion,
                OutputPricePerMillion = x.Config.OutputPricePerMillion,
                CachedInputPricePerMillion = x.Config.CachedInputPricePerMillion,
                EffectiveInputPricePerMillion = resolvedPricing?.InputPerMillion,
                EffectiveOutputPricePerMillion = resolvedPricing?.OutputPerMillion,
                EffectiveCachedInputPricePerMillion = resolvedPricing?.CachedInputPerMillion,
                PricingSource = resolvedPricing != null ? (resolvedPricing.Source == ModelPricingSource.Custom ? "custom" : "catalog") : "unknown",
                PricingAsOf = resolvedPricing?.AsOf,
                // All null / false for a flat, unscheduled model, which is most of them. A custom price
                // override is always flat by design, so these are null there too.
                EffectiveLongContextThresholdTokens = resolvedPricing?.LongContext?.ThresholdInputTokens,
                EffectiveLongContextInputPricePerMillion = resolvedPricing?.LongContext?.InputPerMillion,
                EffectiveLongContextOutputPricePerMillion = resolvedPricing?.LongContext?.OutputPerMillion,
                EffectiveServiceTierMultipliers = resolvedPricing?.ServiceTierMultipliers,
                PricingScheduledChangeFrom = resolvedPricing?.ScheduledChange?.EffectiveFrom.ToString("yyyy-MM-dd"),
                PricingScheduledChangeInputPricePerMillion = resolvedPricing?.ScheduledChange?.InputPerMillion,
                PricingScheduledChangeOutputPricePerMillion = resolvedPricing?.ScheduledChange?.OutputPerMillion,
                PricingScheduledChangeNote = resolvedPricing?.ScheduledChange?.Note,
                PricingScheduleElapsed = resolvedPricing?.ScheduleElapsed ?? false
            };
        });

        dtos.AddRange(sysDtos);

        return Ok(dtos);
    }

    [HttpPost("usermodels")]
    public async Task<IActionResult> AddUserModel([FromBody] AddUserModelRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();
        if (string.IsNullOrEmpty(request.Provider) || string.IsNullOrEmpty(request.ModelId)) return BadRequest();

        if ((request.InputPricePerMillion.HasValue && request.InputPricePerMillion.Value < 0) ||
            (request.OutputPricePerMillion.HasValue && request.OutputPricePerMillion.Value < 0) ||
            (request.CachedInputPricePerMillion.HasValue && request.CachedInputPricePerMillion.Value < 0))
        {
            return BadRequest("Prices cannot be negative.");
        }

        string normalizedPricingMode = PricingModes.Normalize(request.PricingMode);
        if (string.Equals(normalizedPricingMode, PricingModes.Custom, StringComparison.OrdinalIgnoreCase))
        {
            if (!request.InputPricePerMillion.HasValue || !request.OutputPricePerMillion.HasValue)
            {
                return BadRequest("Custom pricing requires both input and output prices per million.");
            }
        }

        var model = new MobileGnollHackLogger.Data.UserAiModel
        {
            Provider = request.Provider,
            ModelId = request.ModelId,
            DisplayName = request.DisplayName,
            DisplayNameMode = DisplayNameModes.Normalize(request.DisplayNameMode),
            ThinkingLevel = request.ThinkingLevel,
            ReasoningMode = request.ReasoningMode,
            ReasoningSummary = request.ReasoningSummary,
            ServiceTier = request.ServiceTier,
            MaxInputTokens = request.MaxInputTokens,
            MaxOutputTokens = request.MaxOutputTokens,
            PricingMode = normalizedPricingMode,
            InputPricePerMillion = request.InputPricePerMillion,
            OutputPricePerMillion = request.OutputPricePerMillion,
            CachedInputPricePerMillion = request.CachedInputPricePerMillion
        };
        await _settingsService.AddUserModelAsync(userId, model);
        return Ok(new { model.Id });
    }

    [HttpPut("usermodels/{id}")]
    public async Task<IActionResult> UpdateUserModel(long id, [FromBody] UpdateUserModelRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if ((request.InputPricePerMillion.HasValue && request.InputPricePerMillion.Value < 0) ||
            (request.OutputPricePerMillion.HasValue && request.OutputPricePerMillion.Value < 0) ||
            (request.CachedInputPricePerMillion.HasValue && request.CachedInputPricePerMillion.Value < 0))
        {
            return BadRequest("Prices cannot be negative.");
        }

        string normalizedPricingMode = PricingModes.Normalize(request.PricingMode);
        if (string.Equals(normalizedPricingMode, PricingModes.Custom, StringComparison.OrdinalIgnoreCase))
        {
            if (!request.InputPricePerMillion.HasValue || !request.OutputPricePerMillion.HasValue)
            {
                return BadRequest("Custom pricing requires both input and output prices per million.");
            }
        }

        await _settingsService.UpdateUserModelAsync(
            userId,
            id,
            request.DisplayName,
            DisplayNameModes.Normalize(request.DisplayNameMode),
            request.ThinkingLevel,
            request.ReasoningMode,
            request.ReasoningSummary,
            request.ServiceTier,
            request.MaxInputTokens,
            request.MaxOutputTokens,
            request.ModelId,
            request.Provider,
            normalizedPricingMode,
            request.InputPricePerMillion,
            request.OutputPricePerMillion,
            request.CachedInputPricePerMillion
        );
        return Ok();
    }

    [HttpDelete("usermodels/{id}")]
    public async Task<IActionResult> DeleteUserModel(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _settingsService.DeleteUserModelAsync(userId, id);
        return Ok();
    }

    [HttpPut("usermodels/reorder")]
    public async Task<IActionResult> ReorderUserModels([FromBody] ReorderModelsRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();
        if (request.OrderedIds == null) return BadRequest();

        await _settingsService.ReorderUserModelsAsync(userId, request.OrderedIds);
        return Ok();
    }

    [HttpPut("systemmodels/reorder")]
    public async Task<IActionResult> ReorderUserSystemModels([FromBody] ReorderModelsRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();
        if (request.OrderedIds == null) return BadRequest();

        await _settingsService.ReorderUserSystemModelsAsync(userId, request.OrderedIds);
        return Ok();
    }

    [HttpPut("systemmodels/reorder/reset")]
    public async Task<IActionResult> ResetUserSystemModels()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _settingsService.ResetSystemModelsOrderAsync(userId);
        return Ok();
    }

    /// <summary>
    /// The model-listing URL for a provider at a given endpoint, and the auth to apply.
    /// </summary>
    /// <remarks>
    /// This is the third URL layer, outside the providers entirely. Left hardcoded to the
    /// official hosts, a deployment with a custom endpoint would validate its key against a
    /// host it never uses — and the bad outcome is not the failure, it is the *success*: a key
    /// that works against `api.openai.com` reports the Azure deployment healthy without ever
    /// having contacted it.
    /// </remarks>
    private static string ComposeModelsUrl(string provider, AiEndpointDescriptor endpoint, string apiKey)
    {
        if (!endpoint.IsCustom)
        {
            return provider switch
            {
                "Anthropic" => "https://api.anthropic.com/v1/models",
                "Google" => $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(apiKey)}",
                _ => "https://api.openai.com/v1/models"
            };
        }

        string baseUrl = endpoint.BaseUrl!.TrimEnd('/');

        if (provider == "Google")
        {
            /* No ?key= on a custom endpoint: it is rejected there, and a credential in a URL
               ends up in gateway access logs. The header carries it instead. */
            string googleUrl = $"{baseUrl}/v1beta/models";
            return endpoint.AuthStyle == AiEndpointAuthStyle.AzureApiKey && !string.IsNullOrWhiteSpace(endpoint.ApiVersion)
                ? googleUrl + "?api-version=" + Uri.EscapeDataString(endpoint.ApiVersion)
                : googleUrl;
        }

        if (provider == "Anthropic")
            return baseUrl + "/v1/models";

        // OpenAI, and every OpenAI-compatible gateway.
        if (endpoint.AuthStyle == AiEndpointAuthStyle.AzureApiKey)
        {
            return $"{baseUrl}/openai/v1/models?api-version="
                + Uri.EscapeDataString(endpoint.ApiVersion ?? string.Empty);
        }

        return baseUrl + "/v1/models";
    }

    /// <summary>Applies the endpoint's credential and allowlisted headers to a listing request.</summary>
    private static void ApplyModelsAuth(HttpClient client, string provider, AiEndpointDescriptor endpoint, string apiKey)
    {
        switch (endpoint.AuthStyle)
        {
            case AiEndpointAuthStyle.AzureApiKey:
                client.DefaultRequestHeaders.TryAddWithoutValidation("api-key", apiKey);
                break;

            case AiEndpointAuthStyle.BearerToken:
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                break;

            case AiEndpointAuthStyle.None:
                break;

            default:
                // The official public API of each provider, which all three do differently.
                if (provider == "Anthropic")
                    client.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", apiKey);
                else if (provider != "Google")
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                break;
        }

        if (provider == "Anthropic")
            client.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        if (endpoint.CustomHeaders != null)
        {
            foreach (var (name, value) in endpoint.CustomHeaders)
                client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
        }
    }

    /// <summary>
    /// The message for a failed model listing, distinguishing a custom endpoint that has no
    /// listing route from a credential that does not work.
    /// </summary>
    /// <remarks>
    /// The distinction is the point. An endpoint exposing no listing route is **not
    /// verifiable**, which is a different statement from "your key is wrong" — and neither may
    /// ever be reported as verified by having tested somewhere else.
    /// </remarks>
    private static string DescribeListingFailure(
        string provider, AiEndpointDescriptor endpoint, System.Net.HttpStatusCode statusCode, string body)
    {
        if (endpoint.IsCustom
            && (statusCode == System.Net.HttpStatusCode.NotFound
                || statusCode == System.Net.HttpStatusCode.MethodNotAllowed
                || statusCode == System.Net.HttpStatusCode.NotImplemented))
        {
            return $"The configured endpoint returned {(int)statusCode} for its model list, so the models "
                + "available there cannot be verified. This is not necessarily a problem: many gateways and "
                + "self-hosted servers expose no listing route. Enter the model id by hand. The key itself "
                + "has NOT been validated — Overseer will not test it against the public API instead.";
        }

        string where = endpoint.IsCustom ? "The configured endpoint" : $"{provider} API";
        return $"{where} returned {statusCode}: {body}";
    }

    [HttpPost("models")]
    public async Task<IActionResult> GetModels([FromBody] GetModelsRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var provider = request.Provider ?? "OpenAI";
        var apiKey = request.ApiKey;

        bool isAdmin = (await _authorizationService.AuthorizeAsync(User, "AdminOnly")).Succeeded;

        if (string.IsNullOrEmpty(apiKey) && request.SystemConfigId.HasValue && isAdmin)
        {
            apiKey = await _settingsService.GetDecryptedSystemApiKeyAsync(request.SystemConfigId.Value);
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = await _settingsService.GetDecryptedApiKeyForProviderAsync(userId, provider);
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            return BadRequest(new { message = "API Key is required to fetch models." });
        }

        /* The endpoint the listing must interrogate. Order matters: a saved system
           configuration's own endpoint wins, then one typed into the admin form (so an
           administrator can validate a key before saving), then the user's key -- which
           resolves to the official endpoint while user-supplied base URLs are off. */
        AiEndpointDescriptor endpoint = AiEndpointDescriptor.Official;
        if (request.SystemConfigId.HasValue && isAdmin)
        {
            var config = await _settingsService.GetSystemConfigurationAsync(request.SystemConfigId.Value);
            endpoint = _endpointPolicy.Resolve(config);
        }
        else if (!string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            if (!isAdmin)
                return Forbid();

            var endpointCheck = _endpointPolicy.ValidateWithoutDns(
                request.BaseUrl, request.CustomHeadersJson, request.ApiVersion);
            if (!endpointCheck.IsValid)
                return BadRequest(new { message = endpointCheck.Error });

            endpoint = _endpointPolicy.Resolve(request.BaseUrl, request.CustomHeadersJson, request.ApiVersion);
        }
        else
        {
            var providerKey = await _settingsService.GetApiKeyRowAsync(userId, provider);
            endpoint = _endpointPolicy.Resolve(providerKey);
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var models = new List<ApiModelDto>();
            string modelsUrl = ComposeModelsUrl(provider, endpoint, apiKey);
            ApplyModelsAuth(client, provider, endpoint, apiKey);



            if (provider == "OpenAI")
            {
                var response = await client.GetAsync(modelsUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest(new { message = DescribeListingFailure(provider, endpoint, response.StatusCode, await response.Content.ReadAsStringAsync()) });
                }
                var json = await response.Content.ReadAsStringAsync();
                var root = JsonDocument.Parse(json).RootElement;
                if (root.TryGetProperty("data", out var dataElement))
                {
                    foreach (var modelElement in dataElement.EnumerateArray())
                    {
                        if (modelElement.TryGetProperty("id", out var idElement))
                        {
                            var name = idElement.GetString() ?? "";
                            if (!_modelMetadataService.IsWhitelisted(provider, name))
                                continue;

                            var meta = _modelMetadataService.GetMetadata(provider, name);
                            long created = 0;
                            if (!string.IsNullOrEmpty(meta.ReleaseDate) && DateTimeOffset.TryParse(meta.ReleaseDate, out var dto))
                            {
                                created = dto.ToUnixTimeSeconds();
                            }
                            else if (modelElement.TryGetProperty("created", out var createdElement) && createdElement.ValueKind == JsonValueKind.Number)
                            {
                                created = createdElement.GetInt64();
                            }
                            
                            models.Add(new ApiModelDto { Id = name, DisplayName = meta.DisplayName, CreatedAt = created, Description = meta.Description, SupportedThinkingLevels = meta.SupportedThinkingLevels, SupportedReasoningModes = meta.SupportedReasoningModes, SupportedReasoningSummaries = meta.SupportedReasoningSummaries, ContextWindowSize = meta.ContextWindowSize, MaxInputTokens = meta.MaxInputTokens, MaxOutputTokens = meta.MaxOutputTokens, DefaultPricing = meta.DefaultPricing });
                        }
                    }
                }
            }
            else if (provider == "Anthropic")
            {
                var response = await client.GetAsync(modelsUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest(new { message = DescribeListingFailure(provider, endpoint, response.StatusCode, await response.Content.ReadAsStringAsync()) });
                }
                var json = await response.Content.ReadAsStringAsync();
                var root = JsonDocument.Parse(json).RootElement;
                if (root.TryGetProperty("data", out var dataElement))
                {
                    foreach (var modelElement in dataElement.EnumerateArray())
                    {
                        if (modelElement.TryGetProperty("id", out var idElement))
                        {
                            var name = idElement.GetString() ?? "";
                            if (!_modelMetadataService.IsWhitelisted(provider, name))
                                continue;

                            var meta = _modelMetadataService.GetMetadata(provider, name);
                            long created = 0;
                            if (!string.IsNullOrEmpty(meta.ReleaseDate) && DateTimeOffset.TryParse(meta.ReleaseDate, out var dtoDate))
                            {
                                created = dtoDate.ToUnixTimeSeconds();
                            }
                            else if (modelElement.TryGetProperty("created_at", out var createdElement))
                            {
                                var dateStr = createdElement.GetString();
                                if (!string.IsNullOrEmpty(dateStr) && DateTimeOffset.TryParse(dateStr, out var dto))
                                {
                                    created = dto.ToUnixTimeSeconds();
                                }
                            }
                            
                            var apiModelDto = new ApiModelDto { Id = name, DisplayName = meta.DisplayName, CreatedAt = created, Description = meta.Description, SupportedThinkingLevels = meta.SupportedThinkingLevels, SupportedReasoningModes = meta.SupportedReasoningModes, SupportedReasoningSummaries = meta.SupportedReasoningSummaries, ContextWindowSize = meta.ContextWindowSize, MaxInputTokens = meta.MaxInputTokens, MaxOutputTokens = meta.MaxOutputTokens, DefaultPricing = meta.DefaultPricing };

                            var anthropicDefaultEffort = _configuration.GetValue<string>("AnthropicSettings:ExplicitDefaultEffort") ?? "high";
                            // Left null when disabled: behaviour then reverts to the model's own API default, which
                            // differs per model and is exactly the case the UI must not claim to know.
                            if (!string.IsNullOrEmpty(anthropicDefaultEffort)
                                && !string.Equals(anthropicDefaultEffort, "none", StringComparison.OrdinalIgnoreCase))
                            {
                                apiModelDto.DefaultThinkingLevel = anthropicDefaultEffort;
                            }

                            models.Add(apiModelDto);
                        }
                    }
                }
            }
            else if (provider == "Google")
            {
                var response = await client.GetAsync(modelsUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest(new { message = DescribeListingFailure(provider, endpoint, response.StatusCode, await response.Content.ReadAsStringAsync()) });
                }
                var json = await response.Content.ReadAsStringAsync();
                var root = JsonDocument.Parse(json).RootElement;
                if (root.TryGetProperty("models", out var dataElement))
                {
                    foreach (var modelElement in dataElement.EnumerateArray())
                    {
                        if (modelElement.TryGetProperty("name", out var idElement))
                        {
                            var name = idElement.GetString() ?? "";
                            if (name.StartsWith("models/")) name = name.Substring(7);
                            
                            if (!_modelMetadataService.IsWhitelisted(provider, name))
                                continue;

                            var meta = _modelMetadataService.GetMetadata(provider, name);
                            long created = 0;
                            if (!string.IsNullOrEmpty(meta.ReleaseDate) && DateTimeOffset.TryParse(meta.ReleaseDate, out var dto))
                            {
                                created = dto.ToUnixTimeSeconds();
                            }
                            
                            models.Add(new ApiModelDto { Id = name, DisplayName = meta.DisplayName, CreatedAt = created, Description = meta.Description, SupportedThinkingLevels = meta.SupportedThinkingLevels, SupportedReasoningModes = meta.SupportedReasoningModes, SupportedReasoningSummaries = meta.SupportedReasoningSummaries, ContextWindowSize = meta.ContextWindowSize, MaxInputTokens = meta.MaxInputTokens, MaxOutputTokens = meta.MaxOutputTokens, DefaultPricing = meta.DefaultPricing });
                        }
                    }
                }
            }
            else
            {
                return BadRequest(new { message = $"Unsupported provider: {provider}" });
            }

            // Apply recommendation flags and supported service tiers
            var aiProvider = _aiProviders.FirstOrDefault(p => p.ProviderName.Equals(provider, StringComparison.OrdinalIgnoreCase));
            var supportedServiceTiers = aiProvider?.SupportedServiceTiers?.ToList() ?? new List<string>();
            foreach (var m in models)
            {
                m.SupportedServiceTiers = supportedServiceTiers;
            }

            var recommended = _recommendedModelService.GetRecommendedModels(provider);
            for (int i = 0; i < recommended.Count; i++)
            {
                var rec = recommended[i];
                var model = models.FirstOrDefault(m => m.Id == rec.Model);
                if (model != null)
                {
                    model.IsRecommended = true;
                    model.RecommendationRank = i + 1;
                    model.RecommendedThinkingLevel = rec.ThinkingLevel;
                }
            }

            return Ok(models);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Error fetching models: {ex.Message}" });
        }
    }
}

public class ApiModelDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> SupportedThinkingLevels { get; set; } = new();
    public List<string> SupportedReasoningModes { get; set; } = new();
    public List<string> SupportedReasoningSummaries { get; set; } = new();
    public List<string> SupportedServiceTiers { get; set; } = new();
    
    public int ContextWindowSize { get; set; }
    public int MaxInputTokens { get; set; }
    public int MaxOutputTokens { get; set; }
    
    public bool IsRecommended { get; set; }
    public int RecommendationRank { get; set; }
    public string? RecommendedThinkingLevel { get; set; }

    /// <summary>
    /// The effort level the provider will actually receive when the user selects "Default".
    /// Null when the effective behaviour is not knowable (non-Anthropic providers, or the
    /// kill switch is engaged) — the client then shows a plain "Default".
    /// </summary>
    public string? DefaultThinkingLevel { get; set; }

    public ModelCatalogPricing? DefaultPricing { get; set; }
}

/// <summary>
/// A partial update of the outbound-masking switches. Every field is nullable and null means
/// "leave alone".
/// </summary>
public class UpdateDlpSettingsRequest
{
    public bool? DlpMaskApiKeys { get; set; }
    public bool? DlpMaskPrivateKeys { get; set; }
    public bool? DlpMaskTokens { get; set; }
    public bool? DlpMaskCreditCards { get; set; }
    public bool? DlpMaskSsns { get; set; }
    public bool? DlpMaskEmails { get; set; }
    public bool? DlpMaskPhoneNumbers { get; set; }
}

public class UpdateSettingsRequest
{
    public bool? SpoilerFreeMode { get; set; }
    public int? MaxResultLength { get; set; }
    public int? MaxCallsPerSession { get; set; }
    public int? MaxToolIterations { get; set; }
    public int? MaxParallelToolCalls { get; set; }
    public bool? EnableWebSearch { get; set; }
    public bool? EnableToolUse { get; set; }
    public bool? EnableSubAgents { get; set; }
    public bool? EnableClientTools { get; set; }
    public bool? EnableGameActions { get; set; }
    public bool? ShowSourceCodeReferences { get; set; }
    public int? ShowThoughtsAndTools { get; set; }
    public bool? ShowParallelBadge { get; set; }
    public bool? ShowContextWindowUsage { get; set; }
    public bool? ShowChatCost { get; set; }
    public int? RequestTimeout { get; set; }

    /* A value weaker than the administrator's floor is not an error: the resolver clamps it at
       every read, so what applies is always the stricter of the two whatever is stored.

       Note what the client actually sends, though: it clamps to the floor before saving, so a
       preference weaker than the floor is not preserved and relaxing the floor later does not
       restore it. Storing the raw preference and clamping only on read would be the better
       behaviour and needs a client change, not a server one. */
    public string? ConfidentialPersistence { get; set; }
    public int? ConfidentialRetentionDays { get; set; }
    public bool? ConfidentialDisableToolEgress { get; set; }
    public bool? ConfidentialDisableTitleGeneration { get; set; }
    public bool? ConfidentialDisablePromptCache { get; set; }
    public bool? ConfidentialImmediatePurge { get; set; }
    public string? ConfidentialModelGate { get; set; }
}

public class SetApiKeyParallelModeRequest
{
    public int Mode { get; set; }
}

public class SaveApiKeyRequest
{
    public string Provider { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public class SetApiKeyEndpointRequest
{
    public string? BaseUrl { get; set; }
    public string? CustomHeadersJson { get; set; }
    public string? ApiVersion { get; set; }
}

public class SetApiKeyPostureRequest
{
    /// <summary>A ProviderConfidentialityPosture name, or null to clear the declaration.</summary>
    public string? Posture { get; set; }

    /// <summary>The user's own note about their provider account's terms. Max 1024 characters.</summary>
    public string? Note { get; set; }
}

public class SetApiKeyConfidentialTrustRequest
{
    /// <summary>True or false is a remembered decision; null returns the key to undecided.</summary>
    public bool? Trusted { get; set; }
}

public class AddUserModelRequest
{
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? DisplayNameMode { get; set; }
    public string? ThinkingLevel { get; set; }
    public string? ReasoningMode { get; set; }
    public string? ReasoningSummary { get; set; }
    public string? ServiceTier { get; set; }
    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public string? PricingMode { get; set; }
    public decimal? InputPricePerMillion { get; set; }
    public decimal? OutputPricePerMillion { get; set; }
    public decimal? CachedInputPricePerMillion { get; set; }
}

public class UpdateUserModelRequest
{
    public string? ModelId { get; set; }
    public string? Provider { get; set; }
    public string? DisplayName { get; set; }
    public string? DisplayNameMode { get; set; }
    public string? ThinkingLevel { get; set; }
    public string? ReasoningMode { get; set; }
    public string? ReasoningSummary { get; set; }
    public string? ServiceTier { get; set; }
    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public string? PricingMode { get; set; }
    public decimal? InputPricePerMillion { get; set; }
    public decimal? OutputPricePerMillion { get; set; }
    public decimal? CachedInputPricePerMillion { get; set; }
}

public class ReorderModelsRequest
{
    public long[] OrderedIds { get; set; } = Array.Empty<long>();
}

public class GetModelsRequest
{
    public string? Provider { get; set; }
    public string? ApiKey { get; set; }
    public long? SystemConfigId { get; set; }

    /* Administrator only, and validated before use: these let the admin model form check a
       key against the endpoint it will actually be used with, before the configuration is
       saved. A non-administrator supplying them is refused rather than ignored. */
    public string? BaseUrl { get; set; }
    public string? CustomHeadersJson { get; set; }
    public string? ApiVersion { get; set; }
}

public class UpdateTitleModelRequest
{
    public long? ModelId { get; set; }
    public bool IsSystem { get; set; }
    public bool? Disabled { get; set; }
}


using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Extensions;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Privacy;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CryptoService _cryptoService;
    private readonly Overseer.Services.Providers.AiRequestGovernor _governor;
    private readonly ModelPricingService? _modelPricingService;

    private readonly Overseer.Services.Privacy.EndpointPolicy _endpointPolicy;
    private readonly SystemConfigUsageGuard _usageGuard;
    private ModelAvailabilityService? _modelAvailabilityService;
    private ModelResolutionService? _modelResolutionService;
    private ModelMetadataService? _metadataService;

    /// <param name="modelAvailabilityService">Built from a fresh <see cref="ModelMetadataService"/> on first use when null.</param>
    /// <param name="modelResolutionService">Built from a fresh <see cref="ModelMetadataService"/> on first use when null.</param>
    /// <param name="modelMetadataService">A fresh instance is created on first use when null.</param>
    public AdminController(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        UserManager<ApplicationUser> userManager,
        CryptoService cryptoService,
        Overseer.Services.Providers.AiRequestGovernor governor,
        Overseer.Services.Privacy.EndpointPolicy endpointPolicy,
        SystemConfigUsageGuard usageGuard,
        ModelPricingService? modelPricingService = null,
        ModelAvailabilityService? modelAvailabilityService = null,
        ModelResolutionService? modelResolutionService = null,
        ModelMetadataService? modelMetadataService = null)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _userManager = userManager;
        _cryptoService = cryptoService;
        _governor = governor;
        _endpointPolicy = endpointPolicy;
        _usageGuard = usageGuard;
        _modelPricingService = modelPricingService;
        _modelAvailabilityService = modelAvailabilityService;
        _modelResolutionService = modelResolutionService;
        _metadataService = modelMetadataService;
    }

    private ModelMetadataService Metadata =>
        _metadataService ??= new ModelMetadataService();

    private ModelAvailabilityService AvailabilityService =>
        _modelAvailabilityService ??= new ModelAvailabilityService(Metadata);

    private ModelResolutionService ResolutionService =>
        _modelResolutionService ??= new ModelResolutionService(Metadata);

    private static readonly System.Collections.Generic.HashSet<string> AllowedCounters = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "DailyChatRequestsCount", "MonthlyChatRequestsCount", "TotalChatRequestsCount",
        "DailyTitleRequestsCount", "MonthlyTitleRequestsCount", "TotalTitleRequestsCount",
        "DailyChatTokensCount", "MonthlyChatTokensCount", "TotalChatTokensCount",
        "DailyTitleTokensCount", "MonthlyTitleTokensCount", "TotalTitleTokensCount"
    };

    // --- Groups ---

    [HttpGet("groups")]
    public async Task<IActionResult> GetGroups()
    {
        var groups = await _dbContext.Groups
            .Select(g => new AdminGroupDto
            {
                Id = g.Id,
                DisplayName = g.DisplayName,
                CreatedAtUtc = g.CreatedAtUtc,
                UserCount = _dbContext.UserGroups.Count(ug => ug.GroupId == g.Id)
            })
            .ToListAsync();

        return Ok(groups);
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
    {
        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return BadRequest("Group name cannot be empty.");
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(displayName, @"^[a-zA-Z0-9 _\-]+$"))
        {
            return BadRequest("Group name contains invalid characters.");
        }

        var group = new Group
        {
            DisplayName = displayName,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Groups.Add(group);
        await _dbContext.SaveChangesAsync();

        return Ok(new AdminGroupDto
        {
            Id = group.Id,
            DisplayName = group.DisplayName,
            CreatedAtUtc = group.CreatedAtUtc,
            UserCount = 0
        });
    }

    [HttpDelete("groups/{id}")]
    public async Task<IActionResult> DeleteGroup(long id)
    {
        var group = await _dbContext.Groups.FindAsync(id);
        if (group == null) return NotFound();

        _dbContext.Groups.Remove(group);
        await _dbContext.SaveChangesAsync();

        return Ok();
    }

    // --- Users ---

    private string? AdminUserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    private IActionResult ToActionResult(Overseer.Services.Security.ReviewActionResult result) => result.Status switch
    {
        Overseer.Services.Security.ReviewActionStatus.Ok => result.Decision != null ? Ok(result.Decision) : Ok(),
        Overseer.Services.Security.ReviewActionStatus.NotFound => NotFound(new { message = result.Message }),
        Overseer.Services.Security.ReviewActionStatus.Conflict => Conflict(new { message = result.Message }),
        _ => BadRequest(new { message = result.Message })
    };

    // --- Account deletion requests ---

    [HttpGet("account-deletion-requests")]
    public async Task<IActionResult> GetAccountDeletionRequests([FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int skip, [FromQuery] int take, [FromServices] Overseer.Services.Security.AccountDeletionReviewService review)
        => Ok(await review.ListAsync(status, search, skip, take));

    /// <summary>Erases the account; 400 when the typed name does not match, 409 while the erasure paths are missing.</summary>
    [HttpPost("account-deletion-requests/{id:long}/approve")]
    public async Task<IActionResult> ApproveAccountDeletion(long id, [FromBody] ApproveAccountDeletionRequest request,
        [FromServices] Overseer.Services.Security.AccountDeletionReviewService review)
        => ToActionResult(await review.ApproveAsync(id, request?.ConfirmUserName, AdminUserId ?? string.Empty));

    [HttpPost("account-deletion-requests/{id:long}/decline")]
    public async Task<IActionResult> DeclineAccountDeletion(long id, [FromBody] DeclineAccountDeletionRequest request,
        [FromServices] Overseer.Services.Security.AccountDeletionReviewService review)
        => ToActionResult(await review.DeclineAsync(id, request?.Note, AdminUserId ?? string.Empty));

    // --- Security events ---

    [HttpGet("security/summary")]
    public async Task<IActionResult> GetSecuritySummary([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromServices] Overseer.Services.Security.SecurityQueryService security)
        => Ok(await security.GetSummaryAsync(startDate, endDate));

    [HttpGet("security/events")]
    public async Task<IActionResult> GetSecurityEvents([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] string? kinds, [FromQuery] string? userId, [FromQuery] string? search, [FromQuery] int skip, [FromQuery] int take,
        [FromServices] Overseer.Services.Security.SecurityQueryService security)
        => Ok(await security.GetEventsAsync(startDate, endDate, kinds, userId, search, skip, take));

    /// <summary>The user a provider safety identifier belongs to; 404 when none.</summary>
    [HttpGet("security/safety-identifier/{identifier}")]
    public async Task<IActionResult> LookupSafetyIdentifier(string identifier,
        [FromServices] Overseer.Services.Security.SafetyIdentifierService safetyIdentifiers)
    {
        string? userId = await safetyIdentifiers.FindUserIdAsync(identifier);
        if (userId == null) return NotFound(new { message = "No user has this identifier." });
        string? userName = await _dbContext.Users.Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync();
        return Ok(new SafetyIdentifierLookupDto { UserId = userId, UserName = userName ?? string.Empty });
    }

    /// <summary>The snapshot guard's mode, who set it and when, and the configured block rule.</summary>
    [HttpGet("security/snapshot-guard")]
    public async Task<IActionResult> GetSnapshotGuard([FromServices] Overseer.Services.Security.SnapshotGuardModeService modes)
        => Ok(await SnapshotGuardDtoAsync(await modes.GetStateAsync(HttpContext.RequestAborted)));

    /// <summary>Sets the snapshot guard's mode, appending a setting even when it is unchanged; 400 for an unknown mode.</summary>
    [HttpPost("security/snapshot-guard")]
    public async Task<IActionResult> SetSnapshotGuard([FromBody] AdminSnapshotGuardUpdateDto? request,
        [FromServices] Overseer.Services.Security.SnapshotGuardModeService modes)
    {
        if (SnapshotGuardOptions.ParseMode(request?.Mode) is not SnapshotGuardMode mode)
        {
            return BadRequest(new { message = "The mode must be Off, Observe or Enforce." });
        }
        var state = await modes.SetAsync(mode, AdminUserId, HttpContext.RequestAborted);
        return Ok(await SnapshotGuardDtoAsync(state));
    }

    private async Task<AdminSnapshotGuardDto> SnapshotGuardDtoAsync(Overseer.Services.Security.SnapshotGuardModeState state)
    {
        var options = SnapshotGuardOptions.Read(_configuration);
        string? setBy = state.SetByUserId == null
            ? null
            : await _dbContext.Users.AsNoTracking().Where(u => u.Id == state.SetByUserId).Select(u => u.UserName).FirstOrDefaultAsync() ?? state.SetByUserId;
        return new AdminSnapshotGuardDto
        {
            Mode = state.Mode.ToString(),
            SetAtUtc = state.SetAtUtc,
            SetByUserName = setBy,
            Seeded = state.Seeded,
            AutoBlockEnabled = options.AutoBlockEnabled,
            AutoBlockDistinctDays = options.AutoBlockDistinctDays,
            AutoBlockWindowDays = options.AutoBlockWindowDays,
            NotificationEmailConfigured = options.NotificationEmail != null
        };
    }

    [HttpPost("users/{userId}/groups")]
    public async Task<IActionResult> AssignGroupToUser(string userId, [FromBody] AssignGroupRequest request)
    {
        var exists = await _dbContext.UserGroups.AnyAsync(ug => ug.AspNetUserId == userId && ug.GroupId == request.GroupId);
        if (!exists)
        {
            _dbContext.UserGroups.Add(new UserGroup { AspNetUserId = userId, GroupId = request.GroupId });
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }

    [HttpDelete("users/{userId}/groups/{groupId}")]
    public async Task<IActionResult> RemoveGroupFromUser(string userId, long groupId)
    {
        var ug = await _dbContext.UserGroups.FirstOrDefaultAsync(u => u.AspNetUserId == userId && u.GroupId == groupId);
        if (ug != null)
        {
            _dbContext.UserGroups.Remove(ug);
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }

    // --- System AI Configurations ---

    [HttpGet("systemconfigs")]
    public async Task<IActionResult> GetSystemConfigs()
    {
        var userAssignmentCounts = await _dbContext.UserSystemAiApiConfigurations
            .GroupBy(u => u.SystemAiApiConfigurationId)
            .Select(g => new { ConfigId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConfigId, x => x.Count);

        var groupAssignmentCounts = await _dbContext.GroupSystemAiApiConfigurations
            .GroupBy(g => g.SystemAiApiConfigurationId)
            .Select(g => new { ConfigId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConfigId, x => x.Count);

        var entities = await _dbContext.SystemAiApiConfigurations
            .AsNoTracking()
            .OrderBy(c => c.OrderIndex)
            .ToListAsync();

        var configs = entities
            .Select(c => ToSystemConfigDto(
                c,
                userAssignmentCounts.GetValueOrDefault(c.Id, 0),
                groupAssignmentCounts.GetValueOrDefault(c.Id, 0)))
            .ToList();

        return Ok(configs);
    }

    /// <summary>A system configuration as the System Configs tab lists it, with its effective pricing and model availability.</summary>
    private SystemAiApiConfigurationDto ToSystemConfigDto(SystemAiApiConfiguration c, int userAssignmentCount, int groupAssignmentCount)
    {
        var dto = new SystemAiApiConfigurationDto
        {
            Id = c.Id,
            DisplayName = c.DisplayName,
            DisplayNameMode = c.DisplayNameMode,
            Provider = c.Provider,
            ModelId = c.ModelId,
            ReleaseDate = Metadata.GetReleaseDate(c.Provider, c.ModelId),
            ConfidentialityPosture = c.ConfidentialityPosture,
            ConfidentialityNote = c.ConfidentialityNote,
            PostureAgreementRef = c.PostureAgreementRef,
            PostureVerifiedUtc = c.PostureVerifiedUtc,
            DataRegion = c.DataRegion,
            BaseUrl = c.BaseUrl,
            CustomHeadersJson = c.CustomHeadersJson,
            ApiVersion = c.ApiVersion,
            ThinkingLevel = c.ThinkingLevel,
            ReasoningMode = c.ReasoningMode,
            ReasoningSummary = c.ReasoningSummary,
            ServiceTier = c.ServiceTier,
            MaxInputTokens = c.MaxInputTokens,
            MaxOutputTokens = c.MaxOutputTokens,
            OrderIndex = c.OrderIndex,
            IsEnabled = c.IsEnabled,
            HasApiKey = !string.IsNullOrEmpty(c.EncryptedApiKey),
            UseDefaultApiKey = c.UseDefaultApiKey,
            IsSystemWide = c.IsSystemWide,
            MaxDailyChatRequests = c.MaxDailyChatRequests,
            MaxMonthlyChatRequests = c.MaxMonthlyChatRequests,
            MaxTotalChatRequests = c.MaxTotalChatRequests,
            MaxDailyTitleRequests = c.MaxDailyTitleRequests,
            MaxMonthlyTitleRequests = c.MaxMonthlyTitleRequests,
            MaxTotalTitleRequests = c.MaxTotalTitleRequests,
            MaxDailyChatTokens = c.MaxDailyChatTokens,
            MaxMonthlyChatTokens = c.MaxMonthlyChatTokens,
            MaxTotalChatTokens = c.MaxTotalChatTokens,
            MaxDailyTitleTokens = c.MaxDailyTitleTokens,
            MaxMonthlyTitleTokens = c.MaxMonthlyTitleTokens,
            MaxTotalTitleTokens = c.MaxTotalTitleTokens,
            DailyChatRequestsCount = c.DailyChatRequestsCount,
            MonthlyChatRequestsCount = c.MonthlyChatRequestsCount,
            TotalChatRequestsCount = c.TotalChatRequestsCount,
            DailyTitleRequestsCount = c.DailyTitleRequestsCount,
            MonthlyTitleRequestsCount = c.MonthlyTitleRequestsCount,
            TotalTitleRequestsCount = c.TotalTitleRequestsCount,
            DailyChatTokensCount = c.DailyChatTokensCount,
            MonthlyChatTokensCount = c.MonthlyChatTokensCount,
            TotalChatTokensCount = c.TotalChatTokensCount,
            DailyTitleTokensCount = c.DailyTitleTokensCount,
            MonthlyTitleTokensCount = c.MonthlyTitleTokensCount,
            TotalTitleTokensCount = c.TotalTitleTokensCount,
            ModelRole = c.ModelRole,
            ParallelExecutionMode = (int)c.ParallelExecutionMode,
            Note = c.Note,
            PricingMode = c.PricingMode,
            InputPricePerMillion = c.InputPricePerMillion,
            OutputPricePerMillion = c.OutputPricePerMillion,
            CachedInputPricePerMillion = c.CachedInputPricePerMillion,
            ModelCatalogMode = ModelCatalogModes.Normalize(c.ModelCatalogMode),
            UserAssignmentCount = userAssignmentCount,
            GroupAssignmentCount = groupAssignmentCount
        };

        ModelPricing? resolved = null;
        if (_modelPricingService != null)
        {
            resolved = _modelPricingService.Resolve(c);
            if (resolved != null)
            {
                dto.EffectiveInputPricePerMillion = resolved.InputPerMillion;
                dto.EffectiveOutputPricePerMillion = resolved.OutputPerMillion;
                dto.EffectiveCachedInputPricePerMillion = resolved.CachedInputPerMillion;
                dto.PricingSource = resolved.Source == ModelPricingSource.Custom ? "custom" : "catalog";
                dto.PricingAsOf = resolved.AsOf;
                dto.EffectiveLongContextThresholdTokens = resolved.LongContext?.ThresholdInputTokens;
                dto.EffectiveLongContextInputPricePerMillion = resolved.LongContext?.InputPerMillion;
                dto.EffectiveLongContextOutputPricePerMillion = resolved.LongContext?.OutputPerMillion;
                dto.EffectiveServiceTierMultipliers = resolved.ServiceTierMultipliers;
                dto.PricingScheduledChangeFrom = resolved.ScheduledChange?.EffectiveFrom.ToString("yyyy-MM-dd");
                dto.PricingScheduledChangeInputPricePerMillion = resolved.ScheduledChange?.InputPerMillion;
                dto.PricingScheduledChangeOutputPricePerMillion = resolved.ScheduledChange?.OutputPerMillion;
                dto.PricingScheduledChangeNote = resolved.ScheduledChange?.Note;
                dto.PricingScheduleElapsed = resolved.ScheduleElapsed;
            }
            else
            {
                dto.PricingSource = "unknown";
            }
        }

        dto.ModelAvailability = SettingsService.WithRowSuggestion(
            AvailabilityService.Evaluate(c.Provider, c.ModelId, c.ModelCatalogMode, c.BaseUrl),
            c.MaxInputTokens, c.MaxOutputTokens, resolved);

        return dto;
    }

    /// <summary>
    /// What the custom-endpoint policy permits on this server, for the admin model form.
    /// </summary>
    [HttpGet("endpoint-policy")]
    public IActionResult GetEndpointPolicy()
    {
        var summary = _endpointPolicy.Summary();
        return Ok(new EndpointPolicySummaryDto
        {
            CustomEndpointsEnabled = summary.CustomEndpointsEnabled,
            AllowedHostPatterns = summary.AllowedHostPatterns.ToList(),
            AllowedHeaderNames = summary.AllowedHeaderNames.ToList(),
            AllowLoopback = summary.AllowLoopback
        });
    }

    [HttpPost("systemconfigs")]
    public async Task<IActionResult> CreateSystemConfig(
        [FromBody] CreateSystemAiApiConfigurationRequest request, CancellationToken ct = default)
    {
        if (request.ModelRole < 1 || request.ModelRole > 7)
        {
            return BadRequest("ModelRole must be between 1 and 7.");
        }

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

        if (!TryNormalizePosture(request.ConfidentialityPosture, out var newPosture, out var postureError))
            return BadRequest(postureError);

        if (request.UseDefaultApiKey && !string.IsNullOrWhiteSpace(request.BaseUrl))
            return BadRequest(DefaultKeyNeedsOfficialEndpoint);

        /* Full validation, DNS included, because this is an administrator saving a
           configuration rather than a per-turn resolve. An endpoint that fails here is
           refused; one that is stored and later stops validating falls back to the public
           endpoint and is reported by ConfigHealthService. */
        var endpointCheck = _endpointPolicy.Validate(
            request.BaseUrl, request.CustomHeadersJson, request.ApiVersion);
        if (!endpointCheck.IsValid)
            return BadRequest(endpointCheck.Error);

        try
        {
            var orderIndex = await _dbContext.SystemAiApiConfigurations.AnyAsync() 
                ? await _dbContext.SystemAiApiConfigurations.MaxAsync(c => c.OrderIndex) + 1 
                : 0;

            var config = new SystemAiApiConfiguration
            {
                DisplayName = request.DisplayName,
                DisplayNameMode = DisplayNameModes.Normalize(request.DisplayNameMode),
                Provider = request.Provider,
                ModelId = request.ModelId,
                ConfidentialityPosture = newPosture,
                ConfidentialityNote = request.ConfidentialityNote,
                PostureAgreementRef = request.PostureAgreementRef,
                PostureVerifiedUtc = request.PostureVerifiedUtc,
                DataRegion = request.DataRegion,
                BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl.Trim(),
                CustomHeadersJson = string.IsNullOrWhiteSpace(request.CustomHeadersJson) ? null : request.CustomHeadersJson,
                ApiVersion = string.IsNullOrWhiteSpace(request.ApiVersion) ? null : request.ApiVersion.Trim(),
                ThinkingLevel = request.ThinkingLevel,
                ReasoningMode = request.ReasoningMode,
                ReasoningSummary = request.ReasoningSummary,
                ServiceTier = request.ServiceTier,
                MaxInputTokens = request.MaxInputTokens,
                MaxOutputTokens = request.MaxOutputTokens,
                IsEnabled = request.IsEnabled,
                IsSystemWide = request.IsSystemWide,
                MaxDailyChatRequests = request.MaxDailyChatRequests,
                MaxMonthlyChatRequests = request.MaxMonthlyChatRequests,
                MaxTotalChatRequests = request.MaxTotalChatRequests,
                MaxDailyTitleRequests = request.MaxDailyTitleRequests,
                MaxMonthlyTitleRequests = request.MaxMonthlyTitleRequests,
                MaxTotalTitleRequests = request.MaxTotalTitleRequests,
                MaxDailyChatTokens = request.MaxDailyChatTokens,
                MaxMonthlyChatTokens = request.MaxMonthlyChatTokens,
                MaxTotalChatTokens = request.MaxTotalChatTokens,
                MaxDailyTitleTokens = request.MaxDailyTitleTokens,
                MaxMonthlyTitleTokens = request.MaxMonthlyTitleTokens,
                MaxTotalTitleTokens = request.MaxTotalTitleTokens,
                ModelRole = request.ModelRole,
                ParallelExecutionMode = (MobileGnollHackLogger.Data.ParallelExecutionMode)request.ParallelExecutionMode,
                OrderIndex = orderIndex,
                Note = request.Note,
                PricingMode = normalizedPricingMode,
                InputPricePerMillion = request.InputPricePerMillion,
                OutputPricePerMillion = request.OutputPricePerMillion,
                CachedInputPricePerMillion = request.CachedInputPricePerMillion,
                UseDefaultApiKey = request.UseDefaultApiKey,
                ModelCatalogMode = AvailabilityService.DeriveCatalogMode(request.Provider, request.ModelId)
            };

            if (request.UseDefaultApiKey)
            {
                if (!await SystemDefaultApiKeyService.ApplyDefaultKeyAsync(_dbContext, _cryptoService, config, ct))
                    return BadRequest(NoDefaultKeyMessage(config.Provider));
            }
            else if (!string.IsNullOrWhiteSpace(request.ApiKey))
            {
                EncryptApiKey(config, request.ApiKey);
            }

            _dbContext.SystemAiApiConfigurations.Add(config);
            await _dbContext.SaveChangesAsync();

            return Ok(new { id = config.Id });
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpPut("systemconfigs/{id}")]
    public async Task<IActionResult> UpdateSystemConfig(
        long id, [FromBody] UpdateSystemAiApiConfigurationRequest request, CancellationToken ct = default)
    {
        if (request.ModelRole < 1 || request.ModelRole > 7)
        {
            return BadRequest("ModelRole must be between 1 and 7.");
        }

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

        if (!TryNormalizePosture(request.ConfidentialityPosture, out var updatedPosture, out var postureError))
            return BadRequest(postureError);

        if (request.UseDefaultApiKey && !string.IsNullOrWhiteSpace(request.BaseUrl))
            return BadRequest(DefaultKeyNeedsOfficialEndpoint);

        var endpointCheck = _endpointPolicy.Validate(
            request.BaseUrl, request.CustomHeadersJson, request.ApiVersion);
        if (!endpointCheck.IsValid)
            return BadRequest(endpointCheck.Error);

        try
        {
            var config = await _dbContext.SystemAiApiConfigurations.FindAsync(id);
            if (config == null) return NotFound();

            bool wasUsingDefaultKey = config.UseDefaultApiKey;
            bool modelChanged = !string.Equals(config.Provider, request.Provider, StringComparison.Ordinal)
                || !string.Equals(config.ModelId, request.ModelId, StringComparison.Ordinal);

            config.DisplayName = request.DisplayName;
            config.DisplayNameMode = DisplayNameModes.Normalize(request.DisplayNameMode);
            config.Provider = request.Provider;
            config.ModelId = request.ModelId;

            /* Only a new model re-derives the catalog mode, so saving another field of a flagged
               configuration does not resolve it. */
            if (modelChanged)
                config.ModelCatalogMode = AvailabilityService.DeriveCatalogMode(config.Provider, config.ModelId);
            config.ConfidentialityPosture = updatedPosture;
            config.ConfidentialityNote = request.ConfidentialityNote;
            config.PostureAgreementRef = request.PostureAgreementRef;
            /* Assigned unconditionally, clearing included: withdrawing verification is a thing an
               operator must be able to do, and a null that silently kept the old date would leave
               a green badge standing on an agreement that has lapsed. */
            config.PostureVerifiedUtc = request.PostureVerifiedUtc;
            config.DataRegion = request.DataRegion;
            config.BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl.Trim();
            config.CustomHeadersJson = string.IsNullOrWhiteSpace(request.CustomHeadersJson) ? null : request.CustomHeadersJson;
            config.ApiVersion = string.IsNullOrWhiteSpace(request.ApiVersion) ? null : request.ApiVersion.Trim();
            config.ThinkingLevel = request.ThinkingLevel;
            config.ReasoningMode = request.ReasoningMode;
            config.ReasoningSummary = request.ReasoningSummary;
            config.ServiceTier = request.ServiceTier;
            config.MaxInputTokens = request.MaxInputTokens;
            config.MaxOutputTokens = request.MaxOutputTokens;
            config.IsEnabled = request.IsEnabled;
            config.IsSystemWide = request.IsSystemWide;
            config.MaxDailyChatRequests = request.MaxDailyChatRequests;
            config.MaxMonthlyChatRequests = request.MaxMonthlyChatRequests;
            config.MaxTotalChatRequests = request.MaxTotalChatRequests;
            config.MaxDailyTitleRequests = request.MaxDailyTitleRequests;
            config.MaxMonthlyTitleRequests = request.MaxMonthlyTitleRequests;
            config.MaxTotalTitleRequests = request.MaxTotalTitleRequests;
            config.MaxDailyChatTokens = request.MaxDailyChatTokens;
            config.MaxMonthlyChatTokens = request.MaxMonthlyChatTokens;
            config.MaxTotalChatTokens = request.MaxTotalChatTokens;
            config.MaxDailyTitleTokens = request.MaxDailyTitleTokens;
            config.MaxMonthlyTitleTokens = request.MaxMonthlyTitleTokens;
            config.MaxTotalTitleTokens = request.MaxTotalTitleTokens;
            config.ModelRole = request.ModelRole;
            config.ParallelExecutionMode = (MobileGnollHackLogger.Data.ParallelExecutionMode)request.ParallelExecutionMode;
            config.Note = request.Note;
            config.PricingMode = normalizedPricingMode;
            config.InputPricePerMillion = request.InputPricePerMillion;
            config.OutputPricePerMillion = request.OutputPricePerMillion;
            config.CachedInputPricePerMillion = request.CachedInputPricePerMillion;
            config.UseDefaultApiKey = request.UseDefaultApiKey;

            if (request.UseDefaultApiKey)
            {
                // A fresh copy of the default key of the provider the configuration now has.
                if (!await SystemDefaultApiKeyService.ApplyDefaultKeyAsync(_dbContext, _cryptoService, config, ct))
                    return BadRequest(NoDefaultKeyMessage(config.Provider));
            }
            else if (wasUsingDefaultKey)
            {
                // The copied default key never stays behind as a custom key.
                if (string.IsNullOrWhiteSpace(request.ApiKey))
                    ClearApiKey(config);
                else
                    EncryptApiKey(config, request.ApiKey);
            }
            else if (request.ApiKey != null)
            {
                if (string.IsNullOrWhiteSpace(request.ApiKey))
                {
                    ClearApiKey(config);
                }
                else
                {
                    EncryptApiKey(config, request.ApiKey);
                }
            }

            await _dbContext.SaveChangesAsync();

            return Ok();
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpPost("systemconfigs/{id}/reset")]
    public async Task<IActionResult> ResetSystemConfigRateLimits(long id, [FromBody] ResetCounterRequest? request = null)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(id);
        if (config == null) return NotFound();

        if (request?.CounterName != null)
        {
            if (!AllowedCounters.Contains(request.CounterName))
            {
                return BadRequest("Invalid counter name.");
            }
            var prop = config.GetType().GetProperty(request.CounterName);
            if (prop != null && prop.CanWrite)
            {
                if (prop.PropertyType == typeof(int)) prop.SetValue(config, 0);
                else if (prop.PropertyType == typeof(long)) prop.SetValue(config, 0L);
            }
        }
        else
        {
            config.DailyChatRequestsCount = 0;
            config.MonthlyChatRequestsCount = 0;
            config.TotalChatRequestsCount = 0;
            config.DailyTitleRequestsCount = 0;
            config.MonthlyTitleRequestsCount = 0;
            config.TotalTitleRequestsCount = 0;
            config.DailyChatTokensCount = 0;
            config.MonthlyChatTokensCount = 0;
            config.TotalChatTokensCount = 0;
            config.DailyTitleTokensCount = 0;
            config.MonthlyTitleTokensCount = 0;
            config.TotalTitleTokensCount = 0;
        }
        
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// Switches the configuration to a catalog model, or keeps it as a custom model. A switch is refused
    /// while something is calling the configuration's model; a dry run lists those blockers instead and
    /// saves nothing. Keeping a model as custom is never refused for use: a run has its prices snapshotted.
    /// </summary>
    [HttpPost("systemconfigs/{id}/model-resolution")]
    public async Task<IActionResult> ResolveSystemConfigModel(
        long id, [FromBody] ModelResolutionRequest request, CancellationToken ct = default)
    {
        try
        {
            var config = await _dbContext.SystemAiApiConfigurations.FindAsync(new object[] { id }, ct);
            if (config == null) return NotFound();

            var outcome = ResolutionService.Resolve(ModelSettings.FromSystemConfig(config), request);
            if (outcome.IsRefused) return BadRequest(new { message = outcome.Refusal });

            bool isSwitch = string.Equals(request.Action, ModelResolutionActions.Switch, StringComparison.OrdinalIgnoreCase);
            var blockers = isSwitch
                ? await _usageGuard.FindActiveUsesAsync(id, ct)
                : new List<SystemConfigBlockerDto>();

            SystemAiApiConfiguration resolved;
            if (request.DryRun)
            {
                resolved = (SystemAiApiConfiguration)_dbContext.Entry(config).CurrentValues.ToObject();
                outcome.Updated!.ApplyTo(resolved);
            }
            else
            {
                if (blockers.Count > 0)
                {
                    return Conflict(new
                    {
                        message = $"'{config.DisplayName}' is in use by a benchmark right now and cannot switch models until it finishes or is canceled.",
                        blockers
                    });
                }

                outcome.Updated!.ApplyTo(config);
                await _dbContext.SaveChangesAsync(ct);
                resolved = config;
            }

            int userAssignmentCount = await _dbContext.UserSystemAiApiConfigurations
                .CountAsync(a => a.SystemAiApiConfigurationId == id, ct);
            int groupAssignmentCount = await _dbContext.GroupSystemAiApiConfigurations
                .CountAsync(a => a.SystemAiApiConfigurationId == id, ct);

            return Ok(new ModelResolutionResult
            {
                Changes = outcome.Changes.ToList(),
                Blockers = request.DryRun ? blockers : new List<SystemConfigBlockerDto>(),
                Model = ToSystemConfigDto(resolved, userAssignmentCount, groupAssignmentCount)
            });
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Whether the configuration can be deleted now, what is using it, and what a delete removes and keeps.</summary>
    [HttpGet("systemconfigs/{id}/deletion-check")]
    public async Task<IActionResult> GetSystemConfigDeletionCheck(long id, CancellationToken ct)
    {
        try
        {
            var config = await _dbContext.SystemAiApiConfigurations.FindAsync(new object[] { id }, ct);
            if (config == null) return NotFound();

            return Ok(await _usageGuard.CheckDeletionAsync(config, ct));
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Deletes the configuration unless something is calling its model right now. Benchmark history
    /// and usage and error logs are kept: none of them depends on the configuration row.
    /// </summary>
    [HttpDelete("systemconfigs/{id}")]
    public async Task<IActionResult> DeleteSystemConfig(long id)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(id);
        if (config == null) return NotFound();

        // Checked again here, not only in the dialog: a run may have started since.
        var blockers = await _usageGuard.FindActiveUsesAsync(id);
        if (blockers.Count > 0)
        {
            return Conflict(new
            {
                error = $"'{config.DisplayName}' is in use by a benchmark right now and cannot be deleted until it finishes or is cancelled.",
                blockers
            });
        }

        _dbContext.SystemAiApiConfigurations.Remove(config);
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPut("systemconfigs/reorder")]
    public async Task<IActionResult> ReorderSystemConfigs([FromBody] ReorderRequest request)
    {
        var configs = await _dbContext.SystemAiApiConfigurations.ToListAsync();
        var idDict = configs.ToDictionary(c => c.Id);

        for (int i = 0; i < request.OrderedIds.Length; i++)
        {
            if (idDict.TryGetValue(request.OrderedIds[i], out var config))
            {
                config.OrderIndex = i;
            }
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    // --- User System Config Assignments ---

    [HttpGet("users/{userId}/systemconfigs")]
    public async Task<IActionResult> GetUserSystemConfigs(string userId)
    {
        var assignments = await _dbContext.UserSystemAiApiConfigurations
            .Where(a => a.AspNetUserId == userId)
            .Select(a => new UserSystemAiConfigDto
            {
                Id = a.Id,
                UserId = a.AspNetUserId,
                SystemAiApiConfigurationId = a.SystemAiApiConfigurationId,
                IsEnabled = a.IsEnabled,
                OrderIndex = _dbContext.UserSystemModelOrders
                    .Where(o => o.AspNetUserId == userId && o.SystemAiApiConfigurationId == a.SystemAiApiConfigurationId)
                    .Select(o => (int?)o.OrderIndex)
                    .FirstOrDefault() ?? a.OrderIndex,
                MaxDailyChatRequests = a.MaxDailyChatRequests,
                MaxMonthlyChatRequests = a.MaxMonthlyChatRequests,
                MaxTotalChatRequests = a.MaxTotalChatRequests,
                MaxDailyTitleRequests = a.MaxDailyTitleRequests,
                MaxMonthlyTitleRequests = a.MaxMonthlyTitleRequests,
                MaxTotalTitleRequests = a.MaxTotalTitleRequests,
                MaxDailyChatTokens = a.MaxDailyChatTokens,
                MaxMonthlyChatTokens = a.MaxMonthlyChatTokens,
                MaxTotalChatTokens = a.MaxTotalChatTokens,
                MaxDailyTitleTokens = a.MaxDailyTitleTokens,
                MaxMonthlyTitleTokens = a.MaxMonthlyTitleTokens,
                MaxTotalTitleTokens = a.MaxTotalTitleTokens,
                DailyChatRequestsCount = a.DailyChatRequestsCount,
                MonthlyChatRequestsCount = a.MonthlyChatRequestsCount,
                TotalChatRequestsCount = a.TotalChatRequestsCount,
                DailyTitleRequestsCount = a.DailyTitleRequestsCount,
                MonthlyTitleRequestsCount = a.MonthlyTitleRequestsCount,
                TotalTitleRequestsCount = a.TotalTitleRequestsCount,
                DailyChatTokensCount = a.DailyChatTokensCount,
                MonthlyChatTokensCount = a.MonthlyChatTokensCount,
                TotalChatTokensCount = a.TotalChatTokensCount,
                DailyTitleTokensCount = a.DailyTitleTokensCount,
                MonthlyTitleTokensCount = a.MonthlyTitleTokensCount,
                TotalTitleTokensCount = a.TotalTitleTokensCount,
                ModelRole = a.ModelRole
            })
            .ToListAsync();

        assignments = assignments.OrderBy(a => a.OrderIndex).ToList();

        return Ok(assignments);
    }

    [HttpPost("users/{userId}/systemconfigs")]
    public async Task<IActionResult> AssignSystemConfigToUser(string userId, [FromBody] AssignConfigToUserRequest request)
    {
        if (request.ModelRole < 1 || request.ModelRole > 7)
        {
            return BadRequest("ModelRole must be between 1 and 7.");
        }

        if (await _dbContext.UserSystemAiApiConfigurations.AnyAsync(a => a.AspNetUserId == userId && a.SystemAiApiConfigurationId == request.SystemAiApiConfigurationId))
        {
            return BadRequest("This configuration is already assigned to the user.");
        }

        var assignment = new UserSystemAiApiConfiguration
        {
            AspNetUserId = userId,
            SystemAiApiConfigurationId = request.SystemAiApiConfigurationId,
            IsEnabled = request.IsEnabled,
            MaxDailyChatRequests = request.MaxDailyChatRequests,
            MaxMonthlyChatRequests = request.MaxMonthlyChatRequests,
            MaxTotalChatRequests = request.MaxTotalChatRequests,
            MaxDailyTitleRequests = request.MaxDailyTitleRequests,
            MaxMonthlyTitleRequests = request.MaxMonthlyTitleRequests,
            MaxTotalTitleRequests = request.MaxTotalTitleRequests,
            MaxDailyChatTokens = request.MaxDailyChatTokens,
            MaxMonthlyChatTokens = request.MaxMonthlyChatTokens,
            MaxTotalChatTokens = request.MaxTotalChatTokens,
            MaxDailyTitleTokens = request.MaxDailyTitleTokens,
            MaxMonthlyTitleTokens = request.MaxMonthlyTitleTokens,
            MaxTotalTitleTokens = request.MaxTotalTitleTokens,
            ModelRole = request.ModelRole,
            OrderIndex = null
        };

        _dbContext.UserSystemAiApiConfigurations.Add(assignment);
        await _dbContext.SaveChangesAsync();

        return Ok(new UserSystemAiConfigDto
        {
            Id = assignment.Id,
            UserId = assignment.AspNetUserId,
            SystemAiApiConfigurationId = assignment.SystemAiApiConfigurationId,
            IsEnabled = assignment.IsEnabled,
            OrderIndex = assignment.OrderIndex,
            MaxDailyChatRequests = assignment.MaxDailyChatRequests,
            MaxMonthlyChatRequests = assignment.MaxMonthlyChatRequests,
            MaxTotalChatRequests = assignment.MaxTotalChatRequests,
            MaxDailyTitleRequests = assignment.MaxDailyTitleRequests,
            MaxMonthlyTitleRequests = assignment.MaxMonthlyTitleRequests,
            MaxTotalTitleRequests = assignment.MaxTotalTitleRequests,
            MaxDailyChatTokens = assignment.MaxDailyChatTokens,
            MaxMonthlyChatTokens = assignment.MaxMonthlyChatTokens,
            MaxTotalChatTokens = assignment.MaxTotalChatTokens,
            MaxDailyTitleTokens = assignment.MaxDailyTitleTokens,
            MaxMonthlyTitleTokens = assignment.MaxMonthlyTitleTokens,
            MaxTotalTitleTokens = assignment.MaxTotalTitleTokens,
            DailyChatRequestsCount = assignment.DailyChatRequestsCount,
            MonthlyChatRequestsCount = assignment.MonthlyChatRequestsCount,
            TotalChatRequestsCount = assignment.TotalChatRequestsCount,
            DailyTitleRequestsCount = assignment.DailyTitleRequestsCount,
            MonthlyTitleRequestsCount = assignment.MonthlyTitleRequestsCount,
            TotalTitleRequestsCount = assignment.TotalTitleRequestsCount,
            DailyChatTokensCount = assignment.DailyChatTokensCount,
            MonthlyChatTokensCount = assignment.MonthlyChatTokensCount,
            TotalChatTokensCount = assignment.TotalChatTokensCount,
            DailyTitleTokensCount = assignment.DailyTitleTokensCount,
            MonthlyTitleTokensCount = assignment.MonthlyTitleTokensCount,
            TotalTitleTokensCount = assignment.TotalTitleTokensCount,
            ModelRole = assignment.ModelRole
        });
    }
    
    [HttpDelete("user-systemconfigs/{id}")]
    public async Task<IActionResult> RemoveSystemConfigFromUser(long id)
    {
        var assignment = await _dbContext.UserSystemAiApiConfigurations.FindAsync(id);
        if (assignment != null)
        {
            _dbContext.UserSystemAiApiConfigurations.Remove(assignment);
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }
    
    [HttpPut("user-systemconfigs/{id}")]
    public async Task<IActionResult> UpdateUserSystemConfig(long id, [FromBody] UpdateUserSystemAiConfigRequest request)
    {
        if (request.ModelRole < 1 || request.ModelRole > 7)
        {
            return BadRequest("ModelRole must be between 1 and 7.");
        }

        var assignment = await _dbContext.UserSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        assignment.IsEnabled = request.IsEnabled;
        assignment.MaxDailyChatRequests = request.MaxDailyChatRequests;
        assignment.MaxMonthlyChatRequests = request.MaxMonthlyChatRequests;
        assignment.MaxTotalChatRequests = request.MaxTotalChatRequests;
        assignment.MaxDailyTitleRequests = request.MaxDailyTitleRequests;
        assignment.MaxMonthlyTitleRequests = request.MaxMonthlyTitleRequests;
        assignment.MaxTotalTitleRequests = request.MaxTotalTitleRequests;
        assignment.MaxDailyChatTokens = request.MaxDailyChatTokens;
        assignment.MaxMonthlyChatTokens = request.MaxMonthlyChatTokens;
        assignment.MaxTotalChatTokens = request.MaxTotalChatTokens;
        assignment.MaxDailyTitleTokens = request.MaxDailyTitleTokens;
        assignment.MaxMonthlyTitleTokens = request.MaxMonthlyTitleTokens;
        assignment.MaxTotalTitleTokens = request.MaxTotalTitleTokens;
        assignment.ModelRole = request.ModelRole;

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("user-systemconfigs/{id}/reset")]
    public async Task<IActionResult> ResetUserSystemConfigRateLimits(long id, [FromBody] ResetCounterRequest? request = null)
    {
        var assignment = await _dbContext.UserSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        if (request?.CounterName != null)
        {
            if (!AllowedCounters.Contains(request.CounterName))
            {
                return BadRequest("Invalid counter name.");
            }
            var prop = assignment.GetType().GetProperty(request.CounterName);
            if (prop != null && prop.CanWrite)
            {
                if (prop.PropertyType == typeof(int)) prop.SetValue(assignment, 0);
                else if (prop.PropertyType == typeof(long)) prop.SetValue(assignment, 0L);
            }
        }
        else
        {
            assignment.DailyChatRequestsCount = 0;
            assignment.MonthlyChatRequestsCount = 0;
            assignment.TotalChatRequestsCount = 0;
            assignment.DailyTitleRequestsCount = 0;
            assignment.MonthlyTitleRequestsCount = 0;
            assignment.TotalTitleRequestsCount = 0;
            assignment.DailyChatTokensCount = 0;
            assignment.MonthlyChatTokensCount = 0;
            assignment.TotalChatTokensCount = 0;
            assignment.DailyTitleTokensCount = 0;
            assignment.MonthlyTitleTokensCount = 0;
            assignment.TotalTitleTokensCount = 0;
        }
        
        await _dbContext.SaveChangesAsync();
        return Ok();
    }
    
    /// <summary>Orders the user's assignments (by assignment id) through the user's model order, the one source of order.</summary>
    [HttpPut("users/{userId}/systemconfigs/reorder")]
    public async Task<IActionResult> ReorderUserSystemConfigs(string userId, [FromBody] ReorderRequest request)
    {
        var configIdByAssignment = await _dbContext.UserSystemAiApiConfigurations
            .Where(a => a.AspNetUserId == userId)
            .ToDictionaryAsync(a => a.Id, a => a.SystemAiApiConfigurationId);

        var orderedConfigIds = request.OrderedIds
            .Where(configIdByAssignment.ContainsKey)
            .Select(id => configIdByAssignment[id])
            .ToArray();

        await SettingsService.UpsertSystemModelOrderAsync(_dbContext, userId, orderedConfigIds);
        return Ok();
    }

    // --- Group System Config Assignments ---

    [HttpGet("groups/{groupId}/systemconfigs")]
    public async Task<IActionResult> GetGroupSystemConfigs(long groupId)
    {
        var assignments = await _dbContext.GroupSystemAiApiConfigurations
            .Where(a => a.GroupId == groupId)
            .OrderBy(a => a.OrderIndex)
            .Select(a => new GroupSystemAiConfigDto
            {
                Id = a.Id,
                GroupId = a.GroupId,
                SystemAiApiConfigurationId = a.SystemAiApiConfigurationId,
                IsEnabled = a.IsEnabled,
                OrderIndex = a.OrderIndex,
                MaxDailyChatRequests = a.MaxDailyChatRequests,
                MaxMonthlyChatRequests = a.MaxMonthlyChatRequests,
                MaxTotalChatRequests = a.MaxTotalChatRequests,
                MaxDailyTitleRequests = a.MaxDailyTitleRequests,
                MaxMonthlyTitleRequests = a.MaxMonthlyTitleRequests,
                MaxTotalTitleRequests = a.MaxTotalTitleRequests,
                MaxDailyChatTokens = a.MaxDailyChatTokens,
                MaxMonthlyChatTokens = a.MaxMonthlyChatTokens,
                MaxTotalChatTokens = a.MaxTotalChatTokens,
                MaxDailyTitleTokens = a.MaxDailyTitleTokens,
                MaxMonthlyTitleTokens = a.MaxMonthlyTitleTokens,
                MaxTotalTitleTokens = a.MaxTotalTitleTokens,
                DailyChatRequestsCount = a.DailyChatRequestsCount,
                MonthlyChatRequestsCount = a.MonthlyChatRequestsCount,
                TotalChatRequestsCount = a.TotalChatRequestsCount,
                DailyTitleRequestsCount = a.DailyTitleRequestsCount,
                MonthlyTitleRequestsCount = a.MonthlyTitleRequestsCount,
                TotalTitleRequestsCount = a.TotalTitleRequestsCount,
                DailyChatTokensCount = a.DailyChatTokensCount,
                MonthlyChatTokensCount = a.MonthlyChatTokensCount,
                TotalChatTokensCount = a.TotalChatTokensCount,
                DailyTitleTokensCount = a.DailyTitleTokensCount,
                MonthlyTitleTokensCount = a.MonthlyTitleTokensCount,
                TotalTitleTokensCount = a.TotalTitleTokensCount,
                ModelRole = a.ModelRole
            })
            .ToListAsync();

        return Ok(assignments);
    }

    [HttpPost("groups/{groupId}/systemconfigs")]
    public async Task<IActionResult> AssignSystemConfigToGroup(long groupId, [FromBody] AssignConfigToGroupRequest request)
    {
        if (request.ModelRole < 1 || request.ModelRole > 7)
        {
            return BadRequest("ModelRole must be between 1 and 7.");
        }

        if (await _dbContext.GroupSystemAiApiConfigurations.AnyAsync(a => a.GroupId == groupId && a.SystemAiApiConfigurationId == request.SystemAiApiConfigurationId))
        {
            return BadRequest("This configuration is already assigned to the group.");
        }

        var orderIndex = await _dbContext.GroupSystemAiApiConfigurations.Where(a => a.GroupId == groupId).AnyAsync() 
            ? await _dbContext.GroupSystemAiApiConfigurations.Where(a => a.GroupId == groupId).MaxAsync(c => c.OrderIndex) + 1 
            : 0;

        var assignment = new GroupSystemAiApiConfiguration
        {
            GroupId = groupId,
            SystemAiApiConfigurationId = request.SystemAiApiConfigurationId,
            IsEnabled = request.IsEnabled,
            MaxDailyChatRequests = request.MaxDailyChatRequests,
            MaxMonthlyChatRequests = request.MaxMonthlyChatRequests,
            MaxTotalChatRequests = request.MaxTotalChatRequests,
            MaxDailyTitleRequests = request.MaxDailyTitleRequests,
            MaxMonthlyTitleRequests = request.MaxMonthlyTitleRequests,
            MaxTotalTitleRequests = request.MaxTotalTitleRequests,
            MaxDailyChatTokens = request.MaxDailyChatTokens,
            MaxMonthlyChatTokens = request.MaxMonthlyChatTokens,
            MaxTotalChatTokens = request.MaxTotalChatTokens,
            MaxDailyTitleTokens = request.MaxDailyTitleTokens,
            MaxMonthlyTitleTokens = request.MaxMonthlyTitleTokens,
            MaxTotalTitleTokens = request.MaxTotalTitleTokens,
            ModelRole = request.ModelRole,
            OrderIndex = orderIndex
        };

        _dbContext.GroupSystemAiApiConfigurations.Add(assignment);
        await _dbContext.SaveChangesAsync();

        return Ok(new GroupSystemAiConfigDto
        {
            Id = assignment.Id,
            GroupId = assignment.GroupId,
            SystemAiApiConfigurationId = assignment.SystemAiApiConfigurationId,
            IsEnabled = assignment.IsEnabled,
            OrderIndex = assignment.OrderIndex,
            MaxDailyChatRequests = assignment.MaxDailyChatRequests,
            MaxMonthlyChatRequests = assignment.MaxMonthlyChatRequests,
            MaxTotalChatRequests = assignment.MaxTotalChatRequests,
            MaxDailyTitleRequests = assignment.MaxDailyTitleRequests,
            MaxMonthlyTitleRequests = assignment.MaxMonthlyTitleRequests,
            MaxTotalTitleRequests = assignment.MaxTotalTitleRequests,
            MaxDailyChatTokens = assignment.MaxDailyChatTokens,
            MaxMonthlyChatTokens = assignment.MaxMonthlyChatTokens,
            MaxTotalChatTokens = assignment.MaxTotalChatTokens,
            MaxDailyTitleTokens = assignment.MaxDailyTitleTokens,
            MaxMonthlyTitleTokens = assignment.MaxMonthlyTitleTokens,
            MaxTotalTitleTokens = assignment.MaxTotalTitleTokens,
            DailyChatRequestsCount = assignment.DailyChatRequestsCount,
            MonthlyChatRequestsCount = assignment.MonthlyChatRequestsCount,
            TotalChatRequestsCount = assignment.TotalChatRequestsCount,
            DailyTitleRequestsCount = assignment.DailyTitleRequestsCount,
            MonthlyTitleRequestsCount = assignment.MonthlyTitleRequestsCount,
            TotalTitleRequestsCount = assignment.TotalTitleRequestsCount,
            DailyChatTokensCount = assignment.DailyChatTokensCount,
            MonthlyChatTokensCount = assignment.MonthlyChatTokensCount,
            TotalChatTokensCount = assignment.TotalChatTokensCount,
            DailyTitleTokensCount = assignment.DailyTitleTokensCount,
            MonthlyTitleTokensCount = assignment.MonthlyTitleTokensCount,
            TotalTitleTokensCount = assignment.TotalTitleTokensCount,
            ModelRole = assignment.ModelRole
        });
    }

    [HttpDelete("group-systemconfigs/{id}")]
    public async Task<IActionResult> RemoveSystemConfigFromGroup(long id)
    {
        var assignment = await _dbContext.GroupSystemAiApiConfigurations.FindAsync(id);
        if (assignment != null)
        {
            _dbContext.GroupSystemAiApiConfigurations.Remove(assignment);
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }
    
    [HttpPut("group-systemconfigs/{id}")]
    public async Task<IActionResult> UpdateGroupSystemConfig(long id, [FromBody] UpdateGroupSystemAiConfigRequest request)
    {
        if (request.ModelRole < 1 || request.ModelRole > 7)
        {
            return BadRequest("ModelRole must be between 1 and 7.");
        }

        var assignment = await _dbContext.GroupSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        assignment.IsEnabled = request.IsEnabled;
        assignment.MaxDailyChatRequests = request.MaxDailyChatRequests;
        assignment.MaxMonthlyChatRequests = request.MaxMonthlyChatRequests;
        assignment.MaxTotalChatRequests = request.MaxTotalChatRequests;
        assignment.MaxDailyTitleRequests = request.MaxDailyTitleRequests;
        assignment.MaxMonthlyTitleRequests = request.MaxMonthlyTitleRequests;
        assignment.MaxTotalTitleRequests = request.MaxTotalTitleRequests;
        assignment.MaxDailyChatTokens = request.MaxDailyChatTokens;
        assignment.MaxMonthlyChatTokens = request.MaxMonthlyChatTokens;
        assignment.MaxTotalChatTokens = request.MaxTotalChatTokens;
        assignment.MaxDailyTitleTokens = request.MaxDailyTitleTokens;
        assignment.MaxMonthlyTitleTokens = request.MaxMonthlyTitleTokens;
        assignment.MaxTotalTitleTokens = request.MaxTotalTitleTokens;
        assignment.ModelRole = request.ModelRole;

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("group-systemconfigs/{id}/reset")]
    public async Task<IActionResult> ResetGroupSystemConfigRateLimits(long id, [FromBody] ResetCounterRequest? request = null)
    {
        var assignment = await _dbContext.GroupSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        if (request?.CounterName != null)
        {
            if (!AllowedCounters.Contains(request.CounterName))
            {
                return BadRequest("Invalid counter name.");
            }
            var prop = assignment.GetType().GetProperty(request.CounterName);
            if (prop != null && prop.CanWrite)
            {
                if (prop.PropertyType == typeof(int)) prop.SetValue(assignment, 0);
                else if (prop.PropertyType == typeof(long)) prop.SetValue(assignment, 0L);
            }
        }
        else
        {
            assignment.DailyChatRequestsCount = 0;
            assignment.MonthlyChatRequestsCount = 0;
            assignment.TotalChatRequestsCount = 0;
            assignment.DailyTitleRequestsCount = 0;
            assignment.MonthlyTitleRequestsCount = 0;
            assignment.TotalTitleRequestsCount = 0;
            assignment.DailyChatTokensCount = 0;
            assignment.MonthlyChatTokensCount = 0;
            assignment.TotalChatTokensCount = 0;
            assignment.DailyTitleTokensCount = 0;
            assignment.MonthlyTitleTokensCount = 0;
            assignment.TotalTitleTokensCount = 0;
        }
        
        await _dbContext.SaveChangesAsync();
        return Ok();
    }
    
    [HttpPut("groups/{groupId}/systemconfigs/reorder")]
    public async Task<IActionResult> ReorderGroupSystemConfigs(long groupId, [FromBody] ReorderRequest request)
    {
        var configs = await _dbContext.GroupSystemAiApiConfigurations.Where(a => a.GroupId == groupId).ToListAsync();
        var idDict = configs.ToDictionary(c => c.Id);

        for (int i = 0; i < request.OrderedIds.Length; i++)
        {
            if (idDict.TryGetValue(request.OrderedIds[i], out var config))
            {
                config.OrderIndex = i;
            }
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    // --- Errors ---

    [HttpGet("errors")]
    public async Task<IActionResult> GetErrors()
    {
        // The row's own copy, never a join: an error outlives the configuration it was recorded against.
        var errors = await _dbContext.SystemAiErrorLogs
            .Where(e => !e.IsDismissed)
            .OrderByDescending(e => e.TimestampUtc)
            .Select(e => new SystemAiErrorLogDto
            {
                Id = e.Id,
                SystemAiApiConfigurationId = e.SystemAiApiConfigurationId,
                ConfigurationName = e.ModelDisplayName ?? e.ModelId ?? ("Deleted configuration #" + e.SystemAiApiConfigurationId),
                ErrorMessage = e.ErrorMessage,
                HttpStatusCode = e.HttpStatusCode,
                TimestampUtc = e.TimestampUtc
            })
            .ToListAsync();

        return Ok(errors);
    }

    [HttpPost("errors/{id}/dismiss")]
    public async Task<IActionResult> DismissError(long id)
    {
        var error = await _dbContext.SystemAiErrorLogs.FindAsync(id);
        if (error == null) return NotFound();

        var user = await _userManager.GetUserAsync(User);

        error.IsDismissed = true;
        error.DismissedByUserId = user?.Id;
        error.DismissedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    // --- Analytics ---

    [HttpGet("systemconfigs/{id}/analytics")]
    public async Task<IActionResult> GetConfigAnalytics(long id,
        [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] string? mode, [FromQuery] string? usernameFilter,
        [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var query = _dbContext.SystemAiUsageLogs
            .Where(l => l.SystemAiApiConfigurationId == id);

        if (startDate.HasValue) query = query.Where(l => l.TimestampUtc >= startDate.Value);
        if (endDate.HasValue) query = query.Where(l => l.TimestampUtc < endDate.Value.AddDays(1));

        if (mode == "individual")
        {
            // Group by user, with optional username filter
            var userQuery = query
                .Join(_dbContext.Users, l => l.AspNetUserId, u => u.Id,
                      (l, u) => new { Log = l, User = u });

            if (!string.IsNullOrWhiteSpace(usernameFilter))
                userQuery = userQuery.Where(x =>
                    x.User.UserName!.Contains(usernameFilter));

            int size = Math.Clamp(pageSize ?? 10, 1, 100);
            int pg = Math.Max(1, page ?? 1);

            var totalCount = await userQuery.Select(x => x.User.Id).Distinct().CountAsync();

            var rows = await userQuery
                .GroupBy(x => new { x.User.Id, x.User.UserName })
                .Select(g => new AnalyticsUserRow
                {
                    UserId = g.Key.Id,
                    UserName = g.Key.UserName ?? "",
                    ChatRequests = g.Count(x => x.Log.RoleContext == 1),
                    TitleRequests = g.Count(x => x.Log.RoleContext == 2),
                    BenchmarkRequests = g.Count(x => x.Log.RoleContext == 4),
                    InputTokens = g.Sum(x => (long)(x.Log.InputTokens ?? 0)),
                    OutputTokens = g.Sum(x => (long)(x.Log.OutputTokens ?? 0)),
                    CacheReadTokens = g.Sum(x => (long)(x.Log.CacheReadInputTokens ?? 0)),
                    CacheCreationTokens = g.Sum(x => (long)(x.Log.CacheCreationInputTokens ?? 0)),
                    AvgDurationMs = (int)(g.Average(x => x.Log.TotalDurationMs) ?? 0)
                })
                .OrderByDescending(r => r.ChatRequests + r.TitleRequests + r.BenchmarkRequests)
                .Skip((pg - 1) * size)
                .Take(size)
                .ToListAsync();

            return Ok(new AnalyticsResponse { Rows = rows, TotalCount = totalCount });
        }
        else
        {
            // Aggregate all users into a single row
            var row = await query
                .GroupBy(l => 1)
                .Select(g => new AnalyticsUserRow
                {
                    UserName = "All Users",
                    ChatRequests = g.Count(l => l.RoleContext == 1),
                    TitleRequests = g.Count(l => l.RoleContext == 2),
                    BenchmarkRequests = g.Count(l => l.RoleContext == 4),
                    InputTokens = g.Sum(l => (long)(l.InputTokens ?? 0)),
                    OutputTokens = g.Sum(l => (long)(l.OutputTokens ?? 0)),
                    CacheReadTokens = g.Sum(l => (long)(l.CacheReadInputTokens ?? 0)),
                    CacheCreationTokens = g.Sum(l => (long)(l.CacheCreationInputTokens ?? 0)),
                    AvgDurationMs = (int)(g.Average(l => l.TotalDurationMs) ?? 0)
                })
                .FirstOrDefaultAsync();

            return Ok(new AnalyticsResponse
            {
                Rows = row != null ? new List<AnalyticsUserRow> { row } : new(),
                TotalCount = row != null ? 1 : 0
            });
        }
    }

    // --- Governor & Telemetry ---

    [HttpGet("governor/status")]
    public IActionResult GetGovernorStatus()
    {
        var statusList = _governor.GetStatus();
        var dto = new AiGovernorStatusDto
        {
            MaxConcurrentCalls = _governor.MaxConcurrentCalls,
            MaxRetryAfterSeconds = _governor.MaxRetryAfterSeconds,
            ActiveKeys = statusList.Select(s => new AiGovernorKeyStatusDto
            {
                CredentialKey = s.CredentialKey,
                IsRateLimited = s.IsRateLimited,
                RemainingCooldownSeconds = Math.Round(s.RemainingCooldownSeconds, 1),
                InFlightCalls = s.InFlightCalls
            }).ToList()
        };
        return Ok(dto);
    }

    [HttpPost("governor/reset-cooldown")]
    public IActionResult ResetGovernorCooldown([FromBody] ResetCooldownRequest? request)
    {
        _governor.ClearCooldown(request?.CredentialKey);
        return Ok();
    }

    /// <param name="keys">system (default) | user | both.</param>
    [HttpGet("ai-telemetry/summary")]
    public async Task<IActionResult> GetAiTelemetrySummary([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] string? keys, [FromServices] Overseer.Services.Telemetry.AiTelemetrySummaryService summaryService)
        => Ok(await summaryService.GetSummaryAsync(startDate, endDate, keys));

    /// <summary>
    /// Telemetry over time; refused with 400 when the range has more than 1,100 buckets, a filter has more than 50
    /// choices, or a model or group is malformed. Each filter is a repeated query key; an empty one filters nothing.
    /// </summary>
    /// <param name="roles">Comma-separated role groups: chat, title, benchmark, other.</param>
    /// <param name="bucket">auto | day | week | month.</param>
    /// <param name="splitBy">none | role | provider | model | keySource | user.</param>
    /// <param name="userId">One user, merged into <paramref name="userIds"/>.</param>
    /// <param name="userIds">Users, <c>userIds=a&amp;userIds=b</c>.</param>
    /// <param name="models">Models as <c>provider|modelId</c>.</param>
    /// <param name="groupIds">Group ids, or <c>none</c> for the users in no group.</param>
    [HttpGet("ai-telemetry/series")]
    public async Task<IActionResult> GetAiTelemetrySeries([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] string? keys, [FromQuery] string? roles, [FromQuery] string? bucket, [FromQuery] string? splitBy,
        [FromQuery] string? userId, [FromQuery] string[]? userIds, [FromQuery] string[]? models, [FromQuery] string[]? groupIds,
        [FromServices] Overseer.Services.Telemetry.AiTelemetrySummaryService summaryService)
    {
        var (rangeStart, rangeEnd) = Overseer.Services.Telemetry.AiTelemetrySummaryService.SeriesRange(startDate, endDate);
        if (Overseer.Services.Telemetry.AiTelemetrySummaryService.BucketCount(rangeStart, rangeEnd, bucket)
            > Overseer.Services.Telemetry.AiTelemetrySummaryService.MaxSeriesBuckets)
        {
            return BadRequest(new { message = "The range has too many buckets; choose a coarser bucket." });
        }

        var userList = new[] { userId }.Concat(userIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (userList.Count > Overseer.Services.Telemetry.AiTelemetrySummaryService.MaxSeriesFilterUsers)
        {
            return BadRequest(new { message = $"At most {Overseer.Services.Telemetry.AiTelemetrySummaryService.MaxSeriesFilterUsers} users can be charted at once." });
        }

        var modelList = new List<Overseer.Services.Telemetry.AiTelemetryModelRef>();
        foreach (var value in models ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!Overseer.Services.Telemetry.AiTelemetryModelRef.TryParse(value, out var model))
            {
                return BadRequest(new { message = "Each model is provider|modelId." });
            }
            if (!modelList.Contains(model)) modelList.Add(model);
        }
        if (modelList.Count > Overseer.Services.Telemetry.AiTelemetrySummaryService.MaxSeriesFilterModels)
        {
            return BadRequest(new { message = $"At most {Overseer.Services.Telemetry.AiTelemetrySummaryService.MaxSeriesFilterModels} models can be charted at once." });
        }

        var groupList = new List<long?>();
        foreach (var value in groupIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!TryParseTelemetryGroup(value, out long? group))
            {
                return BadRequest(new { message = "Each group is a group id or none." });
            }
            if (!groupList.Contains(group)) groupList.Add(group);
        }
        if (groupList.Count > Overseer.Services.Telemetry.AiTelemetrySummaryService.MaxSeriesFilterGroups)
        {
            return BadRequest(new { message = $"At most {Overseer.Services.Telemetry.AiTelemetrySummaryService.MaxSeriesFilterGroups} groups can be charted at once." });
        }

        var roleList = (roles ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return Ok(await summaryService.GetSeriesAsync(startDate, endDate, keys, roleList, bucket, splitBy, userList, modelList, groupList));
    }

    /// <summary>The models, users and groups the charts offer for the series' range, key sources and roles.</summary>
    /// <param name="keys">system (default) | user | both.</param>
    /// <param name="roles">Comma-separated role groups: chat, title, benchmark, other.</param>
    [HttpGet("ai-telemetry/series-options")]
    public async Task<IActionResult> GetAiTelemetrySeriesOptions([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] string? keys, [FromQuery] string? roles,
        [FromServices] Overseer.Services.Telemetry.AiTelemetrySummaryService summaryService)
    {
        var roleList = (roles ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return Ok(await summaryService.GetSeriesOptionsAsync(startDate, endDate, keys, roleList));
    }

    /// <summary>Per-group usage of the selected key sources: every group's current members, then the users in no group.</summary>
    /// <param name="keys">system (default) | user | both.</param>
    [HttpGet("ai-telemetry/groups")]
    public async Task<IActionResult> GetAiTelemetryGroups([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] string? keys, [FromServices] Overseer.Services.Telemetry.AiTelemetrySummaryService summaryService)
        => Ok(await summaryService.GetGroupsAsync(startDate, endDate, keys));

    /// <summary>Usage of one group's current members by model, role context and thinking level, in the selected key sources.</summary>
    /// <param name="keys">system (default) | user | both.</param>
    /// <param name="groupId">A group id, or <c>none</c> for the users in no group.</param>
    [HttpGet("ai-telemetry/group-models")]
    public async Task<IActionResult> GetAiTelemetryGroupModels([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] string? keys, [FromQuery] string? groupId,
        [FromServices] Overseer.Services.Telemetry.AiTelemetrySummaryService summaryService)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return BadRequest(new { message = "groupId is required." });
        }
        if (!TryParseTelemetryGroup(groupId, out long? group))
        {
            return BadRequest(new { message = "groupId is a group id or none." });
        }

        var dto = await summaryService.GetGroupModelsAsync(startDate, endDate, keys, group);
        return dto == null ? NotFound(new { message = "No group has this identifier." }) : Ok(dto);
    }

    /// <summary>A telemetry group filter value: a group id, or <c>none</c> (null) for the users in no group.</summary>
    private static bool TryParseTelemetryGroup(string value, out long? groupId)
    {
        groupId = null;
        string trimmed = value.Trim();
        if (string.Equals(trimmed, Overseer.Services.Telemetry.AiTelemetrySummaryService.NoGroupKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (long.TryParse(trimmed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long id))
        {
            groupId = id;
            return true;
        }
        return false;
    }

    /// <summary>Per-user usage of the selected key sources, sorted by aggregate or name and paged on the server.</summary>
    /// <param name="sort">cost (default) | requests | tokens | peakHour | refusals | lastActive | name.</param>
    /// <param name="keys">system (default) | user | both.</param>
    /// <param name="direction">asc | desc (default).</param>
    [HttpGet("ai-telemetry/users")]
    public async Task<IActionResult> GetAiTelemetryUsers([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] string? search, [FromQuery] string? userId, [FromQuery] string? sort, [FromQuery] int skip, [FromQuery] int take,
        [FromQuery] string? keys, [FromQuery] string? direction,
        [FromServices] Overseer.Services.Telemetry.AiTelemetrySummaryService summaryService)
        => Ok(await summaryService.GetUsersAsync(startDate, endDate, search,
            string.IsNullOrWhiteSpace(userId) ? null : userId, sort, skip, take, keys, direction));

    /// <summary>Usage of one model by user, in one key source and the given role groups.</summary>
    /// <param name="keys">system (default) | user.</param>
    /// <param name="roles">Comma-separated role groups: chat, title, benchmark, other.</param>
    [HttpGet("ai-telemetry/model-users")]
    public async Task<IActionResult> GetAiTelemetryModelUsers([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] string? keys, [FromQuery] string? provider, [FromQuery] string? modelId, [FromQuery] string? roles,
        [FromServices] Overseer.Services.Telemetry.AiTelemetrySummaryService summaryService)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(modelId))
        {
            return BadRequest(new { message = "provider and modelId are required." });
        }

        var roleList = (roles ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return Ok(await summaryService.GetModelUsersAsync(startDate, endDate, keys, provider, modelId, roleList));
    }

    /// <summary>Usage of one user by model, role context and thinking level, in the selected key sources.</summary>
    /// <param name="keys">system (default) | user | both.</param>
    [HttpGet("ai-telemetry/user-models")]
    public async Task<IActionResult> GetAiTelemetryUserModels([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] string? keys, [FromQuery] string? userId,
        [FromServices] Overseer.Services.Telemetry.AiTelemetrySummaryService summaryService)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { message = "userId is required." });
        }

        return Ok(await summaryService.GetUserModelsAsync(startDate, endDate, keys, userId));
    }

    // --- Helper Methods ---

    private void EncryptApiKey(SystemAiApiConfiguration config, string plainText)
    {
        var (ciphertext, nonce, tag) = _cryptoService.Encrypt(plainText, "SYSTEM_API_KEY");
        config.EncryptedApiKey = ciphertext;
        config.ApiKeyNonce = nonce;
        config.ApiKeyTag = tag;
    }

    private static void ClearApiKey(SystemAiApiConfiguration config)
    {
        config.EncryptedApiKey = null;
        config.ApiKeyNonce = null;
        config.ApiKeyTag = null;
    }

    private const string DefaultKeyNeedsOfficialEndpoint = "A default key works only with the provider's own endpoint.";

    private static string NoDefaultKeyMessage(string provider)
        => $"There is no default {provider} key. Add one with API Keys in System Configs, or choose Custom.";

    [HttpGet("system-alerts")]
    public IActionResult GetSystemAlerts([FromServices] ConfigHealthService configHealthService)
    {
        return Ok(configHealthService.GetSystemAlerts());
    }

    // --- Database Storage & Maintenance ---

    [HttpGet("storage-metrics")]
    public async Task<IActionResult> GetStorageMetrics([FromServices] DatabaseStorageMetricsService metricsService)
    {
        var metrics = await metricsService.GetStorageMetricsAsync();
        return Ok(metrics);
    }

    [HttpPost("maintenance/run-now")]
    public async Task<IActionResult> RunMaintenanceNow([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var result = await retentionService.RunFullMaintenanceAsync(request, MaintenanceTriggers.Manual);
        return Ok(result);
    }

    /// <summary>
    /// Runs one granular maintenance action and returns the same result shape as the full pass.
    /// The run is recorded in the maintenance history whether it succeeds or not; a failure is
    /// rethrown.
    /// </summary>
    private static async Task<IActionResult> RunGranularMaintenanceAsync(
        ChatRetentionService retentionService,
        string trigger,
        bool isDryRun,
        Func<MaintenanceResultDto, Task> action)
    {
        var startedUtc = DateTime.UtcNow;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new MaintenanceResultDto { IsDryRun = isDryRun, Trigger = trigger };

        try
        {
            await action(result);
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            sw.Stop();
            result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            await retentionService.RecordRunAsync(result, startedUtc);
        }

        return new OkObjectResult(result);
    }

    [HttpGet("maintenance/history")]
    public async Task<IActionResult> GetMaintenanceHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 1000);

        var query = _dbContext.MaintenanceRunLogs.AsNoTracking();
        var totalCount = await query.CountAsync();

        var rows = await query
            .OrderByDescending(r => r.StartedUtc)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new MaintenanceRunLogDto
            {
                Id = r.Id,
                StartedUtc = r.StartedUtc,
                CompletedUtc = r.CompletedUtc,
                Trigger = r.Trigger,
                IsDryRun = r.IsDryRun,
                Success = r.Success,
                ElapsedMilliseconds = r.ElapsedMilliseconds,
                SoftDeletedCount = r.SoftDeletedCount,
                PurgedSessionCount = r.PurgedSessionCount,
                PurgedMessageCount = r.PurgedMessageCount,
                PurgedToolCallCount = r.PurgedToolCallCount,
                PrunedToolResultCount = r.PrunedToolResultCount,
                PrunedBenchmarkToolResultCount = r.PrunedBenchmarkToolResultCount,
                PrunedAuditLogCount = r.PrunedAuditLogCount,
                PrunedAiErrorLogCount = r.PrunedAiErrorLogCount,
                PrunedSecurityEventCount = r.PrunedSecurityEventCount,
                PrunedChatProviderAttemptCount = r.PrunedChatProviderAttemptCount,
                DeletedDiskFolderCount = r.DeletedDiskFolderCount,
                DeletedDiskFileCount = r.DeletedDiskFileCount,
                SweptOrphanFolderCount = r.SweptOrphanFolderCount,
                ReclaimedDiskBytes = r.ReclaimedDiskBytes,
                ErrorMessage = r.ErrorMessage,
                HasLog = r.LogText != null || r.ErrorMessage != null
            })
            .ToListAsync();

        // SpecifyKind does not translate to SQL, so the kinds are fixed after materialisation.
        foreach (var row in rows)
        {
            row.StartedUtc = DateTime.SpecifyKind(row.StartedUtc, DateTimeKind.Utc);
            if (row.CompletedUtc.HasValue)
            {
                row.CompletedUtc = DateTime.SpecifyKind(row.CompletedUtc.Value, DateTimeKind.Utc);
            }
        }

        return Ok(new MaintenanceHistoryPageDto { TotalCount = totalCount, Rows = rows });
    }

    [HttpGet("maintenance/history/{id:long}/log")]
    public async Task<IActionResult> GetMaintenanceRunLog(long id)
    {
        var row = await _dbContext.MaintenanceRunLogs
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new MaintenanceRunLogTextDto { ErrorMessage = r.ErrorMessage, LogText = r.LogText })
            .FirstOrDefaultAsync();

        return row == null ? NotFound() : Ok(row);
    }

    private static string DryRunPrefix(bool isDryRun) => isDryRun ? "[DRY RUN] " : "";

    [HttpPost("maintenance/purge-trash-now")]
    public async Task<IActionResult> PurgeTrashNow([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        return await RunGranularMaintenanceAsync(retentionService, MaintenanceTriggers.PurgeTrash, isDryRun, async result =>
        {
            var trashIds = await _dbContext.ChatSession
                .Where(s => s.IsDeleted)
                .Select(s => s.Id)
                .ToListAsync();

            var purgeResult = await retentionService.PermanentlyPurgeSessionsAsync(trashIds, isDryRun);
            ChatRetentionService.ApplyPurgeCounts(result, purgeResult);
        });
    }

    [HttpPost("maintenance/purge-inactive")]
    public async Task<IActionResult> PurgeInactive([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        var days = request?.InactivityDays ?? retentionService.Settings.InactivityTtlDays;
        return await RunGranularMaintenanceAsync(retentionService, MaintenanceTriggers.PurgeInactive, isDryRun, async result =>
        {
            result.SoftDeletedCount = await retentionService.SoftDeleteInactiveSessionsAsync(days, isDryRun);
            result.Logs.Add($"{DryRunPrefix(isDryRun)}Expired {result.SoftDeletedCount} inactive sessions: older than {days} days, plus sessions past their own retention TTL (confidential sessions set to purge are removed outright).");
        });
    }

    [HttpPost("maintenance/prune-tool-results")]
    public async Task<IActionResult> PruneToolResults([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        var days = request?.ToolCallPruneDays ?? retentionService.Settings.PruneToolCallResultsDays;
        return await RunGranularMaintenanceAsync(retentionService, MaintenanceTriggers.PruneToolResults, isDryRun, async result =>
        {
            result.PrunedToolResultCount = await retentionService.PruneAgedToolCallResultsAsync(days, isDryRun);
            result.Logs.Add($"{DryRunPrefix(isDryRun)}Pruned {result.PrunedToolResultCount} tool call results older than {days} days.");
        });
    }

    [HttpPost("maintenance/prune-benchmark-tool-results")]
    public async Task<IActionResult> PruneBenchmarkToolResults([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        var days = request?.BenchmarkToolCallPruneDays ?? retentionService.Settings.PruneBenchmarkToolCallResultsDays;
        return await RunGranularMaintenanceAsync(retentionService, MaintenanceTriggers.PruneBenchmarkToolResults, isDryRun, async result =>
        {
            result.PrunedBenchmarkToolResultCount = await retentionService.PruneAgedBenchmarkToolCallPayloadsAsync(days, isDryRun);
            result.Logs.Add($"{DryRunPrefix(isDryRun)}Pruned {result.PrunedBenchmarkToolResultCount} benchmark tool call payloads for runs older than {days} days.");
        });
    }

    [HttpPost("maintenance/prune-audit-log")]
    public async Task<IActionResult> PruneAuditLog([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        var days = request?.AuditLogRetentionDays ?? retentionService.Settings.AuditLogRetentionDays;

        // An explicit request to prune with no window is a mistake, not a request to do nothing.
        if (days <= 0)
            return BadRequest(new { message = "Audit log retention must be a positive number of days." });

        return await RunGranularMaintenanceAsync(retentionService, MaintenanceTriggers.PruneAuditLog, isDryRun, async result =>
        {
            result.PrunedAuditLogCount = await retentionService.PruneAccessJournalAsync(days, isDryRun);
            result.Logs.Add($"{DryRunPrefix(isDryRun)}Pruned {result.PrunedAuditLogCount} access journal rows older than {days} days.");
        });
    }

    [HttpPost("maintenance/prune-ai-error-log")]
    public async Task<IActionResult> PruneAiErrorLog([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        var days = request?.AiErrorLogPruneDays ?? retentionService.Settings.PruneDismissedAiErrorLogDays;

        if (days <= 0)
            return BadRequest(new { message = "The dismissed AI error window must be a positive number of days." });

        return await RunGranularMaintenanceAsync(retentionService, MaintenanceTriggers.PruneAiErrorLog, isDryRun, async result =>
        {
            result.PrunedAiErrorLogCount = await retentionService.PruneDismissedAiErrorLogsAsync(days, isDryRun);
            result.Logs.Add($"{DryRunPrefix(isDryRun)}Pruned {result.PrunedAiErrorLogCount} dismissed AI error rows dismissed over {days} days ago. Undismissed rows are never pruned.");
        });
    }

    [HttpPost("maintenance/prune-security-events")]
    public async Task<IActionResult> PruneSecurityEvents([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        var days = request?.SecurityEventPruneDays ?? retentionService.Settings.PruneSecurityEventDays;

        if (days <= 0)
            return BadRequest(new { message = "The security event window must be a positive number of days." });

        return await RunGranularMaintenanceAsync(retentionService, MaintenanceTriggers.PruneSecurityEvents, isDryRun, async result =>
        {
            result.PrunedSecurityEventCount = await retentionService.PruneSecurityEventsAsync(days, isDryRun);
            result.Logs.Add($"{DryRunPrefix(isDryRun)}Pruned {result.PrunedSecurityEventCount} security events last seen over {days} days ago. Events of users with an active account hold or a pending deletion request are kept.");
        });
    }

    [HttpPost("maintenance/prune-chat-provider-attempts")]
    public async Task<IActionResult> PruneChatProviderAttempts([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        var days = request?.ChatProviderAttemptPruneDays ?? retentionService.Settings.PruneChatProviderAttemptDays;

        if (days <= 0)
            return BadRequest(new { message = "The chat provider attempt window must be a positive number of days." });

        return await RunGranularMaintenanceAsync(retentionService, MaintenanceTriggers.PruneChatProviderAttempts, isDryRun, async result =>
        {
            result.PrunedChatProviderAttemptCount = await retentionService.PruneChatProviderAttemptsAsync(days, isDryRun);
            result.Logs.Add($"{DryRunPrefix(isDryRun)}Pruned {result.PrunedChatProviderAttemptCount} chat provider attempts older than {days} days.");
        });
    }

    [HttpPost("maintenance/sweep-orphans")]
    public async Task<IActionResult> SweepOrphanedFolders([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        return await RunGranularMaintenanceAsync(retentionService, MaintenanceTriggers.SweepOrphans, isDryRun, async result =>
        {
            result.SweptOrphanFolderCount = await retentionService.SweepOrphanedDiskDirectoriesAsync(isDryRun);
            result.Logs.Add($"{DryRunPrefix(isDryRun)}Swept {result.SweptOrphanFolderCount} orphaned disk folders.");
        });
    }

    [HttpPost("maintenance/clear-report-charts")]
    public async Task<IActionResult> ClearReportCharts(
        [FromBody] MaintenanceRequestDto? request,
        [FromServices] ChatRetentionService retentionService,
        [FromServices] Overseer.Services.Benchmarking.BenchmarkReportChartStore chartStore)
    {
        var isDryRun = request?.DryRun ?? false;

        if (!chartStore.IsConfigured)
            return BadRequest(new { message = Overseer.Services.Benchmarking.BenchmarkReportChartStore.NotConfiguredMessage });

        return await RunGranularMaintenanceAsync(retentionService, MaintenanceTriggers.ClearReportCharts, isDryRun, async result =>
        {
            var documentIds = await _dbContext.BenchmarkReportDocuments
                .Select(d => d.Id)
                .ToListAsync();

            var cleared = await chartStore.ClearAllAsync(isDryRun, documentIds);
            result.DeletedDiskFolderCount = cleared.FolderCount;
            result.DeletedDiskFileCount = cleared.FileCount;
            result.ReclaimedDiskBytes = cleared.Bytes;

            var megabytes = cleared.Bytes / (1024.0 * 1024.0);
            result.Logs.Add($"{DryRunPrefix(isDryRun)}{(isDryRun ? "Would delete" : "Deleted")} {cleared.FolderCount} chart folders ({cleared.FileCount} files, {megabytes:0.##} MB), {cleared.OrphanFolderCount} of them for documents that no longer exist.");
            if (cleared.LeftAlone.Count > 0)
                result.Logs.Add($"Left alone: {string.Join(", ", cleared.LeftAlone)}");

            if (!isDryRun)
                DatabaseStorageMetricsService.InvalidateDiskMetricsCache();
        });
    }

    [HttpPost("maintenance/send-report-email")]
    public async Task<IActionResult> SendReportEmail([FromServices] DatabaseStorageMetricsService metricsService)
    {
        var metrics = await metricsService.GetStorageMetricsAsync();
        var sent = await metricsService.SendStorageWarningReportEmailAsync(metrics, metrics.StatusLevel == "Normal" ? "Informational" : metrics.StatusLevel);
        return Ok(new { success = sent, message = sent ? "Storage report email sent successfully." : "Failed to send email. Check EmailSender configuration." });
    }

    [HttpPost("test-sentry")]
    public IActionResult TestSentryError()
    {
        throw new Exception("Sentry Backend Crash Test triggered by Admin");
    }

    /// <summary>
    /// Normalises a posture name from a request to its canonical casing, or refuses it.
    /// </summary>
    /// <remarks>
    /// An unrecognised name is a 400 rather than a silent fall back to "Unknown". The parser
    /// used when *reading* degrades unrecognised values downward, which is the safe direction
    /// for a value already in the database; on the way in, storing something other than what
    /// the operator asked for is how a posture stops meaning anything.
    /// </remarks>
    private static bool TryNormalizePosture(string? requested, out string? normalized, out string error)
    {
        normalized = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(requested))
            return true;

        if (!Enum.TryParse<Overseer.Services.Privacy.ProviderConfidentialityPosture>(
                requested.Trim(), ignoreCase: true, out var parsed))
        {
            error = $"Unrecognised confidentiality posture '{requested}'. Valid values are: "
                + string.Join(", ", Enum.GetNames<Overseer.Services.Privacy.ProviderConfidentialityPosture>()) + ".";
            return false;
        }

        normalized = parsed.ToStoredValue();
        return true;
    }
}

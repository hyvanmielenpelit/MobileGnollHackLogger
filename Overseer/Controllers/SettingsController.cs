using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Overseer.Services;
using Overseer.Extensions;
using System.Security.Claims;
using System.Text.Json;
using System.Net.Http.Headers;

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

    public SettingsController(SettingsService settingsService, IHttpClientFactory httpClientFactory, IConfiguration configuration, ModelMetadataService modelMetadataService, RecommendedModelService recommendedModelService)
    {
        _settingsService = settingsService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _modelMetadataService = modelMetadataService;
        _recommendedModelService = recommendedModelService;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetSettings()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var settings = await _settingsService.GetSettingsAsync(userId);
        
        var apiKeysStatus = await _settingsService.GetApiKeysStatusAsync(userId);
        var userModels = await _settingsService.GetUserModelsAsync(userId);
        var systemConfigs = await _settingsService.GetResolvedSystemModelsAsync(userId);
        bool hasSystemModel = systemConfigs.Any();
        
        bool hasApiKey = apiKeysStatus.Any(s => (bool)((dynamic)s).HasKey) || hasSystemModel;
        bool hasModel = userModels.Any() || hasSystemModel;
        
        bool showDebugLog = _configuration.ShouldShowDebugLog(User.Identity?.Name);
        
        return Ok(new
        {
            hasApiKey = hasApiKey,
            hasModel = hasModel,
            configuredProviders = apiKeysStatus.Where(s => (bool)((dynamic)s).HasKey).Select(s => (string)((dynamic)s).Provider).ToList(),
            maxAttachmentSize = _configuration.GetValue<long>("MaxAttachmentSize", 15728640),
            spoilerFreeMode = settings?.SpoilerFreeMode ?? true,
            showSourceCodeReferences = settings?.ShowSourceCodeReferences ?? false,
            maxResultLength = settings?.MaxResultLength,
            maxCallsPerSession = settings?.MaxCallsPerSession,
            maxToolIterations = settings?.MaxToolIterations,
            enableWebSearch = settings?.EnableWebSearch ?? true,
            enableToolUse = settings?.EnableToolUse ?? true,
            enableClientTools = settings?.EnableClientTools ?? true,
            enableGameActions = settings?.EnableGameActions ?? false,
            showThoughtsAndTools = settings?.ShowThoughtsAndTools ?? 0,
            requestTimeout = settings?.RequestTimeout,
            showDebugLog = showDebugLog,
            performanceLimits = new {
                maxResultLength = new {
                    min = _configuration.GetValue<int>("AiPerformanceSettings:MaxResultLength:Min", 500),
                    max = _configuration.GetValue<int>("AiPerformanceSettings:MaxResultLength:Max", 100000),
                    defaultValue = _configuration.GetValue<int>("AiPerformanceSettings:MaxResultLength:Default", 3000)
                },
                maxCallsPerSession = new {
                    min = _configuration.GetValue<int>("AiPerformanceSettings:MaxCallsPerSession:Min", 1),
                    max = _configuration.GetValue<int>("AiPerformanceSettings:MaxCallsPerSession:Max", 500),
                    defaultValue = _configuration.GetValue<int>("AiPerformanceSettings:MaxCallsPerSession:Default", 30)
                },
                maxToolIterations = new {
                    min = _configuration.GetValue<int>("AiPerformanceSettings:MaxToolIterations:Min", 1),
                    max = _configuration.GetValue<int>("AiPerformanceSettings:MaxToolIterations:Max", 100),
                    defaultValue = _configuration.GetValue<int>("AiPerformanceSettings:MaxToolIterations:Default", 32)
                },
                requestTimeout = new {
                    min = _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Min", 5),
                    max = _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Max", 3600),
                    defaultValue = _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Default", 300)
                }
            },
            titleGenerationModelId = settings?.TitleGenerationModelId,
            titleGenerationSystemModelId = settings?.TitleGenerationSystemModelId
        });
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

        if (request.RequestTimeout.HasValue)
        {
            int min = _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Min", 5);
            int max = _configuration.GetValue<int>("AiPerformanceSettings:ChatRequestTimeout:Max", 3600);
            if (request.RequestTimeout.Value < min || request.RequestTimeout.Value > max)
                return BadRequest($"RequestTimeout must be between {min} and {max}");
        }

        await _settingsService.SaveSettingsAsync(userId, request.SpoilerFreeMode, request.EnableWebSearch, request.EnableToolUse, request.EnableClientTools, request.EnableGameActions, request.ShowSourceCodeReferences, request.MaxResultLength, request.MaxCallsPerSession, request.MaxToolIterations, request.ShowThoughtsAndTools, request.RequestTimeout);
        
        return Ok();
    }

    [HttpPut("titlemodel")]
    public async Task<IActionResult> UpdateTitleModel([FromBody] UpdateTitleModelRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        try
        {
            await _settingsService.SaveTitleGenerationModelAsync(userId, request.ModelId, request.IsSystem);
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

    [HttpGet("usermodels")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetUserModels()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var models = await _settingsService.GetUserModelsAsync(userId);
        var dtos = models.Select(m => new {
            Id = m.Id,
            Provider = m.Provider,
            ModelId = m.ModelId,
            DisplayName = (string?)m.DisplayName,
            ThinkingLevel = (string?)m.ThinkingLevel,
            OrderIndex = m.OrderIndex,
            MaxInputTokens = (int?)m.MaxInputTokens,
            MaxOutputTokens = (int?)m.MaxOutputTokens,
            IsSystem = false,
            ModelRole = 3
        }).ToList();

        var systemConfigs = await _settingsService.GetResolvedSystemModelsAsync(userId);
        var sysDtos = systemConfigs.Select(x => new {
            Id = x.Config.Id,
            Provider = x.Config.Provider,
            ModelId = x.Config.ModelId,
            DisplayName = (string?)x.Config.DisplayName,
            ThinkingLevel = (string?)x.Config.ThinkingLevel,
            OrderIndex = x.Config.OrderIndex,
            MaxInputTokens = (int?)x.Config.MaxInputTokens,
            MaxOutputTokens = (int?)x.Config.MaxOutputTokens,
            IsSystem = true,
            ModelRole = x.ResolvedRole
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

        var model = new MobileGnollHackLogger.Data.UserAiModel
        {
            Provider = request.Provider,
            ModelId = request.ModelId,
            DisplayName = request.DisplayName,
            ThinkingLevel = request.ThinkingLevel,
            MaxInputTokens = request.MaxInputTokens,
            MaxOutputTokens = request.MaxOutputTokens
        };
        await _settingsService.AddUserModelAsync(userId, model);
        return Ok(new { model.Id });
    }

    [HttpPut("usermodels/{id}")]
    public async Task<IActionResult> UpdateUserModel(long id, [FromBody] UpdateUserModelRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _settingsService.UpdateUserModelAsync(userId, id, request.DisplayName, request.ThinkingLevel, request.MaxInputTokens, request.MaxOutputTokens);
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

    [HttpPost("models")]
    public async Task<IActionResult> GetModels([FromBody] GetModelsRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var provider = request.Provider ?? "OpenAI";
        var apiKey = request.ApiKey;

        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = await _settingsService.GetDecryptedApiKeyForProviderAsync(userId, provider);
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            return BadRequest(new { message = "API Key is required to fetch models." });
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var models = new List<ApiModelDto>();

            var cutoffStr = _configuration["ModelCutoffDate"] ?? "2026-01-01";
            long cutoffTimestamp = 0;
            if (DateTimeOffset.TryParse(cutoffStr, out var cutoffDto))
            {
                cutoffTimestamp = cutoffDto.ToUnixTimeSeconds();
            }

            if (provider == "OpenAI")
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                var response = await client.GetAsync("https://api.openai.com/v1/models");
                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest(new { message = $"OpenAI API returned {response.StatusCode}: {await response.Content.ReadAsStringAsync()}" });
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
                            if (name.StartsWith("gpt-") || name.StartsWith("chatgpt-") || name.StartsWith("o1-") || name.StartsWith("o3-"))
                            {
                                // Specifically exclude audio/tts/dall-e if they ever accidentally get prefixed
                                if (!name.Contains("audio") && !name.Contains("realtime"))
                                {
                                    long created = 0;
                                    if (modelElement.TryGetProperty("created", out var createdElement) && createdElement.ValueKind == JsonValueKind.Number)
                                    {
                                        created = createdElement.GetInt64();
                                    }
                                    if (created >= cutoffTimestamp)
                                    {
                                        var meta = _modelMetadataService.GetMetadata(name);
                                        models.Add(new ApiModelDto { Id = name, CreatedAt = created, Description = meta.Description, SupportedThinkingLevels = meta.SupportedThinkingLevels, ContextWindowSize = meta.ContextWindowSize, MaxInputTokens = meta.MaxInputTokens, MaxOutputTokens = meta.MaxOutputTokens });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else if (provider == "Anthropic")
            {
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                var response = await client.GetAsync("https://api.anthropic.com/v1/models");
                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest(new { message = $"Anthropic API returned {response.StatusCode}: {await response.Content.ReadAsStringAsync()}" });
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
                            if (name.StartsWith("claude-"))
                            {
                                long created = 0;
                                if (modelElement.TryGetProperty("created_at", out var createdElement))
                                {
                                    var dateStr = createdElement.GetString();
                                    if (!string.IsNullOrEmpty(dateStr) && DateTimeOffset.TryParse(dateStr, out var dto))
                                    {
                                        created = dto.ToUnixTimeSeconds();
                                    }
                                }
                                if (created >= cutoffTimestamp)
                                {
                                    var meta = _modelMetadataService.GetMetadata(name);
                                    models.Add(new ApiModelDto { Id = name, CreatedAt = created, Description = meta.Description, SupportedThinkingLevels = meta.SupportedThinkingLevels, ContextWindowSize = meta.ContextWindowSize, MaxInputTokens = meta.MaxInputTokens, MaxOutputTokens = meta.MaxOutputTokens });
                                }
                            }
                        }
                    }
                }
            }
            else if (provider == "Google")
            {
                var response = await client.GetAsync($"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}");
                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest(new { message = $"Google API returned {response.StatusCode}: {await response.Content.ReadAsStringAsync()}" });
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
                            
                            // Keep gemini models, exclude embedding, aqa, tunedModels unless they are gemini
                            if (name.StartsWith("gemini-") || name.StartsWith("learnlm-"))
                            {
                                if (!name.Contains("embedding") && !name.Contains("robotics") && !name.Contains("omni") && !name.EndsWith("-latest"))
                                {
                                    var meta = _modelMetadataService.GetMetadata(name);
                                    // If strict pattern doesn't match, Description == Id. 
                                    // For Google, we'll exclude if it's older than Jan 1, 2026 (gemini-1.0, gemini-1.5, gemini-2.0, gemini-2.5) by manually filtering based on name prefix
                                    bool isOldModel = name.StartsWith("gemini-1.") || name.StartsWith("gemini-2.");
                                    if (!isOldModel)
                                    {
                                        models.Add(new ApiModelDto { Id = name, CreatedAt = 0, Description = meta.Description, SupportedThinkingLevels = meta.SupportedThinkingLevels, ContextWindowSize = meta.ContextWindowSize, MaxInputTokens = meta.MaxInputTokens, MaxOutputTokens = meta.MaxOutputTokens });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                return BadRequest(new { message = $"Unsupported provider: {provider}" });
            }

            // Apply recommendation flags
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
    public long CreatedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> SupportedThinkingLevels { get; set; } = new List<string>();
    
    public int ContextWindowSize { get; set; }
    public int MaxInputTokens { get; set; }
    public int MaxOutputTokens { get; set; }
    
    public bool IsRecommended { get; set; }
    public int RecommendationRank { get; set; }
    public string? RecommendedThinkingLevel { get; set; }
}

public class UpdateSettingsRequest
{
    public bool? SpoilerFreeMode { get; set; }
    public int? MaxResultLength { get; set; }
    public int? MaxCallsPerSession { get; set; }
    public int? MaxToolIterations { get; set; }
    public bool? EnableWebSearch { get; set; }
    public bool? EnableToolUse { get; set; }
    public bool? EnableClientTools { get; set; }
    public bool? EnableGameActions { get; set; }
    public bool? ShowSourceCodeReferences { get; set; }
    public int? ShowThoughtsAndTools { get; set; }
    public int? RequestTimeout { get; set; }
}

public class SaveApiKeyRequest
{
    public string Provider { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public class AddUserModelRequest
{
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? ThinkingLevel { get; set; }
    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
}

public class UpdateUserModelRequest
{
    public string? DisplayName { get; set; }
    public string? ThinkingLevel { get; set; }
    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
}

public class ReorderModelsRequest
{
    public long[] OrderedIds { get; set; } = Array.Empty<long>();
}

public class GetModelsRequest
{
    public string? Provider { get; set; }
    public string? ApiKey { get; set; }
}

public class UpdateTitleModelRequest
{
    public long? ModelId { get; set; }
    public bool IsSystem { get; set; }
}


using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Overseer.Services;
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

    public SettingsController(SettingsService settingsService, IHttpClientFactory httpClientFactory, IConfiguration configuration, ModelMetadataService modelMetadataService)
    {
        _settingsService = settingsService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _modelMetadataService = modelMetadataService;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var settings = await _settingsService.GetSettingsAsync(userId);
        
        return Ok(new
        {
            provider = settings?.DefaultProvider,
            model = settings?.DefaultModel,
            thinkingLevel = settings?.ThinkingLevel,
            hasApiKey = !string.IsNullOrEmpty(settings?.EncryptedApiKey),
            maxAttachmentSize = _configuration.GetValue<long>("MaxAttachmentSize", 15728640),
            spoilerFreeMode = settings?.SpoilerFreeMode ?? true,
            maxInputTokens = settings?.MaxInputTokens,
            maxOutputTokens = settings?.MaxOutputTokens
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _settingsService.SaveSettingsAsync(userId, request.Provider, request.Model, request.ApiKey, request.ThinkingLevel, request.SpoilerFreeMode, request.MaxInputTokens, request.MaxOutputTokens);
        
        return Ok();
    }
    
    [HttpPost("test")]
    public IActionResult TestSettings([FromBody] UpdateSettingsRequest request)
    {
        // Dummy test endpoint
        if (string.IsNullOrEmpty(request.ApiKey)) return BadRequest("API key is required for testing.");
        
        // In a real implementation, you would make a small API call to the specified provider using the provided key.
        return Ok(new { success = true, message = "Test successful (mock)" });
    }

    [HttpDelete("apikey")]
    public async Task<IActionResult> DeleteApiKey()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _settingsService.DeleteApiKeyAsync(userId);
        return Ok();
    }

    [HttpPost("models")]
    public async Task<IActionResult> GetModels([FromBody] UpdateSettingsRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var provider = request.Provider ?? "OpenAI";
        var apiKey = request.ApiKey;

        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = await _settingsService.GetDecryptedApiKeyAsync(userId);
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
}

public class UpdateSettingsRequest
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public string? ThinkingLevel { get; set; }
    public bool? SpoilerFreeMode { get; set; }
    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
}

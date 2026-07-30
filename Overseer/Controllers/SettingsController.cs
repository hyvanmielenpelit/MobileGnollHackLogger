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

    public SettingsController(SettingsService settingsService, IHttpClientFactory httpClientFactory)
    {
        _settingsService = settingsService;
        _httpClientFactory = httpClientFactory;
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
            hasApiKey = !string.IsNullOrEmpty(settings?.EncryptedApiKey)
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _settingsService.SaveSettingsAsync(userId, request.Provider, request.Model, request.ApiKey, request.ThinkingLevel);
        
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
            var models = new List<string>();

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
                                    models.Add(name);
                                }
                            }
                        }
                    }
                }
                // Sort models alphabetically for better UX
                models.Sort();
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
                                models.Add(name);
                            }
                        }
                    }
                }
                models.Sort();
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
                                models.Add(name);
                            }
                        }
                    }
                }
                models.Sort();
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

public class UpdateSettingsRequest
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public string? ThinkingLevel { get; set; }
}

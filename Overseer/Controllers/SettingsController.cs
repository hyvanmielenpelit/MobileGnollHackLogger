using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Overseer.Services;
using System.Security.Claims;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settingsService;

    public SettingsController(SettingsService settingsService)
    {
        _settingsService = settingsService;
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
            hasApiKey = !string.IsNullOrEmpty(settings?.EncryptedApiKey)
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _settingsService.SaveSettingsAsync(userId, request.Provider, request.Model, request.ApiKey);
        
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
}

public class UpdateSettingsRequest
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
}

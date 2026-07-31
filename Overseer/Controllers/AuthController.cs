using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MobileGnollHackLogger.Data;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Invalid credentials.");

        var user = await _userManager.FindByNameAsync(request.UserName);
        if (user == null)
            return Unauthorized("Invalid credentials.");

        var result = await _signInManager.PasswordSignInAsync(user.UserName!, request.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return Ok(new { userName = user.UserName, email = user.Email });
        }

        return Unauthorized(new { message = "Invalid credentials.", result = result });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        Response.Cookies.Delete("XSRF-TOKEN");
        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me([FromServices] ApplicationDbContext dbContext)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var settings = await dbContext.UserAiSettings.FindAsync(user.Id);
        bool hasApiKey = settings != null && !string.IsNullOrEmpty(settings.EncryptedApiKey);

        return Ok(new
        {
            userName = user.UserName,
            email = user.Email,
            hasApiKey = hasApiKey
        });
    }

    [HttpGet("handoff")]
    public async Task<IActionResult> Handoff([FromQuery] string token, [FromQuery] long sessionId, [FromServices] Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        if (string.IsNullOrEmpty(token) || sessionId <= 0) return BadRequest();

        var cacheKey = $"handoff_{token}";
        if (cache.TryGetValue(cacheKey, out var rawData) && rawData is HandoffData data)
        {
            if (data.SessionId != sessionId) return BadRequest();

            var user = await _userManager.FindByIdAsync(data.UserId);
            if (user != null)
            {
                await _signInManager.SignInAsync(user, isPersistent: true);
                cache.Remove(cacheKey); // Single-use
                
                // Return a client-side meta refresh instead of HTTP 302 to preserve the cookie in iOS WKWebView
                var html = $@"<!DOCTYPE html><html><head><meta http-equiv=""refresh"" content=""0;url=/chat?sessionId={sessionId}""></head><body>Redirecting...</body></html>";
                return Content(html, "text/html");
            }
        }

        return Unauthorized();
    }
}

public class LoginRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class HandoffData
{
    public string UserId { get; set; } = string.Empty;
    public long SessionId { get; set; }
}

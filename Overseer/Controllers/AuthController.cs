using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MobileGnollHackLogger.Data;
using Overseer.Extensions;

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
    public async Task<IActionResult> Me([FromServices] ApplicationDbContext dbContext, [FromServices] IConfiguration configuration)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Ok(null);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Ok(null);
        }

        bool hasApiKey = dbContext.UserAiApiKeys.Any(k => k.AspNetUserId == user.Id && !string.IsNullOrEmpty(k.EncryptedApiKey));
        bool isAdmin = configuration.IsAdmin(user.UserName);

        return Ok(new
        {
            userName = user.UserName,
            email = user.Email,
            hasApiKey = hasApiKey,
            isAdmin = isAdmin
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
                var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <meta name=""theme-color"" content=""#121212"">
    <meta http-equiv=""refresh"" content=""0;url=/chat?sessionId={sessionId}"">
    <title>Gnoll Overseer</title>
    <style>
        html, body {{
            margin: 0;
            padding: 0;
            width: 100%;
            height: 100%;
            background-color: #121212;
            color: #ffffff;
            font-family: 'Lato', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
        }}
        .initial-loading-container {{
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            height: 100vh;
            height: 100dvh;
            background: radial-gradient(circle at center, #1a1a1a 0%, #0a0a0a 100%);
        }}
        .initial-loading-title {{
            font-family: 'Cinzel', serif;
            font-size: 3rem;
            font-weight: normal;
            color: #d4af37;
            letter-spacing: 2px;
            margin-bottom: 30px;
            text-shadow: 0 0 15px rgba(212, 175, 55, 0.4);
            text-align: center;
        }}
        .initial-spinner {{
            width: 50px;
            height: 50px;
            border: 4px solid rgba(212, 175, 55, 0.2);
            border-top-color: #d4af37;
            border-radius: 50%;
            animation: spin 1s linear infinite;
            margin-bottom: 20px;
        }}
        .initial-loading-text {{
            font-size: 1.1rem;
            color: #aaaaaa;
            letter-spacing: 1px;
            animation: pulse 1.5s ease-in-out infinite;
        }}
        @keyframes spin {{
            0% {{ transform: rotate(0deg); }}
            100% {{ transform: rotate(360deg); }}
        }}
        @keyframes pulse {{
            0%, 100% {{ opacity: 0.6; }}
            50% {{ opacity: 1; }}
        }}
    </style>
</head>
<body>
    <div class=""initial-loading-container"">
        <h1 class=""initial-loading-title"">Gnoll Overseer</h1>
        <div class=""initial-spinner""></div>
        <div class=""initial-loading-text"">Initializing...</div>
    </div>
    <script>
        setTimeout(function() {{
            window.location.replace('/chat?sessionId={sessionId}');
        }}, 50);
    </script>
</body>
</html>";
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

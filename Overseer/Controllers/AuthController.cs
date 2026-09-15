using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Extensions;
using Overseer.Services;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;

    public AuthController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _dbContext = dbContext;
    }

    /* Every failure below answers with exactly this object. An unknown user and a wrong
       password must be indistinguishable, so nothing about the account -- not its existence,
       not its lockout state, not whether it has two-factor enabled -- may vary with the
       response. In particular SignInResult is never serialised: it carries IsLockedOut and
       RequiresTwoFactor, and returning it to a caller who has not proved the password turns
       the endpoint into a user-enumeration oracle. */
    private IActionResult InvalidCredentials()
        => Unauthorized(new { message = "Invalid credentials." });

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            return InvalidCredentials();

        var user = await _userManager.FindByNameAsync(request.UserName);
        if (user == null)
            return InvalidCredentials();

        var result = await _signInManager.PasswordSignInAsync(user.UserName!, request.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return Ok(new { userName = user.UserName, email = user.Email });
        }

        /* Each of these means the password was correct, so the caller has proved enough to be
           told what is actually blocking the sign-in. PasswordSignInAsync has already stored
           the two-factor user id in the TwoFactorUserIdScheme cookie, which is what
           /login/2fa then completes. Without this branch, enabling TOTP locked a user out of
           Overseer entirely: the strongest control available acted as a penalty. */
        if (result.RequiresTwoFactor)
        {
            var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
            return Ok(new
            {
                requiresTwoFactor = true,
                hasAuthenticator = providers.Contains(TokenOptions.DefaultAuthenticatorProvider)
            });
        }

        if (result.IsLockedOut)
        {
            return Unauthorized(new
            {
                message = "This account is temporarily locked after too many failed sign-in attempts. Try again later.",
                isLockedOut = true
            });
        }

        if (result.IsNotAllowed)
        {
            return Unauthorized(new
            {
                message = "This account is not permitted to sign in. Confirm your e-mail address and try again.",
                isNotAllowed = true
            });
        }

        return InvalidCredentials();
    }

    /// <summary>
    /// Second step of a two-factor sign-in: completes the login begun by
    /// <see cref="Login"/> using a TOTP code from the user's authenticator app.
    /// </summary>
    [HttpPost("login/2fa")]
    public async Task<IActionResult> LoginTwoFactor([FromBody] TwoFactorLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return InvalidCredentials();

        /* Resolves the user from the TwoFactorUserIdScheme cookie PasswordSignInAsync set, so
           this endpoint cannot be used without having first passed the password step. */
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null)
            return InvalidCredentials();

        // Authenticator apps show the code in groups; accept it however the user typed it.
        string code = request.Code.Replace(" ", string.Empty).Replace("-", string.Empty);

        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(
            code, isPersistent: true, rememberClient: request.RememberMachine);

        if (result.Succeeded)
            return Ok(new { userName = user.UserName, email = user.Email });

        if (result.IsLockedOut)
        {
            return Unauthorized(new
            {
                message = "This account is temporarily locked after too many failed sign-in attempts. Try again later.",
                isLockedOut = true
            });
        }

        return Unauthorized(new { message = "Invalid verification code." });
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

                /* The handoff session is the only one carrying the host's real game state, so the
                   SPA is told here rather than per session. sessionId is a long and gameOn is
                   "0"/"1", so nothing user-controlled reaches the markup below. */
                string? clientSettings = await _dbContext.ChatSession
                    .AsNoTracking()
                    .Where(s => s.Id == sessionId && s.AspNetUserId == user.Id)
                    .Select(s => s.ClientSettings)
                    .FirstOrDefaultAsync();
                string? gameOn = ResolveGameOnFlag(clientSettings);
                // The separator is written as an entity because the target sits in an HTML attribute.
                string target = $"/chat?sessionId={sessionId}" + (gameOn is null ? "" : $"&amp;gameOn={gameOn}");
                string gameOnAttribute = gameOn is null ? "" : $@" data-game-on=""{gameOn}""";

                // Return a client-side meta refresh instead of HTTP 302 to preserve the cookie in iOS WKWebView
                var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <meta name=""theme-color"" content=""#121212"">
    <meta http-equiv=""refresh"" content=""0;url={target}"">
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
    <!-- The redirect itself is the <meta http-equiv=""refresh""> above; this script is only a
         50 ms fast path. It lives in a static file because the CSP's script-src is 'self',
         which blocks an inline script. Target read from data attributes rather than
         interpolated into the script, so the file stays static. data-game-on is absent when
         the host reported no game state. -->
    <script src=""/js/handoff-redirect.js"" data-session-id=""{sessionId}""{gameOnAttribute}></script>
</body>
</html>";
                return Content(html, "text/html");
            }
        }

        return Unauthorized();
    }

    /// <summary>"1" when the host reported a running game and game context is not opted out,
    /// "0" when either is explicitly false, null when the host reported nothing.</summary>
    internal static string? ResolveGameOnFlag(string? clientSettings)
    {
        bool? isGameOn = ClientSettingsReader.ReadBool(clientSettings, "isGameOn");
        bool? sendGameContext = ClientSettingsReader.ReadBool(clientSettings, "sendGameContext");
        if (isGameOn == false || sendGameContext == false) return "0";
        if (isGameOn == true) return "1";
        return null;
    }
}

public class LoginRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class TwoFactorLoginRequest
{
    /// <summary>The TOTP code, with spaces and dashes tolerated.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Whether to skip the second factor on this browser next time.</summary>
    public bool RememberMachine { get; set; }
}

public class HandoffData
{
    public string UserId { get; set; } = string.Empty;
    public long SessionId { get; set; }
}

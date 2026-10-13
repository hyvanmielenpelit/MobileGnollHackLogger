using GnollHackServer.Data.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Areas.API
{
    /// <summary>The outcome of a game API sign-in: the user, or the response to return.</summary>
    public sealed class GameApiAuthResult
    {
        public bool Succeeded { get; init; }
        public ApplicationUser? User { get; init; }

        /// <summary>The response to return when the sign-in did not succeed.</summary>
        public IActionResult? Response { get; init; }
    }

    /// <summary>
    /// The antiforgery check, the sign-in throttle and the password check every game API repeats. The password is
    /// checked without issuing a cookie; failures count toward <see cref="GameApiSignInThrottle"/>, never toward
    /// Identity lockout. A throttled request costs no password hash and writes no request-log row. Every refusal a
    /// player can trigger is a 403 with a plain-text body (<see cref="GameApiSignInResponses"/>).
    /// </summary>
    public class GameApiAuthenticator
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly GameApiSignInThrottle _throttle;
        private readonly ApplicationDbContext _dbContext;

        public GameApiAuthenticator(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
            IConfiguration configuration, GameApiSignInThrottle throttle, ApplicationDbContext dbContext)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _configuration = configuration;
            _throttle = throttle;
            _dbContext = dbContext;
        }

        /// <summary>Signs in a game client; <paramref name="route"/> names the endpoint in events, e.g. "xlogfile".</summary>
        public async Task<GameApiAuthResult> AuthenticateAsync(HttpContext http, DbLogger dbLogger, string userName, string password,
            string? antiForgeryToken, string route)
        {
            string? ip = http.Connection.RemoteIpAddress?.ToString();

            bool tokenMatches = string.Equals(_configuration["AntiForgeryToken"], antiForgeryToken, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(antiForgeryToken);
            dbLogger.AntiForgeryTokenMatched = tokenMatches;
            if (!tokenMatches)
            {
                var mismatch = GameApiSignInResponses.For(GameApiSignInOutcome.AntiForgeryMismatch);
                await dbLogger.LogRequestAsync("AntiForgeryToken is invalid.", Data.LogLevel.Warning, mismatch.StatusCode);
                return Fail(http, mismatch);
            }

            var decision = _throttle.Check(userName, ip);
            if (!decision.Allowed)
            {
                await SecurityEventWriter.RecordAsync(_dbContext, SecurityEventKind.RateLimitRejected, ipAddress: ip,
                    code: "game-api-signin", detail: decision.Reason);
                return Fail(http, GameApiSignInResponses.For(GameApiSignInOutcome.Throttled, decision.RetryAfter));
            }

            var user = await _userManager.FindByNameAsync(userName);
            if (user == null)
            {
                return await FailedPasswordAsync(http, dbLogger, userName, null, ip, route);
            }

            var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
            if (result.IsLockedOut)
            {
                dbLogger.LoginSucceeded = false;
                TimeSpan? remaining = user.LockoutEnd is DateTimeOffset end ? end - DateTimeOffset.UtcNow : null;
                var locked = GameApiSignInResponses.For(GameApiSignInOutcome.LockedOut,
                    remaining is TimeSpan r && r > TimeSpan.Zero ? r : TimeSpan.FromMinutes(1));
                await dbLogger.LogRequestAsync("User is locked out.", Data.LogLevel.Warning, locked.StatusCode);
                return Fail(http, locked);
            }

            if (!result.Succeeded)
            {
                return await FailedPasswordAsync(http, dbLogger, userName, user.Id, ip, route);
            }

            _throttle.RecordSuccess(userName);

            // As PasswordSignInAsync decides RequiresTwoFactor: 2FA enabled and a valid provider.
            if (await _userManager.GetTwoFactorEnabledAsync(user)
                && (await _userManager.GetValidTwoFactorProvidersAsync(user)).Count > 0)
            {
                dbLogger.LoginSucceeded = false;
                var twoFactor = GameApiSignInResponses.For(GameApiSignInOutcome.RequiresTwoFactor);
                await dbLogger.LogRequestAsync("Login requires two factor authentication. Error.", Data.LogLevel.Error, twoFactor.StatusCode);
                return Fail(http, twoFactor);
            }

            dbLogger.LoginSucceeded = true;
            return new GameApiAuthResult { Succeeded = true, User = user };
        }

        /// <summary>The 403 a request gets when the endpoint has nothing to do after a successful sign-in.</summary>
        public static IActionResult SignInFailedResponse(HttpContext http) =>
            ToResult(http, GameApiSignInResponses.For(GameApiSignInOutcome.Failed));

        private async Task<GameApiAuthResult> FailedPasswordAsync(HttpContext http, DbLogger dbLogger, string userName, string? userId,
            string? ip, string route)
        {
            dbLogger.LoginSucceeded = false;
            _throttle.RecordFailure(userName, ip);
            await SecurityEventWriter.RecordAsync(_dbContext, SecurityEventKind.LoginFailed, userId, ip,
                code: "game-api:" + route, detail: userId == null ? "unknown-user" : null);

            var failed = GameApiSignInResponses.For(GameApiSignInOutcome.Failed);
            await dbLogger.LogRequestAsync($"Login failed for user '{userName}'.", Data.LogLevel.Warning, failed.StatusCode);
            return Fail(http, failed);
        }

        private static GameApiAuthResult Fail(HttpContext http, GameApiResponse response) =>
            new() { Succeeded = false, Response = ToResult(http, response) };

        private static IActionResult ToResult(HttpContext http, GameApiResponse response)
        {
            if (response.RetryAfterSeconds is int seconds)
            {
                http.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return new ContentResult
            {
                StatusCode = response.StatusCode,
                Content = response.Body,
                ContentType = GameApiResponse.ContentType
            };
        }
    }
}

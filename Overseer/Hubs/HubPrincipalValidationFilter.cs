using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MobileGnollHackLogger.Data;
using Overseer.Services.Privacy;

namespace Overseer.Hubs;

/// <summary>
/// Re-checks a hub caller's account and security stamp on connect, and then at most once per
/// <see cref="SecurityStampValidatorOptions.ValidationInterval"/> per connection, ending a
/// connection whose account is gone or whose stamp has changed.
/// </summary>
/// <remarks>
/// <para>
/// The authentication cookie is validated only on the requests that establish the connection, and
/// <see cref="HubCallerContext.User"/> keeps that principal for the connection's life, so without
/// this filter a ban, a lockout, <em>Sign out everywhere</em> or an account deletion would not reach
/// an open chat connection. A rejected call never runs its hub method, so it cannot refresh an
/// incognito session's sliding window either.
/// </para>
/// <para>
/// An account found gone also has its incognito sessions erased; a changed stamp leaves them,
/// because ownership is by user id. An unreachable database fails open: the call proceeds and the
/// check runs again on the next call, while HTTP requests stay guarded by the cookie validator.
/// </para>
/// <para>
/// <c>OverseerWebApplicationFactory</c>'s test principal carries no security stamp claim, so an
/// integration test that opens <c>/chathub</c> is rejected by design.
/// </para>
/// </remarks>
public sealed class HubPrincipalValidationFilter : IHubFilter
{
    internal enum PrincipalVerdict { Valid, AccountGone, StampChanged, Anonymous, Unavailable }

    internal const string SignInEndedMessage = "Your sign-in has ended. Sign in again.";

    /// <summary>The <see cref="HubCallerContext.Items"/> key of the last successful check's time.</summary>
    private static readonly object LastValidatedKey = new();

    private readonly TimeSpan _interval;
    private readonly string _stampClaimType;
    private readonly ILogger<HubPrincipalValidationFilter> _logger;
    private readonly TimeProvider _time;

    public HubPrincipalValidationFilter(
        IOptions<SecurityStampValidatorOptions> stampOptions,
        IOptions<IdentityOptions> identityOptions,
        ILogger<HubPrincipalValidationFilter> logger,
        TimeProvider? time = null)
    {
        _interval = stampOptions.Value.ValidationInterval;
        _stampClaimType = identityOptions.Value.ClaimsIdentity.SecurityStampClaimType;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var verdict = await CheckAsync(context.Context, context.ServiceProvider, context.Context.ConnectionAborted);
        if (IsRejected(verdict))
        {
            Reject(context.Context, context.ServiceProvider, verdict);
            throw new HubException(SignInEndedMessage);
        }

        if (verdict == PrincipalVerdict.Valid)
        {
            context.Context.Items[LastValidatedKey] = _time.GetUtcNow();
        }

        await next(context);
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var ctx = invocationContext.Context;
        if (ctx.Items.TryGetValue(LastValidatedKey, out var stored)
            && stored is DateTimeOffset lastValidated
            && _time.GetUtcNow() - lastValidated < _interval)
        {
            return await next(invocationContext);
        }

        var verdict = await CheckAsync(ctx, invocationContext.ServiceProvider, ctx.ConnectionAborted);
        if (IsRejected(verdict))
        {
            Reject(ctx, invocationContext.ServiceProvider, verdict);
            ctx.Abort();
            throw new HubException(SignInEndedMessage);
        }

        if (verdict == PrincipalVerdict.Valid)
        {
            ctx.Items[LastValidatedKey] = _time.GetUtcNow();
        }

        return await next(invocationContext);
    }

    /// <summary>
    /// Compares the caller's principal with the account's current row: gone, a different stamp,
    /// no user id, or a database that could not be read.
    /// </summary>
    internal async Task<PrincipalVerdict> CheckAsync(HubCallerContext ctx, IServiceProvider sp, CancellationToken ct)
    {
        var userId = UserIdOf(ctx);
        if (string.IsNullOrEmpty(userId)) return PrincipalVerdict.Anonymous;

        try
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var row = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.SecurityStamp })
                .FirstOrDefaultAsync(ct);

            if (row == null) return PrincipalVerdict.AccountGone;

            return string.Equals(row.SecurityStamp, ctx.User?.FindFirstValue(_stampClaimType), StringComparison.Ordinal)
                ? PrincipalVerdict.Valid
                : PrincipalVerdict.StampChanged;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Hub connection {ConnectionId}: the caller's account could not be checked; the call proceeds.",
                ctx.ConnectionId);
            return PrincipalVerdict.Unavailable;
        }
    }

    private static bool IsRejected(PrincipalVerdict verdict)
        => verdict is PrincipalVerdict.AccountGone or PrincipalVerdict.StampChanged or PrincipalVerdict.Anonymous;

    private void Reject(HubCallerContext ctx, IServiceProvider sp, PrincipalVerdict verdict)
    {
        if (verdict == PrincipalVerdict.AccountGone)
        {
            sp.GetService<EphemeralSessionStore>()?.CloseAllForUser(UserIdOf(ctx));
        }

        _logger.LogInformation("Hub connection {ConnectionId} rejected: {Verdict}.", ctx.ConnectionId, verdict);
    }

    private static string? UserIdOf(HubCallerContext ctx)
        => ctx.UserIdentifier ?? ctx.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}

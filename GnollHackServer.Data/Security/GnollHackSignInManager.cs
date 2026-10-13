using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MobileGnollHackLogger.Data;

namespace GnollHackServer.Data.Security;

/// <summary>
/// The sign-in manager of the website and Overseer. A banned account cannot sign in on any path: Identity's pre-sign-in
/// check reports IsNotAllowed for password, two-factor, recovery-code and external sign-ins and for
/// <see cref="SignInManager{TUser}.CheckPasswordSignInAsync"/>, before the password is hashed. A completed sign-in
/// records <see cref="ApplicationUser.LastSignInUtc"/> through <see cref="AccountActivity"/>.
/// </summary>
public class GnollHackSignInManager : SignInManager<ApplicationUser>
{
    private readonly ApplicationDbContext _db;

    public GnollHackSignInManager(UserManager<ApplicationUser> userManager, IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory, IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger, IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation, ApplicationDbContext db)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
        _db = db;
    }

    public override async Task<bool> CanSignInAsync(ApplicationUser user)
    {
        if (user.IsBanned == true)
        {
            return false;
        }
        return await base.CanSignInAsync(user);
    }

    /// <summary>Every cookie sign-in ends here, including the bool overload, two-factor and external sign-ins.</summary>
    public override async Task SignInWithClaimsAsync(ApplicationUser user, AuthenticationProperties? authenticationProperties,
        IEnumerable<Claim> additionalClaims)
    {
        await base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);
        await AccountActivity.TouchSignInAsync(_db, user.Id, DateTime.UtcNow, Logger);
    }
}

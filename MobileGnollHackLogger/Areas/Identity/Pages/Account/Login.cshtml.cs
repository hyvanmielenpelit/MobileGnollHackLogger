// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Areas.Identity.Pages.Account
{
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(MobileGnollHackLogger.Areas.API.GameApiRateLimits.AccountPages)]
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly IConfiguration _configuration;

        public LoginModel(SignInManager<ApplicationUser> signInManager, ILogger<LoginModel> logger, ApplicationDbContext dbContext,
            IConfiguration configuration)
        {
            _signInManager = signInManager;
            _logger = logger;
            _dbContext = dbContext;
            _configuration = configuration;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string ReturnUrl { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string ErrorMessage { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            [Required]
            [StringLength(31, ErrorMessage = "Maximum length is 31 characters.")]
            [RegularExpression($"^[a-zA-Z0-9][a-zA-Z0-9_]*$", ErrorMessage = "Only alphanumeric characters and underscore allowed. Can't start with an underscore.")]
            [Display(Name = "User Name")]
            public string UserName { get; set; }

            ///// <summary>
            /////     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            /////     directly from your code. This API may change or be removed in future releases.
            ///// </summary>
            //[Required]
            //[EmailAddress]
            //public string Email { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");

            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                // Failures count toward account lockout (IdentityLockout settings, shared with Overseer).
                // The game APIs answer a locked-out account with 403, so a lockout never sends a game client into a retry loop.
                var result = await _signInManager.PasswordSignInAsync(Input.UserName, Input.Password, Input.RememberMe, lockoutOnFailure: true);
                if (result.Succeeded)
                {
                    _logger.LogInformation("User logged in.");
                    return LocalRedirect(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
                }

                // The attempted user name is never stored; the user id only when an account matched.
                var matchedUser = await _signInManager.UserManager.FindByNameAsync(Input.UserName);
                string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

                /* Identity refuses a banned or unconfirmed account before it checks lockout or the password, so both are
                   checked here: a locked-out account is refused before any password check, a wrong password counts toward
                   lockout as on any other account, and the ban is told only to whoever has the password. */
                bool notAllowedLockedOut = false;
                if (result.IsNotAllowed && matchedUser != null)
                {
                    var userManager = _signInManager.UserManager;
                    if (await userManager.IsLockedOutAsync(matchedUser))
                    {
                        notAllowedLockedOut = true;
                    }
                    else if (!await userManager.CheckPasswordAsync(matchedUser, Input.Password))
                    {
                        await userManager.AccessFailedAsync(matchedUser);
                    }
                    else if (matchedUser.IsBanned == true)
                    {
                        _logger.LogWarning("Banned user attempted to log in.");
                        string contact = _configuration["ReportEmailAddress"];
                        ModelState.AddModelError(string.Empty, "This account has been banned. " + (string.IsNullOrWhiteSpace(contact)
                            ? "Contact the GnollHack team if you think this is a mistake."
                            : $"Contact {contact} if you think this is a mistake."));
                        return Page();
                    }
                }

                if (result.IsLockedOut || notAllowedLockedOut)
                {
                    _logger.LogWarning("User account locked out.");
                    await GnollHackServer.Data.Security.SecurityEventWriter.RecordAsync(_dbContext, SecurityEventKind.LoginLockedOut,
                        matchedUser?.Id, ipAddress, code: "website-login");
                    return RedirectToPage("./Lockout");
                }
                else
                {
                    await GnollHackServer.Data.Security.SecurityEventWriter.RecordAsync(_dbContext, SecurityEventKind.LoginFailed,
                        matchedUser?.Id, ipAddress, code: "website-login", detail: matchedUser == null ? "unknown-user" : null);
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return Page();
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }
    }
}

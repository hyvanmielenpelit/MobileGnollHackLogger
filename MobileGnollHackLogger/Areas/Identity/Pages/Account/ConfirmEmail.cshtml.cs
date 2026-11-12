// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using MobileGnollHackLogger.Data;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MobileGnollHackLogger.Areas.Identity.Pages.Account
{
    [MobileGnollHackLogger.Filters.AllowWithoutAccountTerms]
    public class ConfirmEmailModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly LogFileLogger _logger;

        public ConfirmEmailModel(UserManager<ApplicationUser> userManager, IConfiguration configuration)
        {
            _userManager = userManager;
            _configuration = configuration;
            _logger = new LogFileLogger(_configuration);
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string StatusMessage { get; set; }

        public bool IsSuccess { get; set; }
        /// <summary>
        ///     Confirms the address from the emailed link, then redirects to this page's own path, which renders the
        ///     outcome kept in TempData once.
        /// </summary>
        public async Task<IActionResult> OnGetAsync(string userId, string code)
        {
            if (userId == null || code == null)
            {
                if (userId == null && code == null && TempData[IdentityTempDataKeys.ConfirmEmailSucceeded] is bool succeeded)
                {
                    IsSuccess = succeeded;
                    return Page();
                }
                return RedirectToPage("/Index");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{userId}'.");
            }

            string decoded_code = null;
            try
            {
                decoded_code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            }
            catch (FormatException)
            {
            }

            if (decoded_code == null)
            {
                IsSuccess = false;
                StatusMessage = "Error confirming your email.";
            }
            else
            {
                var result = await _userManager.ConfirmEmailAsync(user, decoded_code);
                IsSuccess = result.Succeeded;
                StatusMessage = IsSuccess ? "Thank you for confirming your email." : "Error confirming your email.";
                foreach (var error in result.Errors)
                {
                    StatusMessage += " " + error.Description;
                }
            }

            TempData[IdentityTempDataKeys.ConfirmEmailSucceeded] = IsSuccess;
            return RedirectToPage();
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    [MobileGnollHackLogger.Filters.AllowWithoutAccountTerms]
    public class RegisterConfirmationModel : PageModel
    {
        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public bool DisplayConfirmAccountLink { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string EmailConfirmationUrl { get; set; }

        /// <summary>
        ///     Renders the card after a registration sent its confirmation email. A request with a query, such as a
        ///     redirect that carried the address, is redirected to this page's own path.
        /// </summary>
        public IActionResult OnGet()
        {
            if (Request.QueryString.HasValue)
            {
                TempData[IdentityTempDataKeys.RegistrationEmailSent] = true;
                return RedirectToPage();
            }

            if (TempData.Peek(IdentityTempDataKeys.RegistrationEmailSent) is not true)
            {
                return RedirectToPage("/Index");
            }

            // Once you add a real email sender, you should remove this code that lets you confirm the account
            //DisplayConfirmAccountLink = true;
            //if (DisplayConfirmAccountLink)
            //{
            //    var userId = await _userManager.GetUserIdAsync(user);
            //    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            //    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            //    EmailConfirmationUrl = Url.Page(
            //        "/Account/ConfirmEmail",
            //        pageHandler: null,
            //        values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
            //        protocol: Request.Scheme);
            //}

            return Page();
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using MobileGnollHackLogger.Data;
namespace MobileGnollHackLogger.Areas.Identity.Pages.Account;

[MobileGnollHackLogger.Filters.AllowWithoutAccountTerms]
public class ConfirmEmailChangeModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public ConfirmEmailChangeModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    /// <summary>
    ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    [TempData]
    public string? StatusMessage { get; set; }

    public bool IsSuccess { get; set; }

    /// <summary>
    ///     Changes the address from the emailed link, then redirects to this page's own path, which renders the outcome
    ///     kept in TempData once.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(string? userId, string? email, string? code)
    {
        if (userId == null || email == null || code == null)
        {
            if (userId == null && email == null && code == null
                && TempData[IdentityTempDataKeys.ConfirmEmailChangeSucceeded] is bool succeeded)
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

        string? decodedCode = null;
        try
        {
            decodedCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
        }

        if (decodedCode == null || !(await _userManager.ChangeEmailAsync(user, email, decodedCode)).Succeeded)
        {
            StatusMessage = "Error changing email.";
            TempData[IdentityTempDataKeys.ConfirmEmailChangeSucceeded] = false;
            return RedirectToPage();
        }

        await _signInManager.RefreshSignInAsync(user);
        StatusMessage = "Thank you for confirming your email change.";
        TempData[IdentityTempDataKeys.ConfirmEmailChangeSucceeded] = true;
        return RedirectToPage();
    }
}

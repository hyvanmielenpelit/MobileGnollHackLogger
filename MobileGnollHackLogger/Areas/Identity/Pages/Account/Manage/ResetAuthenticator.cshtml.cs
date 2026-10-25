// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MobileGnollHackLogger.Areas.Identity.Pages.Account.Manage;

/// <summary>
/// Shadows the Identity UI page at this path, which would otherwise be served: two-factor setup is not
/// offered, because the GnollHack game cannot sign in to an account that uses it.
/// </summary>
public class ResetAuthenticatorModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("./TwoFactorAuthentication");

    public IActionResult OnPost() => RedirectToPage("./TwoFactorAuthentication");
}

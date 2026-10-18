// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using GnollHackServer.Data.Privacy;
using GnollHackServer.Data.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using MobileGnollHackLogger.Filters;

namespace MobileGnollHackLogger.Areas.Identity.Pages.Account.Manage;

[AllowWithoutAccountTerms]
public class DeletePersonalDataModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<DeletePersonalDataModel> _logger;
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly AccountHoldOptions _holdOptions;
    private readonly IEmailSender _emailSender;

    public DeletePersonalDataModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<DeletePersonalDataModel> logger,
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        AccountHoldOptions holdOptions,
        IEmailSender emailSender)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
        _dbContext = dbContext;
        _configuration = configuration;
        _holdOptions = holdOptions;
        _emailSender = emailSender;
    }

    /// <summary>Shown instead of the form when the deletion is held for, or already waiting for, administrator review.</summary>
    public string? ReviewMessage { get; set; }

    private void LogReviewEmailFailure(Exception ex) =>
        _logger.LogError(ex, "The account deletion review email could not be sent.");

    /// <summary>
    ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    [BindProperty]
    public InputModel Input { get; set; } = default!;

    /// <summary>
    ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class InputModel
    {
        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = default!;
    }

    /// <summary>
    ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public bool RequirePassword { get; set; }

    public async Task<IActionResult> OnGet()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        RequirePassword = await _userManager.HasPasswordAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        RequirePassword = await _userManager.HasPasswordAsync(user);
        if (RequirePassword)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            if (!await _userManager.CheckPasswordAsync(user, Input.Password))
            {
                ModelState.AddModelError(string.Empty, "Incorrect password.");
                return Page();
            }
        }

        var userId = user.Id;
        var now = DateTime.UtcNow;

        // A request already waiting for review: nothing else happens, and the review email is repeated only when due.
        var pending = await _dbContext.AccountDeletionRequests
            .FirstOrDefaultAsync(r => r.AspNetUserId == userId && r.Status == AccountDeletionRequestStatus.Pending);
        if (pending != null)
        {
            var currentVerdict = await AccountHoldQueries.EvaluateAsync(_dbContext, userId, _holdOptions, now);
            await AccountDeletionReviewEmail.SendIfDueAsync(_dbContext, pending, _emailSender, _holdOptions,
                currentVerdict.Manual, currentVerdict.Automatic, now, LogReviewEmailFailure);
            ReviewMessage = $"Your request to delete your account is being reviewed by an administrator (since {pending.RequestedUtc:yyyy-MM-dd}). You will receive an email when it is done.";
            return Page();
        }

        // Held for review: nothing is deleted and the user stays signed in. The user is not told why.
        var verdict = await AccountHoldQueries.EvaluateAsync(_dbContext, userId, _holdOptions, now);
        if (verdict.IsHeld)
        {
            var request = new AccountDeletionRequest
            {
                AspNetUserId = userId,
                UserNameSnapshot = user.UserName,
                EmailSnapshot = user.Email,
                RequestedUtc = now,
                Status = AccountDeletionRequestStatus.Pending,
                HoldReasons = verdict.Reasons.Length <= 2048 ? verdict.Reasons : verdict.Reasons[..2048]
            };
            _dbContext.AccountDeletionRequests.Add(request);
            await _dbContext.SaveChangesAsync();

            await SecurityEventWriter.RecordAsync(_dbContext, SecurityEventKind.AccountDeletionHeld, userId,
                code: verdict.Manual ? "manual" : "automatic");
            await AccountDeletionReviewEmail.SendIfDueAsync(_dbContext, request, _emailSender, _holdOptions,
                verdict.Manual, verdict.Automatic, now, LogReviewEmailFailure);

            _logger.LogInformation("Deletion of user with ID '{UserId}' is held for administrator review.", userId);
            ReviewMessage = "Your request to delete your account has been received. It needs to be reviewed by an administrator before it can be completed. You will receive an email when it is done.";
            return Page();
        }

        var erased = await AccountErasure.EraseAsync(_dbContext, _userManager, user,
            new AccountErasurePaths(_configuration["DumpLogPath"], _configuration["ConversationsDataLocation"]));
        if (erased.BonesFilesMissing > 0 || erased.FoldersMissing > 0)
        {
            _logger.LogWarning("Account erasure did not find {BonesFiles} bones file(s) and {Folders} folder(s) on disk.",
                erased.BonesFilesMissing, erased.FoldersMissing);
        }

        await _signInManager.SignOutAsync();

        _logger.LogInformation("User with ID '{UserId}' deleted themselves.", userId);

        return Redirect("~/");
    }
}

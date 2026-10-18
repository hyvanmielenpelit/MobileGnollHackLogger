using System.ComponentModel.DataAnnotations;
using GnollHackServer.Data.Legal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Filters;

namespace MobileGnollHackLogger.Areas.Identity.Pages.Account;

/// <summary>Where a signed-in user accepts the required version of the GnollHack Account Terms of Service.</summary>
[Authorize]
[AllowWithoutAccountTerms]
public class AcceptTermsModel : PageModel
{
    private const string UpdatedMessage = "The Terms of Service were updated. Please review them.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TermsAcceptanceStore _termsStore;
    private readonly LegalDocumentReader _legalReader;
    private readonly ILogger<AcceptTermsModel> _logger;

    public AcceptTermsModel(
        UserManager<ApplicationUser> userManager,
        TermsAcceptanceStore termsStore,
        LegalDocumentReader legalReader,
        ILogger<AcceptTermsModel> logger)
    {
        _userManager = userManager;
        _termsStore = termsStore;
        _legalReader = legalReader;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? TermsRequiredVersion { get; set; }

    public string? TermsUrl { get; set; }

    public string? PrivacyPolicyUrl { get; set; }

    public class InputModel
    {
        [Range(typeof(bool), "true", "true", ErrorMessage = "Please accept the Terms of Service to continue.")]
        [Display(Name = "I accept the Terms of Service")]
        public bool AcceptTerms { get; set; }

        /// <summary>The Terms version shown when the form was rendered.</summary>
        public string? TermsVersion { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        string? userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var status = await _termsStore.GetStatusAsync(userId, LegalDocumentKeys.AccountTerms, HttpContext.RequestAborted);
        if (status.IsAccepted)
        {
            return RedirectToReturnUrl();
        }

        await LoadTermsAsync();
        Input = new InputModel { TermsVersion = TermsRequiredVersion };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        string? userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        await LoadTermsAsync();
        if (TermsRequiredVersion == null)
        {
            return RedirectToReturnUrl();
        }

        if (!string.Equals(Input.TermsVersion, TermsRequiredVersion, StringComparison.Ordinal))
        {
            return TermsUpdated();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var document = await _legalReader.GetAsync(LegalDocumentKeys.AccountTerms);
        var result = await _termsStore.RecordAsync(userId, LegalDocumentKeys.AccountTerms, TermsRequiredVersion,
            LegalDocumentKeys.SourceAccountSite, document?.ContentSha256, null, HttpContext.RequestAborted);

        if (result == TermsRecordResult.StaleVersion)
        {
            await LoadTermsAsync();
            return TermsRequiredVersion == null ? RedirectToReturnUrl() : TermsUpdated();
        }

        _logger.LogInformation("User accepted the account Terms version {Version}.", TermsRequiredVersion);
        return RedirectToReturnUrl();
    }

    private async Task LoadTermsAsync()
    {
        TermsRequiredVersion = await _termsStore.GetRequiredVersionAsync(LegalDocumentKeys.AccountTerms, HttpContext.RequestAborted);
        TermsUrl = _legalReader.GetWikiUrl(LegalDocumentKeys.AccountTerms);
        PrivacyPolicyUrl = _legalReader.GetPrivacyPolicyUrl(LegalDocumentKeys.AccountTerms);
    }

    /// <summary>Shows the update notice with the consent checkbox unchecked and the form pointed at the version required now.</summary>
    private IActionResult TermsUpdated()
    {
        ModelState.Clear();
        ModelState.AddModelError(string.Empty, UpdatedMessage);
        Input.AcceptTerms = false;
        Input.TermsVersion = TermsRequiredVersion;
        return Page();
    }

    private IActionResult RedirectToReturnUrl()
        => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl)
            : LocalRedirect(Url.Content("~/"));
}

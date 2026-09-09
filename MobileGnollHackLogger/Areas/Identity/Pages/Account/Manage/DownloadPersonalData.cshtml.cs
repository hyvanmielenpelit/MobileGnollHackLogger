// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using GnollHackServer.Data.Privacy;

namespace MobileGnollHackLogger.Areas.Identity.Pages.Account.Manage;

public class DownloadPersonalDataModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<DownloadPersonalDataModel> _logger;
    private readonly ApplicationDbContext _dbContext;

    public DownloadPersonalDataModel(
        UserManager<ApplicationUser> userManager,
        ILogger<DownloadPersonalDataModel> logger,
        ApplicationDbContext dbContext)
    {
        _userManager = userManager;
        _logger = logger;
        _dbContext = dbContext;
    }

    public IActionResult OnGet()
    {
        return NotFound();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        _logger.LogInformation("User with ID '{UserId}' asked for their personal data.", _userManager.GetUserId(User));

        // Only include personal data for download
        var personalData = new Dictionary<string, string?>();
        var personalDataProps = typeof(ApplicationUser).GetProperties().Where(
                        prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
        foreach (var p in personalDataProps)
        {
            personalData.Add(p.Name, p.GetValue(user)?.ToString() ?? "null");
        }

        var logins = await _userManager.GetLoginsAsync(user);
        foreach (var l in logins)
        {
            personalData.Add($"{l.LoginProvider} external login provider key", l.ProviderKey);
        }

        personalData.Add($"Authenticator Key", await _userManager.GetAuthenticatorKeyAsync(user));

        /* Erasure has always been complete here; portability was not. Account details were
           downloadable and the conversations -- the part a user would actually ask for -- were
           not, which made the export answer a narrower question than the one being asked.

           No decryptor is passed, and that is forced rather than chosen: the content keyring
           lives in Overseer's User Secrets under its own UserSecretsId, and this application has
           a different one, so it does not hold the key at all. A confidential conversation
           therefore exports with a notice naming where to get its text. The export says so
           itself, in the note below, so a reader of the file is not left to work it out. */
        var conversations = await ChatDataExport.BuildAsync(_dbContext, user.Id);

        await ChatAccessAudit.RecordAsync(
            _dbContext,
            ChatAccessAction.Export,
            actorUserId: user.Id,
            actorUserName: user.UserName,
            subjectUserId: user.Id,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            detail: $"personal data and {conversations.Count} conversations, without content decryption");

        var payload = new Dictionary<string, object?>
        {
            ["account"] = personalData,
            ["conversationsNote"] = ChatDataExport.DescribeExport(canDecrypt: false),
            ["conversations"] = conversations
        };

        Response.Headers.TryAdd("Content-Disposition", "attachment; filename=PersonalData.json");
        return new FileContentResult(
            JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions { WriteIndented = true }),
            "application/json");
    }
}

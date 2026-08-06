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
using System.IO;

namespace MobileGnollHackLogger.Areas.Identity.Pages.Account.Manage;

public class DeletePersonalDataModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<DeletePersonalDataModel> _logger;
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public DeletePersonalDataModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<DeletePersonalDataModel> logger,
        ApplicationDbContext dbContext,
        IConfiguration configuration)
    {
        _userManager = userManager;
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
        var userName = user.UserName;

        // 1. Scrub GameLog rows (preserve Id, CreatedDate, ByteStart, ByteEnd, ByteLength)
        var gameLogs = await _dbContext.GameLog
            .Where(gl => gl.AspNetUserId == userId)
            .ToListAsync();

        foreach (var gl in gameLogs)
        {
            gl.AspNetUserId = null;
            // String fields → null
            gl.Version = null; gl.Platform = null; gl.PlatformVersion = null;
            gl.Port = null; gl.PortVersion = null; gl.PortBuild = null;
            gl.DeathDateText = null; gl.BirthDateText = null;
            gl.Role = null; gl.Race = null; gl.Gender = null; gl.Alignment = null;
            gl.Name = null; gl.CharacterName = null;
            gl.DeathText = null; gl.WhileText = null;
            gl.ConductsBinary = null; gl.AchievementsBinary = null;
            gl.AchievementsText = null; gl.ConductsText = null;
            gl.StartingGender = null; gl.StartingAlignment = null;
            gl.FlagsBinary = null; gl.Mode = null; gl.Scoring = null;
            gl.Tournament = null; gl.Store = null;
            // Int/long fields → 0
            gl.EditLevel = 0; gl.Points = 0;
            gl.DeathDungeonNumber = 0; gl.DeathLevel = 0; gl.MaxLevel = 0;
            gl.HitPoints = 0; gl.MaxHitPoints = 0; gl.Deaths = 0;
            gl.ProcessUserID = 0; gl.Turns = 0;
            gl.RealTime = 0; gl.StartTime = 0; gl.StartTimeUTC = 0;
            gl.EndTime = 0; gl.EndTimeUTC = 0;
            gl.Difficulty = 0; gl.DungeonCollapses = 0;
            // Nullable int fields → null
            gl.SecurityLevel = null; gl.PortSecurityLevel = null;
            gl.ExperienceLevel = null;
        }

        // 2. Delete dumplog files from disk
        var dumpLogBasePath = _configuration["DumpLogPath"];
        if (!string.IsNullOrEmpty(dumpLogBasePath) && !string.IsNullOrEmpty(userName))
        {
            var userDumpDir = Path.Combine(dumpLogBasePath, userName);
            if (Directory.Exists(userDumpDir))
                Directory.Delete(userDumpDir, recursive: true);
        }

        // 3. Delete chat attachment files from disk
        var baseDir = _configuration["ConversationsDataLocation"];
        if (!string.IsNullOrEmpty(baseDir))
        {
            var sessionIds = await _dbContext.ChatSession
                .Where(s => s.AspNetUserId == userId)
                .Select(s => s.Id)
                .ToListAsync();

            foreach (var sid in sessionIds)
            {
                var sessionDir = Path.Combine(baseDir, sid.ToString());
                if (Directory.Exists(sessionDir))
                    Directory.Delete(sessionDir, recursive: true);
            }
        }

        // 4. Delete all chat sessions (cascades to messages, attachments, tool calls)
        _dbContext.ChatSession.RemoveRange(
            _dbContext.ChatSession.Where(s => s.AspNetUserId == userId));

        // 5. Null out DismissedByUserId on system error logs
        await _dbContext.SystemAiErrorLogs
            .Where(e => e.DismissedByUserId == userId)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.DismissedByUserId, (string?)null));

        // 6. Delete bones files from disk and remove bones records
        var bonesRecords = await _dbContext.Bones
            .Where(b => b.AspNetUserId == userId)
            .ToListAsync();
        foreach (var bone in bonesRecords)
        {
            if (!string.IsNullOrEmpty(bone.BonesFilePath) && System.IO.File.Exists(bone.BonesFilePath))
                System.IO.File.Delete(bone.BonesFilePath);
        }
        _dbContext.Bones.RemoveRange(bonesRecords);

        // 7. Delete bones transactions
        _dbContext.BonesTransactions.RemoveRange(
            _dbContext.BonesTransactions.Where(bt => bt.AspNetUserId == userId));

        // 8. Delete save file tracking
        _dbContext.SaveFileTrackings.RemoveRange(
            _dbContext.SaveFileTrackings.Where(s => s.AspNetUserId == userId));

        // 9. Disassociate request logs (preserve logs, remove user links and usernames)
        await _dbContext.RequestLogs
            .Where(r => r.AspNetUserId == userId || r.RequestUserName == userName)
            .ExecuteUpdateAsync(r => r
                .SetProperty(x => x.AspNetUserId, (string?)null)
                .SetProperty(x => x.RequestUserName, (string?)null));

        await _dbContext.SaveChangesAsync();

        var result = await _userManager.DeleteAsync(user);
        userId = await _userManager.GetUserIdAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Unexpected error occurred deleting user.");
        }

        await _signInManager.SignOutAsync();

        _logger.LogInformation("User with ID '{UserId}' deleted themselves.", userId);

        return Redirect("~/");
    }
}

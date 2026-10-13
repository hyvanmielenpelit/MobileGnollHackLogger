using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace GnollHackServer.Data.Privacy;

/// <summary>The two configuration paths an erasure needs; must equal the website's values.</summary>
public sealed record AccountErasurePaths(string? DumpLogPath, string? ConversationsDataLocation);

/// <summary>Files an erasure expected but did not find on this host.</summary>
public sealed record AccountErasureResult(int BonesFilesMissing, int FoldersMissing);

/// <summary>
/// Erases a user's account and data: self-service deletion on the website and an administrator's approved
/// deletion in Overseer run these same steps. Rows that reference the user by foreign key (security events,
/// holds, usage and attempt logs, activity, model order) go with the user by cascade.
/// </summary>
public static class AccountErasure
{
    /// <summary>Scrubs, deletes and finally deletes the user, exactly as self-service deletion always has.</summary>
    /// <remarks>
    /// The bones step deletes absolute paths built from the website's <c>BonesPath</c>; a host that does not
    /// share the website's file system deletes the rows and reports the files it did not find.
    /// </remarks>
    public static async Task<AccountErasureResult> EraseAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        ApplicationUser user, AccountErasurePaths paths, CancellationToken ct = default)
    {
        var userId = user.Id;
        var userName = user.UserName;
        int bonesFilesMissing = 0;
        int foldersMissing = 0;

        // 1. Scrub GameLog rows (preserve Id, CreatedDate, ByteStart, ByteEnd, ByteLength)
        var gameLogs = await db.GameLog
            .Where(gl => gl.AspNetUserId == userId)
            .ToListAsync(ct);

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
        var dumpLogBasePath = paths.DumpLogPath;
        if (!string.IsNullOrEmpty(dumpLogBasePath) && !string.IsNullOrEmpty(userName))
        {
            var userDumpDir = Path.Combine(dumpLogBasePath, userName);
            if (Directory.Exists(userDumpDir))
                Directory.Delete(userDumpDir, recursive: true);
            else if (!Directory.Exists(dumpLogBasePath))
                foldersMissing++;
        }

        // 3. Delete chat attachment files from disk
        var baseDir = paths.ConversationsDataLocation;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var sessionIds = await db.ChatSession
                .Where(s => s.AspNetUserId == userId)
                .Select(s => s.Id)
                .ToListAsync(ct);

            if (!Directory.Exists(baseDir) && sessionIds.Count > 0)
                foldersMissing++;

            foreach (var sid in sessionIds)
            {
                var sessionDir = Path.Combine(baseDir, sid.ToString());
                if (Directory.Exists(sessionDir))
                    Directory.Delete(sessionDir, recursive: true);
            }
        }

        /* 3a. Crypto-shred before removing anything.

               RemoveRange does cascade-delete the row that carries the wrapped content key, so
               shredding happens incidentally on the happy path -- but only on the happy path.
               Doing it explicitly first means a failure between here and the RemoveRange below
               leaves unreadable rows instead of readable ones, and it gives future retention
               work one code path to find rather than two behaviours to reconcile.

               The API-key material needs its own call: a different master key protects it
               (AesEncryptionKey, which does not rotate), so nulling session content keys does
               not reach it. */
        await CryptoShred.NullAllSessionKeysForUserAsync(db, userId);
        await CryptoShred.NullUserApiKeyMaterialAsync(db, userId);
        await CryptoShred.DeleteSystemModelTrustForUserAsync(db, userId);

        // 4. Delete all chat sessions (cascades to messages, attachments, tool calls)
        db.ChatSession.RemoveRange(
            db.ChatSession.Where(s => s.AspNetUserId == userId));

        // 5. Null out DismissedByUserId on system error logs
        await db.SystemAiErrorLogs
            .Where(e => e.DismissedByUserId == userId)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.DismissedByUserId, (string?)null), ct);

        // 6. Delete bones files from disk and remove bones records
        var bonesRecords = await db.Bones
            .Where(b => b.AspNetUserId == userId)
            .ToListAsync(ct);
        foreach (var bone in bonesRecords)
        {
            if (!string.IsNullOrEmpty(bone.BonesFilePath))
            {
                if (File.Exists(bone.BonesFilePath))
                    File.Delete(bone.BonesFilePath);
                else
                    bonesFilesMissing++;
            }
        }
        db.Bones.RemoveRange(bonesRecords);

        // 7. Delete bones transactions
        db.BonesTransactions.RemoveRange(
            db.BonesTransactions.Where(bt => bt.AspNetUserId == userId));

        // 8. Delete save file tracking
        db.SaveFileTrackings.RemoveRange(
            db.SaveFileTrackings.Where(s => s.AspNetUserId == userId));

        // 9. Disassociate request logs (preserve logs, remove user links and usernames)
        await db.RequestLogs
            .Where(r => r.AspNetUserId == userId || r.RequestUserName == userName)
            .ExecuteUpdateAsync(r => r
                .SetProperty(x => x.AspNetUserId, (string?)null)
                .SetProperty(x => x.RequestUserName, (string?)null), ct);

        await db.SaveChangesAsync(ct);

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Unexpected error occurred deleting user.");
        }

        return new AccountErasureResult(bonesFilesMissing, foldersMissing);
    }
}

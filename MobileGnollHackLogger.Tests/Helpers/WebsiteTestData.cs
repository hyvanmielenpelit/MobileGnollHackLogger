using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Tests.Helpers;

/// <summary>Users, game logs, bones and game API forms for the website tests.</summary>
public static class WebsiteTestData
{
    /// <summary>Satisfies Identity's default password rules.</summary>
    public const string Password = "Test-Passw0rd";

    private static int _userCounter;
    private static int _bonesFileCounter;

    /// <summary>
    /// A user name no other test uses: <paramref name="prefix"/>, an underscore and a process-wide counter, matching
    /// <c>^[A-Za-z0-9][A-Za-z0-9_]{0,30}$</c>.
    /// </summary>
    public static string UniqueUserName(string prefix)
    {
        string suffix = "_" + Interlocked.Increment(ref _userCounter).ToString(CultureInfo.InvariantCulture);
        string head = new string(prefix.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').ToArray());
        if (head.Length == 0 || !char.IsAsciiLetterOrDigit(head[0]))
        {
            head = "u" + head;
        }
        if (head.Length + suffix.Length > 31)
        {
            head = head.Substring(0, 31 - suffix.Length);
        }
        return head + suffix;
    }

    /// <summary>A confirmed account with <see cref="Password"/>; <paramref name="configure"/> runs before it is created.</summary>
    public static async Task<ApplicationUser> CreateUserAsync(IServiceProvider services, string userName,
        Action<ApplicationUser>? configure = null)
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = userName + "@example.invalid",
            EmailConfirmed = true
        };
        configure?.Invoke(user);

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var result = await userManager.CreateAsync(user, Password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Creating test user " + userName + " failed: "
                + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
        return user;
    }

    /// <summary>
    /// A tab-separated xlogfile entry as the client posts it. <paramref name="mode"/> null omits the field;
    /// <paramref name="startTime"/> (local) defaults to <paramref name="startTimeUtc"/> + 3600, so the two differ.
    /// </summary>
    public static string XLogEntry(string name, long points, long startTimeUtc, string? mode = "normal", string scoring = "yes",
        int turns = 1234, string death = "killed by a jackal", long? startTime = null)
    {
        long localStart = startTime ?? startTimeUtc + 3600;
        var fields = new List<(string Key, string Value)>
        {
            ("version", "4.1.3"),
            ("edit", "0"),
            ("platform", "windows"),
            ("platformversion", "10.0"),
            ("port", "GnollHack Windows"),
            ("portversion", "4.1.3"),
            ("portbuild", "1"),
            ("points", points.ToString(CultureInfo.InvariantCulture)),
            ("deathdnum", "0"),
            ("deathlev", "3"),
            ("maxlvl", "4"),
            ("hp", "0"),
            ("maxhp", "20"),
            ("deaths", "1"),
            ("deathdate", "20261025"),
            ("birthdate", "20261024"),
            ("uid", "0"),
            ("role", "Val"),
            ("race", "Hum"),
            ("gender", "Fem"),
            ("align", "Neu"),
            ("name", name),
            ("cname", name),
            ("death", death),
            ("conduct", "0x0"),
            ("turns", turns.ToString(CultureInfo.InvariantCulture)),
            ("achieve", "0x0"),
            ("realtime", "600"),
            ("starttime", localStart.ToString(CultureInfo.InvariantCulture)),
            ("starttimeUTC", startTimeUtc.ToString(CultureInfo.InvariantCulture)),
            ("endtime", (localStart + 600).ToString(CultureInfo.InvariantCulture)),
            ("endtimeUTC", (startTimeUtc + 600).ToString(CultureInfo.InvariantCulture)),
            ("gender0", "Fem"),
            ("align0", "Neu"),
            ("flags", "0x0"),
            ("difficulty", "0"),
        };
        if (mode != null)
        {
            fields.Add(("mode", mode));
        }
        fields.Add(("scoring", scoring));
        fields.Add(("tournament", "no"));
        fields.Add(("collapse", "0"));

        return string.Join("\t", fields.Select(f => f.Key + "=" + f.Value));
    }

    /// <summary>Stores <paramref name="entry"/> as a game of <paramref name="user"/>.</summary>
    public static async Task<GameLog> AddGameLogAsync(IServiceProvider services, ApplicationUser user, string entry)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var gameLog = new GameLog(entry)
        {
            AspNetUserId = user.Id,
            CreatedDate = DateTime.UtcNow
        };
        db.GameLog.Add(gameLog);
        await db.SaveChangesAsync();
        return gameLog;
    }

    /// <summary>
    /// A game API form: the credentials, then <paramref name="fields"/>, then <paramref name="files"/>, each file
    /// under its form field name.
    /// </summary>
    public static MultipartFormDataContent GameApiForm(string userName, string password,
        string token = WebsiteWebApplicationFactory.AntiForgeryToken, IDictionary<string, string>? fields = null,
        params (string Field, string FileName, byte[] Content)[] files)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(userName), "UserName" },
            { new StringContent(password), "Password" },
            { new StringContent(token), "AntiForgeryToken" }
        };
        if (fields != null)
        {
            foreach (var (key, value) in fields)
            {
                form.Add(new StringContent(value), key);
            }
        }
        foreach (var (field, fileName, content) in files)
        {
            var fileContent = new ByteArrayContent(content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, field, fileName);
        }
        return form;
    }

    /// <summary>The three fields <c>api/savefiletracking/create</c> requires; <c>Sha256</c> defaults to the Base64 of 32 bytes.</summary>
    public static IDictionary<string, string> SaveFileTrackingFields(long timeStamp = 1, long fileLength = 100, string? sha256 = null)
    {
        return new Dictionary<string, string>
        {
            ["TimeStamp"] = timeStamp.ToString(CultureInfo.InvariantCulture),
            ["Sha256"] = sha256 ?? Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
            ["FileLength"] = fileLength.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// A bones row of <paramref name="owner"/>. Unless <paramref name="filePath"/> is given, the file is
    /// <c>BonesPath\&lt;owner&gt;\&lt;originalFileName&gt;_&lt;n&gt;</c>, written when <paramref name="writeFile"/>.
    /// </summary>
    public static async Task<Bones> AddBonesAsync(IServiceProvider services, ApplicationUser owner, int difficulty, ulong version,
        ulong compatVersion, string? originalFileName = "bon-2D0.5", bool writeFile = true, byte[]? content = null,
        string? filePath = null)
    {
        if (filePath == null)
        {
            string bonesPath = services.GetRequiredService<IConfiguration>()["BonesPath"]
                ?? throw new InvalidOperationException("BonesPath is not configured.");
            string dir = Path.Combine(bonesPath, owner.UserName!);
            Directory.CreateDirectory(dir);
            string baseName = string.IsNullOrEmpty(originalFileName) ? "bones" : Path.GetFileName(originalFileName);
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "bones";
            }
            filePath = Path.Combine(dir, baseName + "_seed" + Interlocked.Increment(ref _bonesFileCounter).ToString(CultureInfo.InvariantCulture));
        }
        if (writeFile)
        {
            await File.WriteAllBytesAsync(filePath, content ?? System.Text.Encoding.ASCII.GetBytes("bones of " + owner.UserName));
        }

        var db = services.GetRequiredService<ApplicationDbContext>();
        var bones = new Bones
        {
            AspNetUserId = owner.Id,
            DifficultyLevel = difficulty,
            BonesFilePath = filePath,
            OriginalFileName = originalFileName,
            Platform = "windows",
            PlatformVersion = "10.0",
            Port = "GnollHack Windows",
            PortVersion = "4.1.3",
            PortBuild = "1",
            VersionNumber = version,
            VersionCompatibilityNumber = compatVersion,
            Created = DateTime.UtcNow
        };
        db.Bones.Add(bones);
        await db.SaveChangesAsync();
        return bones;
    }

    /// <summary>Records that <paramref name="bones"/> was sent to <paramref name="downloader"/>.</summary>
    public static async Task AddDownloadTransactionAsync(IServiceProvider services, ApplicationUser downloader, Bones bones)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        db.BonesTransactions.Add(new BonesTransaction
        {
            AspNetUserId = downloader.Id,
            Date = DateTime.UtcNow,
            Type = TransactionType.Download,
            BonesId = bones.Id,
            DifficultyLevel = bones.DifficultyLevel,
            Platform = bones.Platform,
            PlatformVersion = bones.PlatformVersion,
            Port = bones.Port,
            PortVersion = bones.PortVersion,
            PortBuild = bones.PortBuild,
            VersionNumber = bones.VersionNumber,
            VersionCompatibilityNumber = bones.VersionCompatibilityNumber
        });
        await db.SaveChangesAsync();
    }
}

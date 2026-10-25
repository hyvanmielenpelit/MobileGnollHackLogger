using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Api;

/// <summary>
/// <c>/bones</c>. On the InMemory provider, because the pool queries compare <c>ulong</c> columns. Each test isolates
/// its pool with its own version block (<see cref="NewPoolVersion"/>): every row and request keeps both version numbers
/// inside the block, so no other test's bones are compatible with it. Pools hold 0-3 or 128 eligible files, so the
/// random "send or not" gate, which applies only between 4 and 127, never decides an outcome.
/// </summary>
public class BonesApiTests : IClassFixture<InMemoryWebsiteWebApplicationFactory>
{
    private const string DefaultFileName = "bon-2D0.5";
    private const string NoneApplicable = "too few bones files on server to send a bones file back: 0 applicable bones files on server.";

    /// <summary>The controller's ServerAvailableBoneMaxLimit: at this pool size a file is always sent.</summary>
    private const int FullPool = 128;

    /// <summary>The width of one test's version block; every offset a test uses is smaller.</summary>
    private const ulong PoolBlock = 100_000;

    private static readonly byte[] UploadContent = Encoding.ASCII.GetBytes("uploaded bones file");
    private static int _poolCounter;

    private readonly InMemoryWebsiteWebApplicationFactory _factory;

    public BonesApiTests(InMemoryWebsiteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Post_CredentialsOnly_Returns200TestConnection()
    {
        var user = await CreateUserAsync("bnConn");
        using var client = _factory.CreateGameClient();
        using var form = WebsiteTestData.GameApiForm(user.UserName!, WebsiteTestData.Password);

        var response = await client.PostAsync("/bones", form, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        bool logged = await QueryAsync(db => db.RequestLogs.AnyAsync(r => r.RequestUserName == user.UserName
            && r.Type == LogType.Bones && r.SubType == RequestLogSubType.TestConnection && r.ResponseCode == 200, Ct));
        Assert.True(logged);
    }

    [Fact]
    public async Task SendBonesFile_WithoutFile_Returns500()
    {
        ulong version = NewPoolVersion();
        var user = await CreateUserAsync("bnNoFile");
        using var client = _factory.CreateGameClient();

        var response = await PostSendAsync(client, user, SendFields(2, version, version), fileName: null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Bones file is null when sending a bones file.", await response.Content.ReadAsStringAsync(Ct));
        Assert.False(await QueryAsync(db => db.Bones.AnyAsync(b => b.AspNetUserId == user.Id, Ct)));
    }

    [Fact]
    public async Task SendBonesFile_EmptyPool_StoresFileRowAndUploadTransaction()
    {
        ulong version = NewPoolVersion();
        var user = await CreateUserAsync("bnUpload");
        using var client = _factory.CreateGameClient();
        string userDir = Path.Combine(_factory.BonesPath, user.UserName!);

        var first = await PostSendAsync(client, user, SendFields(2, version, version));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        string firstBody = await first.Content.ReadAsStringAsync(Ct);
        string firstPath = Path.Combine(userDir, DefaultFileName + "_0");
        var firstRow = await QueryAsync(db => db.Bones.SingleAsync(b => b.AspNetUserId == user.Id, Ct));
        Assert.StartsWith(firstRow.Id.ToString(CultureInfo.InvariantCulture) + ", too few bones files", firstBody);
        Assert.Equal(firstPath, firstRow.BonesFilePath);
        Assert.Equal(DefaultFileName, firstRow.OriginalFileName);
        Assert.Equal(2, firstRow.DifficultyLevel);
        Assert.Equal(version, firstRow.VersionNumber);
        Assert.Equal(UploadContent, await File.ReadAllBytesAsync(firstPath, Ct));
        bool uploadRecorded = await QueryAsync(db => db.BonesTransactions.AnyAsync(t => t.Type == TransactionType.Upload
            && t.AspNetUserId == user.Id && t.BonesId == firstRow.Id, Ct));
        Assert.True(uploadRecorded);

        var second = await PostSendAsync(client, user, SendFields(2, version, version));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        string secondPath = Path.Combine(userDir, DefaultFileName + "_1");
        Assert.True(File.Exists(secondPath));
        var rows = await QueryAsync(db => db.Bones.Where(b => b.AspNetUserId == user.Id).ToListAsync(Ct));
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, b => b.BonesFilePath == secondPath);
    }

    [Fact]
    public async Task SendBonesFile_AccountWith32BonesInPool_StoresNothing()
    {
        ulong version = NewPoolVersion();
        var user = await CreateUserAsync("bnFull");
        await SeedPoolAsync(user, 32, 2, version, version);
        using var client = _factory.CreateGameClient();
        string userDir = Path.Combine(_factory.BonesPath, user.UserName!);
        string[] filesBefore = FilesUnder(userDir);

        var response = await PostSendAsync(client, user, SendFields(2, version, version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("0, ", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(filesBefore, FilesUnder(userDir));
        Assert.False(File.Exists(Path.Combine(userDir, DefaultFileName + "_0")));
        Assert.Equal(32, await QueryAsync(db => db.Bones.CountAsync(b => b.AspNetUserId == user.Id, Ct)));
    }

    [Fact]
    public async Task SendBonesFile_PoolOf128OtherBones_SendsOneSeededFile()
    {
        ulong version = NewPoolVersion();
        var owner = await CreateUserAsync("bnOwner");
        var sender = await CreateUserAsync("bnSender");
        var pool = await SeedPoolAsync(owner, FullPool, 2, version, version);
        using var client = _factory.CreateGameClient();

        var response = await PostSendAsync(client, sender, SendFields(2, version, version));

        await AssertSentFromPoolAsync(response, sender, pool);
    }

    [Theory]
    [InlineData(50, 40, 40, 40)] // Older bones at the request's compatibility floor
    [InlineData(50, 50, 50, 70)] // The same version, whatever its compatibility number
    [InlineData(50, 50, 60, 50)] // Newer bones whose compatibility number is the request's version
    public async Task SendBonesFile_CompatibleVersions_SendsAFile(int requestVersion, int requestCompatibility,
        int bonesVersion, int bonesCompatibility)
    {
        ulong pool = NewPoolVersion();
        var owner = await CreateUserAsync("bnCompat");
        var sender = await CreateUserAsync("bnCompatS");
        var rows = await SeedPoolAsync(owner, FullPool, 1, pool + (ulong)bonesVersion, pool + (ulong)bonesCompatibility);
        using var client = _factory.CreateGameClient();

        var response = await PostSendAsync(client, sender,
            SendFields(1, pool + (ulong)requestVersion, pool + (ulong)requestCompatibility));

        await AssertSentFromPoolAsync(response, sender, rows);
    }

    [Fact]
    public async Task SendBonesFile_PoolOfOwnBones_SendsNothing()
    {
        ulong version = NewPoolVersion();
        var sender = await CreateUserAsync("bnOwnPool");
        await SeedPoolAsync(sender, FullPool, 2, version, version);
        using var client = _factory.CreateGameClient();

        var response = await PostSendAsync(client, sender, SendFields(2, version, version));

        await AssertNothingSentAsync(response, sender);
    }

    [Fact]
    public async Task SendBonesFile_PoolOfBannedOwner_SendsNothing()
    {
        ulong version = NewPoolVersion();
        var owner = await CreateUserAsync("bnBanned", u => u.IsBanned = true);
        var sender = await CreateUserAsync("bnBannedS");
        await SeedPoolAsync(owner, FullPool, 2, version, version);
        using var client = _factory.CreateGameClient();

        var response = await PostSendAsync(client, sender, SendFields(2, version, version));

        await AssertNothingSentAsync(response, sender);
    }

    [Fact]
    public async Task SendBonesFile_PoolOfBonesBannedOwner_SendsNothing()
    {
        ulong version = NewPoolVersion();
        var owner = await CreateUserAsync("bnBonesBan", u => u.IsBonesBanned = true);
        var sender = await CreateUserAsync("bnBonesBanS");
        await SeedPoolAsync(owner, FullPool, 2, version, version);
        using var client = _factory.CreateGameClient();

        var response = await PostSendAsync(client, sender, SendFields(2, version, version));

        await AssertNothingSentAsync(response, sender);
    }

    [Fact]
    public async Task SendBonesFile_PoolOfAnotherDifficulty_SendsNothing()
    {
        ulong version = NewPoolVersion();
        var owner = await CreateUserAsync("bnDiff");
        var sender = await CreateUserAsync("bnDiffS");
        await SeedPoolAsync(owner, FullPool, 1, version, version);
        using var client = _factory.CreateGameClient();

        var response = await PostSendAsync(client, sender, SendFields(2, version, version));

        await AssertNothingSentAsync(response, sender);
    }

    [Theory]
    [InlineData(50, 40, 39, 39)] // Older bones just below the request's compatibility floor
    [InlineData(50, 40, 10, 10)] // Older bones far below the floor
    [InlineData(50, 50, 60, 51)] // Newer bones whose compatibility number is just above the request's version
    [InlineData(50, 50, 60, 60)] // Newer bones compatible only with themselves
    public async Task SendBonesFile_IncompatibleVersions_SendsNothing(int requestVersion, int requestCompatibility,
        int bonesVersion, int bonesCompatibility)
    {
        ulong pool = NewPoolVersion();
        var owner = await CreateUserAsync("bnIncompat");
        var sender = await CreateUserAsync("bnIncompatS");
        await SeedPoolAsync(owner, FullPool, 1, pool + (ulong)bonesVersion, pool + (ulong)bonesCompatibility);
        using var client = _factory.CreateGameClient();

        var response = await PostSendAsync(client, sender,
            SendFields(1, pool + (ulong)requestVersion, pool + (ulong)requestCompatibility));

        await AssertNothingSentAsync(response, sender);
    }

    [Fact]
    public async Task SendBonesFile_OwnersOffTheWhitelist_SendsNothing()
    {
        ulong version = NewPoolVersion();
        var owner = await CreateUserAsync("bnWhite");
        var sender = await CreateUserAsync("bnWhiteS");
        var listed = await CreateUserAsync("bnWhiteL");
        await SeedPoolAsync(owner, FullPool, 2, version, version);
        using var client = _factory.CreateGameClient();

        var response = await PostSendAsync(client, sender, SendFields(2, version, version, allowedUsers: listed.UserName));

        await AssertNothingSentAsync(response, sender);
    }

    [Fact]
    public async Task SendBonesFile_OwnersOnTheBlacklist_SendsNothing()
    {
        ulong version = NewPoolVersion();
        var owner = await CreateUserAsync("bnBlack");
        var sender = await CreateUserAsync("bnBlackS");
        await SeedPoolAsync(owner, FullPool, 2, version, version);
        using var client = _factory.CreateGameClient();

        var response = await PostSendAsync(client, sender, SendFields(2, version, version, allowedUsers: "!" + owner.UserName));

        await AssertNothingSentAsync(response, sender);
    }

    [Theory]
    [InlineData("..\\x")]
    [InlineData("")]
    [InlineData(null)]
    public async Task SendBonesFile_PoolWithUnsafeOriginalFileNames_SendsNothing(string? originalFileName)
    {
        ulong version = NewPoolVersion();
        var owner = await CreateUserAsync("bnUnsafe");
        var sender = await CreateUserAsync("bnUnsafeS");
        await SeedPoolAsync(owner, FullPool, 2, version, version, originalFileName);
        using var client = _factory.CreateGameClient();

        var response = await PostSendAsync(client, sender, SendFields(2, version, version));

        await AssertNothingSentAsync(response, sender);
    }

    [Fact]
    public async Task Post_UnknownCommand_Returns500()
    {
        var user = await CreateUserAsync("bnUnknown");
        using var client = _factory.CreateGameClient();
        var fields = new Dictionary<string, string> { ["Command"] = "DeleteEverything", ["Data"] = "1" };

        var response = await PostSendAsync(client, user, fields);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Unknown bones file command.", await response.Content.ReadAsStringAsync(Ct));
        Assert.False(await QueryAsync(db => db.Bones.AnyAsync(b => b.AspNetUserId == user.Id, Ct)));
    }

    [Fact]
    public async Task Post_BonesBannedAccount_Returns423()
    {
        ulong version = NewPoolVersion();
        var user = await CreateUserAsync("bnRefused", u => u.IsBonesBanned = true);
        using var client = _factory.CreateGameClient();

        var response = await PostSendAsync(client, user, SendFields(2, version, version));

        Assert.Equal(HttpStatusCode.Locked, response.StatusCode);
        Assert.Equal("User " + user.UserName + " is not allowed to send bones.", await response.Content.ReadAsStringAsync(Ct));
        Assert.False(await QueryAsync(db => db.Bones.AnyAsync(b => b.AspNetUserId == user.Id, Ct)));
    }

    [Fact]
    public async Task GetById_ExistingId_ReturnsOneLine()
    {
        ulong version = NewPoolVersion();
        var owner = await CreateUserAsync("bnGet");
        var row = await _factory.WithScopeAsync(sp => WebsiteTestData.AddBonesAsync(sp, owner, 2, version, version));
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/bones/" + row.Id.ToString(CultureInfo.InvariantCulture), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Single(body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task GetById_UnknownId_ReturnsEmptyBody()
    {
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/bones/" + int.MaxValue.ToString(CultureInfo.InvariantCulture), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task ConfirmReceipt_AfterDownload_DeletesFileAndRow()
    {
        ulong version = NewPoolVersion();
        var owner = await CreateUserAsync("bnConfOwner");
        var sender = await CreateUserAsync("bnConfSender");
        var pool = await SeedPoolAsync(owner, FullPool, 2, version, version);
        using var client = _factory.CreateGameClient();
        var download = await PostSendAsync(client, sender, SendFields(2, version, version));
        long bonesId = await AssertSentFromPoolAsync(download, sender, pool);
        string path = HeaderValue(download, "X-GH-BonesFilePath");

        var response = await PostConfirmAsync(client, sender, path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("File " + path + " was successfully deleted from the server. Corresponding entry was also deleted from the database.",
            await response.Content.ReadAsStringAsync(Ct));
        Assert.False(File.Exists(path));
        Assert.False(await QueryAsync(db => db.Bones.AnyAsync(b => b.Id == bonesId, Ct)));
        bool deletionRecorded = await QueryAsync(db => db.BonesTransactions.AnyAsync(t => t.Type == TransactionType.Deletion
            && t.AspNetUserId == sender.Id && t.BonesId == bonesId, Ct));
        Assert.True(deletionRecorded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmReceipt_ByPlayerNotSentTheFile_Returns410AndDeletesNothing(bool byUploader)
    {
        ulong version = NewPoolVersion();
        var uploader = await CreateUserAsync("bnNotSentU");
        var stranger = await CreateUserAsync("bnNotSentS");
        using var client = _factory.CreateGameClient();
        var upload = await PostSendAsync(client, uploader, SendFields(2, version, version));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var row = await QueryAsync(db => db.Bones.SingleAsync(b => b.AspNetUserId == uploader.Id, Ct));
        string path = row.BonesFilePath!;
        int transactionsBefore = await CountTransactionsAsync(row.Id);
        var confirmer = byUploader ? uploader : stranger;

        var response = await PostConfirmAsync(client, confirmer, path);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal(NotSentMessage(path, confirmer), await response.Content.ReadAsStringAsync(Ct));
        Assert.True(File.Exists(path));
        Assert.True(await QueryAsync(db => db.Bones.AnyAsync(b => b.Id == row.Id, Ct)));
        Assert.Equal(transactionsBefore, await CountTransactionsAsync(row.Id));
    }

    [Fact]
    public async Task ConfirmReceipt_SentinelFileWithoutRow_Returns410AndKeepsTheFile()
    {
        var user = await CreateUserAsync("bnSentinel");
        string sentinel = await WriteSentinelAsync();
        using var client = _factory.CreateGameClient();

        var response = await PostConfirmAsync(client, user, sentinel);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal(NotSentMessage(sentinel, user), await response.Content.ReadAsStringAsync(Ct));
        Assert.True(File.Exists(sentinel));
    }

    [Fact]
    public async Task ConfirmReceipt_SentRowStoredOutsideBonesPath_RemovesRowAndKeepsTheFile()
    {
        ulong version = NewPoolVersion();
        var owner = await CreateUserAsync("bnOutside");
        var sender = await CreateUserAsync("bnOutsideS");
        string sentinel = Path.Combine(_factory.TempRoot, "sentinel_" + Guid.NewGuid().ToString("N") + ".bin");
        var row = await _factory.WithScopeAsync(async sp =>
        {
            var bones = await WebsiteTestData.AddBonesAsync(sp, owner, 2, version, version, filePath: sentinel);
            await WebsiteTestData.AddDownloadTransactionAsync(sp, sender, bones);
            return bones;
        });
        using var client = _factory.CreateGameClient();

        var response = await PostConfirmAsync(client, sender, sentinel);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("File " + sentinel + " was did not exist on the server and was thus not deleted. However, a corresponding entry to the file was deleted from the database.",
            await response.Content.ReadAsStringAsync(Ct));
        Assert.True(File.Exists(sentinel));
        Assert.False(await QueryAsync(db => db.Bones.AnyAsync(b => b.Id == row.Id, Ct)));
    }

    [Fact]
    public async Task ConfirmReceipt_UnknownPath_Returns410()
    {
        var user = await CreateUserAsync("bnUnknownPath");
        string path = Path.Combine(_factory.BonesPath, "nobody", DefaultFileName + "_0");
        using var client = _factory.CreateGameClient();

        var response = await PostConfirmAsync(client, user, path);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal(NotSentMessage(path, user), await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("..\\escaped.bin")]
    [InlineData("../escaped.bin")]
    [InlineData("sub\\bon.1")]
    [InlineData("C:\\x")]
    [InlineData(".hidden")]
    [InlineData("bon 2D0.5")]
    [InlineData("a123456789b123456789c123456789d123456789e123456789f123456789g1234")] // 65 characters
    public async Task SendBonesFile_UnsafeFileName_Returns400AndWritesNothing(string fileName)
    {
        ulong version = NewPoolVersion();
        var user = await CreateUserAsync("bnBadName");
        using var client = _factory.CreateGameClient();
        string[] filesBefore = FilesUnder(_factory.TempRoot);

        var response = await PostSendAsync(client, user, SendFields(2, version, version), fileName);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Bones file name is invalid.", await response.Content.ReadAsStringAsync(Ct));
        Assert.False(await QueryAsync(db => db.Bones.AnyAsync(b => b.AspNetUserId == user.Id, Ct)));
        Assert.Equal(filesBefore, FilesUnder(_factory.TempRoot));
    }

    [Theory]
    [InlineData("bon-2D0.15")]
    [InlineData("bon3-1QBar.n")]
    [InlineData("bon-0M0.T")]
    public async Task SendBonesFile_GnollHackFileName_IsStored(string fileName)
    {
        ulong version = NewPoolVersion();
        var user = await CreateUserAsync("bnGoodName");
        using var client = _factory.CreateGameClient();
        string expectedPath = Path.Combine(_factory.BonesPath, user.UserName!, fileName + "_0");

        var response = await PostSendAsync(client, user, SendFields(2, version, version), fileName);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await QueryAsync(db => db.Bones.SingleAsync(b => b.AspNetUserId == user.Id, Ct));
        Assert.Equal(fileName, row.OriginalFileName);
        Assert.Equal(expectedPath, row.BonesFilePath);
        Assert.Equal(UploadContent, await File.ReadAllBytesAsync(expectedPath, Ct));
    }

    /// <summary>The first version of a block of <see cref="PoolBlock"/> versions no other test uses.</summary>
    private static ulong NewPoolVersion() => PoolBlock * (ulong)Interlocked.Increment(ref _poolCounter);

    private static Dictionary<string, string> SendFields(int difficulty, ulong version, ulong compatibility, string? allowedUsers = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["Command"] = "SendBonesFile",
            ["Data"] = difficulty.ToString(CultureInfo.InvariantCulture),
            ["VersionNumber"] = version.ToString(CultureInfo.InvariantCulture),
            ["VersionCompatibilityNumber"] = compatibility.ToString(CultureInfo.InvariantCulture),
            ["Platform"] = "windows",
            ["PlatformVersion"] = "10.0",
            ["Port"] = "GnollHack Windows",
            ["PortVersion"] = "4.1.3",
            ["PortBuild"] = "1"
        };
        if (allowedUsers != null)
        {
            fields["AllowedUsers"] = allowedUsers;
        }
        return fields;
    }

    private static string NotSentMessage(string path, ApplicationUser user) =>
        "Bones file " + path + " was not found among the bones files sent to user " + user.UserName + ". Nothing was deleted.";

    private static string HeaderValue(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.GetValues(name));

    private static string[] FilesUnder(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();

    private Task<ApplicationUser> CreateUserAsync(string prefix, Action<ApplicationUser>? configure = null) =>
        _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName(prefix), configure));

    private Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query) =>
        _factory.WithScopeAsync(sp => query(sp.GetRequiredService<ApplicationDbContext>()));

    private Task<int> CountTransactionsAsync(long bonesId) =>
        QueryAsync(db => db.BonesTransactions.CountAsync(t => t.BonesId == bonesId, Ct));

    /// <summary><paramref name="count"/> bones of <paramref name="owner"/>, each with its own file and content.</summary>
    private Task<List<Bones>> SeedPoolAsync(ApplicationUser owner, int count, int difficulty, ulong version, ulong compatibility,
        string? originalFileName = DefaultFileName)
    {
        return _factory.WithScopeAsync(async sp =>
        {
            var rows = new List<Bones>(count);
            for (int i = 0; i < count; i++)
            {
                byte[] content = Encoding.ASCII.GetBytes("bones " + i.ToString(CultureInfo.InvariantCulture) + " of " + owner.UserName);
                rows.Add(await WebsiteTestData.AddBonesAsync(sp, owner, difficulty, version, compatibility, originalFileName,
                    content: content));
            }
            return rows;
        });
    }

    /// <summary>A file in the temporary root, outside the bones store.</summary>
    private async Task<string> WriteSentinelAsync()
    {
        string sentinel = Path.Combine(_factory.TempRoot, "sentinel_" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(sentinel, Encoding.ASCII.GetBytes("sentinel"), Ct);
        return sentinel;
    }

    /// <summary>A bones request; <paramref name="fileName"/> null sends no file.</summary>
    private static async Task<HttpResponseMessage> PostSendAsync(HttpClient client, ApplicationUser user,
        IDictionary<string, string> fields, string? fileName = DefaultFileName)
    {
        var files = fileName == null
            ? Array.Empty<(string Field, string FileName, byte[] Content)>()
            : new[] { ("BonesFile", fileName, UploadContent) };
        using var form = WebsiteTestData.GameApiForm(user.UserName!, WebsiteTestData.Password, fields: fields, files: files);
        return await client.PostAsync("/bones", form, Ct);
    }

    private static async Task<HttpResponseMessage> PostConfirmAsync(HttpClient client, ApplicationUser user, string path)
    {
        var fields = new Dictionary<string, string> { ["Command"] = "ConfirmReceipt", ["Data"] = path };
        using var form = WebsiteTestData.GameApiForm(user.UserName!, WebsiteTestData.Password, fields: fields);
        return await client.PostAsync("/bones", form, Ct);
    }

    /// <summary>The response is one row of <paramref name="pool"/> with its file, and its download is recorded; returns the row's id.</summary>
    private async Task<long> AssertSentFromPoolAsync(HttpResponseMessage response, ApplicationUser sender, IReadOnlyList<Bones> pool)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        long id = long.Parse(HeaderValue(response, "X-GH-DataBaseTableId"), CultureInfo.InvariantCulture);
        var sent = Assert.Single(pool, b => b.Id == id);
        Assert.Equal(sent.OriginalFileName, HeaderValue(response, "X-GH-OriginalFileName"));
        Assert.Equal(sent.BonesFilePath, HeaderValue(response, "X-GH-BonesFilePath"));
        Assert.Equal(await File.ReadAllBytesAsync(sent.BonesFilePath!, Ct), await response.Content.ReadAsByteArrayAsync(Ct));
        bool downloadRecorded = await QueryAsync(db => db.BonesTransactions.AnyAsync(t => t.Type == TransactionType.Download
            && t.AspNetUserId == sender.Id && t.BonesId == id, Ct));
        Assert.True(downloadRecorded);
        return id;
    }

    /// <summary>The "too few" answer with no applicable file, and no download recorded for <paramref name="sender"/>.</summary>
    private async Task AssertNothingSentAsync(HttpResponseMessage response, ApplicationUser sender)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(NoneApplicable, await response.Content.ReadAsStringAsync(Ct));
        bool downloaded = await QueryAsync(db => db.BonesTransactions.AnyAsync(t => t.Type == TransactionType.Download
            && t.AspNetUserId == sender.Id, Ct));
        Assert.False(downloaded);
    }
}

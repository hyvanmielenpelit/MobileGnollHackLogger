using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Api;

/// <summary>
/// <c>api/savefiletracking/create</c> and <c>use</c>. The encrypted id is AES-256-CBC with PKCS7 padding over the
/// UTF-8 decimal id, Base64 encoded, with the factory's key and IV, as the controller makes it.
/// </summary>
public class SaveFileTrackingApiTests : IClassFixture<WebsiteWebApplicationFactory>
{
    private const string CreateRoute = "/api/savefiletracking/create";
    private const string UseRoute = "/api/savefiletracking/use";

    private static readonly string DefaultSha256 = WebsiteTestData.SaveFileTrackingFields()["Sha256"];
    private static readonly string OtherSha256 = Convert.ToBase64String(Enumerable.Range(101, 32).Select(i => (byte)i).ToArray());

    private readonly WebsiteWebApplicationFactory _factory;

    public SaveFileTrackingApiTests(WebsiteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_ReturnsEncryptedIdOfTheNewRow()
    {
        var user = await CreateUserAsync(_factory, "sftCreate");
        using var client = _factory.CreateGameClient();

        var response = await PostAsync(client, CreateRoute, user, WebsiteTestData.SaveFileTrackingFields(1234, 5678, DefaultSha256));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        long id = DecryptId(await response.Content.ReadAsStringAsync(Ct));
        var row = await QueryAsync(db => db.SaveFileTrackings.SingleAsync(t => t.Id == id, Ct));
        Assert.Equal(1234L, row.TimeStamp);
        Assert.Equal(DefaultSha256, row.Sha256);
        Assert.Equal(5678L, row.FileLength);
        Assert.Equal(user.Id, row.AspNetUserId);
        Assert.Null(row.UsedDate);
    }

    [Fact]
    public async Task Create_SameTimeStampForSameAccount_Returns409()
    {
        var user = await CreateUserAsync(_factory, "sftDup");
        using var client = _factory.CreateGameClient();
        var first = await PostAsync(client, CreateRoute, user, WebsiteTestData.SaveFileTrackingFields(77));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PostAsync(client, CreateRoute, user, WebsiteTestData.SaveFileTrackingFields(77));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal($"User '{user.UserName}' already has Save File Tracking entry with Time Stamp 77.",
            await second.Content.ReadAsStringAsync(Ct));
        Assert.Equal(1, await QueryAsync(db => db.SaveFileTrackings.CountAsync(t => t.AspNetUserId == user.Id, Ct)));
    }

    [Fact]
    public async Task Create_SameTimeStampForAnotherAccount_Returns200()
    {
        var first = await CreateUserAsync(_factory, "sftShareA");
        var second = await CreateUserAsync(_factory, "sftShareB");
        using var client = _factory.CreateGameClient();
        var firstResponse = await PostAsync(client, CreateRoute, first, WebsiteTestData.SaveFileTrackingFields(88));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var response = await PostAsync(client, CreateRoute, second, WebsiteTestData.SaveFileTrackingFields(88));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        long id = DecryptId(await response.Content.ReadAsStringAsync(Ct));
        var row = await QueryAsync(db => db.SaveFileTrackings.SingleAsync(t => t.Id == id, Ct));
        Assert.Equal(second.Id, row.AspNetUserId);
        Assert.Equal(88L, row.TimeStamp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_Sha256EmptyOrMissing_Returns400FromModelValidation(bool omitField)
    {
        var user = await CreateUserAsync(_factory, "sftNoSha");
        using var client = _factory.CreateGameClient();
        var fields = WebsiteTestData.SaveFileTrackingFields(sha256: "");
        if (omitField)
        {
            fields.Remove("Sha256");
        }
        long failuresBefore = await ModelStateFailureCountAsync(CreateRoute);

        var response = await PostAsync(client, CreateRoute, user, fields);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(failuresBefore + 1, await ModelStateFailureCountAsync(CreateRoute));
        Assert.False(await QueryAsync(db => db.SaveFileTrackings.AnyAsync(t => t.AspNetUserId == user.Id, Ct)));
    }

    [Fact]
    public async Task Create_Sha256NotBase64_Returns400()
    {
        var user = await CreateUserAsync(_factory, "sftBadSha");
        using var client = _factory.CreateGameClient();

        var response = await PostAsync(client, CreateRoute, user, WebsiteTestData.SaveFileTrackingFields(sha256: "not base64!"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Error parsing Sha256 from Base64 string.", await response.Content.ReadAsStringAsync(Ct));
        Assert.False(await QueryAsync(db => db.SaveFileTrackings.AnyAsync(t => t.AspNetUserId == user.Id, Ct)));
    }

    [Fact]
    public async Task Create_Sha256Of16Bytes_Returns400()
    {
        var user = await CreateUserAsync(_factory, "sftShortSha");
        using var client = _factory.CreateGameClient();
        string sixteenBytes = Convert.ToBase64String(Enumerable.Range(1, 16).Select(i => (byte)i).ToArray());

        var response = await PostAsync(client, CreateRoute, user, WebsiteTestData.SaveFileTrackingFields(sha256: sixteenBytes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Sha256 is not 32 bytes long.", await response.Content.ReadAsStringAsync(Ct));
        Assert.False(await QueryAsync(db => db.SaveFileTrackings.AnyAsync(t => t.AspNetUserId == user.Id, Ct)));
    }

    [Fact]
    public async Task Create_FileLengthZero_Returns400()
    {
        var user = await CreateUserAsync(_factory, "sftNoLength");
        using var client = _factory.CreateGameClient();

        var response = await PostAsync(client, CreateRoute, user, WebsiteTestData.SaveFileTrackingFields(fileLength: 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("File Length 0 must be positive.", await response.Content.ReadAsStringAsync(Ct));
        Assert.False(await QueryAsync(db => db.SaveFileTrackings.AnyAsync(t => t.AspNetUserId == user.Id, Ct)));
    }

    [Fact]
    public async Task Use_MatchingValues_Returns200AndMarksUsed_ThenSecondUseReturns409()
    {
        var user = await CreateUserAsync(_factory, "sftUse");
        using var client = _factory.CreateGameClient();
        string encryptedId = await CreateTrackingAsync(client, user, 500, 600, DefaultSha256);

        var response = await PostAsync(client, UseRoute, user, UseFields(encryptedId, 500, 600, DefaultSha256));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(Ct));
        long id = DecryptId(encryptedId);
        Assert.NotNull(await UsedDateAsync(id));

        var second = await PostAsync(client, UseRoute, user, UseFields(encryptedId, 500, 600, DefaultSha256));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("SaveFileTracking already used.", await second.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Use_WrongTimeStamp_Returns403()
    {
        var user = await CreateUserAsync(_factory, "sftWrongTs");
        using var client = _factory.CreateGameClient();
        string encryptedId = await CreateTrackingAsync(client, user, 500, 600, DefaultSha256);

        var response = await PostAsync(client, UseRoute, user, UseFields(encryptedId, 501, 600, DefaultSha256));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Time Stamp '501' is not correct.", await response.Content.ReadAsStringAsync(Ct));
        Assert.Null(await UsedDateAsync(DecryptId(encryptedId)));
    }

    [Fact]
    public async Task Use_WrongFileLength_Returns403()
    {
        var user = await CreateUserAsync(_factory, "sftWrongLen");
        using var client = _factory.CreateGameClient();
        string encryptedId = await CreateTrackingAsync(client, user, 500, 600, DefaultSha256);

        var response = await PostAsync(client, UseRoute, user, UseFields(encryptedId, 500, 601, DefaultSha256));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("File lengths do not match.", await response.Content.ReadAsStringAsync(Ct));
        Assert.Null(await UsedDateAsync(DecryptId(encryptedId)));
    }

    [Fact]
    public async Task Use_WrongSha256_Returns403()
    {
        var user = await CreateUserAsync(_factory, "sftWrongSha");
        using var client = _factory.CreateGameClient();
        string encryptedId = await CreateTrackingAsync(client, user, 500, 600, DefaultSha256);

        var response = await PostAsync(client, UseRoute, user, UseFields(encryptedId, 500, 600, OtherSha256));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Sha256 hashes do not match.", await response.Content.ReadAsStringAsync(Ct));
        Assert.Null(await UsedDateAsync(DecryptId(encryptedId)));
    }

    [Fact]
    public async Task Use_AnotherAccountsTracking_Returns403()
    {
        var owner = await CreateUserAsync(_factory, "sftOwner");
        var other = await CreateUserAsync(_factory, "sftOther");
        using var client = _factory.CreateGameClient();
        string encryptedId = await CreateTrackingAsync(client, owner, 700, 800, OtherSha256);

        var response = await PostAsync(client, UseRoute, other, UseFields(encryptedId, 700, 800, OtherSha256));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("User names do not match.", await response.Content.ReadAsStringAsync(Ct));
        Assert.Null(await UsedDateAsync(DecryptId(encryptedId)));
    }

    [Fact]
    public async Task Use_UnknownId_Returns410()
    {
        var user = await CreateUserAsync(_factory, "sftUnknown");
        using var client = _factory.CreateGameClient();

        var response = await PostAsync(client, UseRoute, user, UseFields(EncryptId(long.MaxValue), 500, 600, DefaultSha256));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("SaveFileTracking not found.", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Use_TimeStampZero_Returns400()
    {
        var user = await CreateUserAsync(_factory, "sftTsZero");
        using var client = _factory.CreateGameClient();
        string encryptedId = await CreateTrackingAsync(client, user, 500, 600, DefaultSha256);

        var response = await PostAsync(client, UseRoute, user, UseFields(encryptedId, 0, 600, DefaultSha256));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Model TimeStamp is not greater than 0.", await response.Content.ReadAsStringAsync(Ct));
        Assert.Null(await UsedDateAsync(DecryptId(encryptedId)));
    }

    [Fact]
    public async Task Use_FileLengthZero_Returns400()
    {
        var user = await CreateUserAsync(_factory, "sftLenZero");
        using var client = _factory.CreateGameClient();
        string encryptedId = await CreateTrackingAsync(client, user, 500, 600, DefaultSha256);

        var response = await PostAsync(client, UseRoute, user, UseFields(encryptedId, 500, 0, DefaultSha256));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Model FileLength 0 is not positive.", await response.Content.ReadAsStringAsync(Ct));
        Assert.Null(await UsedDateAsync(DecryptId(encryptedId)));
    }

    [Theory]
    [InlineData("not base64!")] // Not Base64 at all
    [InlineData("AQIDBAU=")] // Five bytes: not a whole AES block
    public async Task Use_EncryptedIdNotValidCiphertext_Returns500(string encryptedId)
    {
        var user = await CreateUserAsync(_factory, "sftBadCipher");
        using var client = _factory.CreateGameClient();

        var response = await PostAsync(client, UseRoute, user, UseFields(encryptedId, 500, 600, DefaultSha256));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Decryption failed. Exception", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Use_EmptySha256_Returns400FromModelValidation()
    {
        var user = await CreateUserAsync(_factory, "sftUseNoSha");
        using var client = _factory.CreateGameClient();
        string encryptedId = await CreateTrackingAsync(client, user, 500, 600, DefaultSha256);
        long failuresBefore = await ModelStateFailureCountAsync(UseRoute);

        var response = await PostAsync(client, UseRoute, user, UseFields(encryptedId, 500, 600, ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(failuresBefore + 1, await ModelStateFailureCountAsync(UseRoute));
        Assert.Null(await UsedDateAsync(DecryptId(encryptedId)));
    }

    [Fact]
    public async Task Use_BlankEncryptionKey_Returns500()
    {
        await using var factory = new BlankEncryptionKeyFactory();
        var user = await CreateUserAsync(factory, "sftNoKey");
        using var client = factory.CreateGameClient();

        var response = await PostAsync(client, UseRoute, user, UseFields(EncryptId(1), 500, 600, DefaultSha256));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Server Error. Encryption key string not found.", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Use_EncryptionKeyOf31Bytes_Returns500()
    {
        await using var factory = new ShortEncryptionKeyFactory();
        var user = await CreateUserAsync(factory, "sftShortKey");
        using var client = factory.CreateGameClient();

        var response = await PostAsync(client, UseRoute, user, UseFields(EncryptId(1), 500, 600, DefaultSha256));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Server Error. Encryption key is of wrong size.", await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>A host whose <c>EncryptionKeyString</c> is blank.</summary>
    public class BlankEncryptionKeyFactory : WebsiteWebApplicationFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["EncryptionKeyString"] = "";
                return settings;
            }
        }
    }

    /// <summary>A host whose <c>EncryptionKeyString</c> is 31 bytes, one short of AES-256.</summary>
    public class ShortEncryptionKeyFactory : WebsiteWebApplicationFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["EncryptionKeyString"] = "0123456789abcdef0123456789abcde";
                return settings;
            }
        }
    }

    private static Aes CreateAes()
    {
        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = Encoding.UTF8.GetBytes(WebsiteWebApplicationFactory.EncryptionKeyString);
        aes.BlockSize = 128;
        aes.IV = Encoding.UTF8.GetBytes(WebsiteWebApplicationFactory.EncryptionIVString);
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes;
    }

    private static string EncryptId(long id)
    {
        using var aes = CreateAes();
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(aes.Key, aes.IV), CryptoStreamMode.Write))
        using (var writer = new StreamWriter(cs, new UTF8Encoding(false)))
        {
            writer.Write(id.ToString(CultureInfo.InvariantCulture));
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    private static long DecryptId(string base64)
    {
        using var aes = CreateAes();
        using var ms = new MemoryStream(Convert.FromBase64String(base64));
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(aes.Key, aes.IV), CryptoStreamMode.Read);
        using var reader = new StreamReader(cs, Encoding.UTF8);
        return long.Parse(reader.ReadToEnd(), CultureInfo.InvariantCulture);
    }

    private static IDictionary<string, string> UseFields(string encryptedId, long timeStamp, long fileLength, string sha256)
    {
        var fields = WebsiteTestData.SaveFileTrackingFields(timeStamp, fileLength, sha256);
        fields["EncryptedId"] = encryptedId;
        return fields;
    }

    private static Task<ApplicationUser> CreateUserAsync(WebsiteWebApplicationFactory factory, string prefix) =>
        factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName(prefix)));

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route, ApplicationUser user,
        IDictionary<string, string> fields)
    {
        using var form = WebsiteTestData.GameApiForm(user.UserName!, WebsiteTestData.Password, fields: fields);
        return await client.PostAsync(route, form, Ct);
    }

    /// <summary>Creates a tracking through the API and returns its encrypted id.</summary>
    private static async Task<string> CreateTrackingAsync(HttpClient client, ApplicationUser user, long timeStamp, long fileLength,
        string sha256)
    {
        var response = await PostAsync(client, CreateRoute, user, WebsiteTestData.SaveFileTrackingFields(timeStamp, fileLength, sha256));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query) =>
        _factory.WithScopeAsync(sp => query(sp.GetRequiredService<ApplicationDbContext>()));

    private Task<DateTime?> UsedDateAsync(long id) =>
        QueryAsync(db => db.SaveFileTrackings.Where(t => t.Id == id).Select(t => t.UsedDate).SingleAsync(Ct));

    /// <summary>
    /// How many Sha256 model-validation failures were logged for <paramref name="route"/>. The request log merges
    /// identical entries into one row with a count, so the total of the counts is compared.
    /// </summary>
    private async Task<long> ModelStateFailureCountAsync(string route)
    {
        var rows = await QueryAsync(db => db.RequestLogs
            .Where(r => r.Type == LogType.Other && r.SubType == RequestLogSubType.ModelStateFailed)
            .ToListAsync(Ct));
        return rows
            .Where(r => r.RequestPath != null && r.RequestPath.EndsWith(route, StringComparison.OrdinalIgnoreCase)
                && r.Message != null && r.Message.Contains("Sha256", StringComparison.Ordinal))
            .Sum(r => r.Count);
    }
}

using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Api;

/// <summary>
/// Game API forms that fail model validation: a 400 with the validation errors, written by Program's
/// <c>InvalidModelStateResponseFactory</c> as a <see cref="RequestLogSubType.ModelStateFailed"/> request-log row.
/// The row holds no user name, so each request carries its own address and the row is found by it.
/// </summary>
public class GameApiModelValidationTests : IClassFixture<WebsiteWebApplicationFactory>
{
    private static int _ipCounter;

    private readonly WebsiteWebApplicationFactory _factory;

    public GameApiModelValidationTests(WebsiteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task XLogFile_WithoutPassword_Returns400AndLogsModelStateFailed()
    {
        using var client = _factory.CreateGameClient();
        string ip = NextIp();
        var form = new MultipartFormDataContent
        {
            { new StringContent(WebsiteTestData.UniqueUserName("mv_xlog")), "UserName" },
            { new StringContent(WebsiteWebApplicationFactory.AntiForgeryToken), "AntiForgeryToken" }
        };

        var response = await PostAsync(client, "/xlogfile", form, ip);

        await AssertValidationFailureAsync(response, "Password");
        var row = await ModelStateFailedRowAsync(ip);
        Assert.Equal(LogType.GameLog, row.Type);
        Assert.Equal(400, row.ResponseCode);
        Assert.Contains("Password", row.Message);
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public async Task Bones_InvalidUserName_Returns400AndLogsModelStateFailed(string userName)
    {
        using var client = _factory.CreateGameClient();
        string ip = NextIp();

        var response = await PostAsync(client, "/bones", WebsiteTestData.GameApiForm(userName, WebsiteTestData.Password), ip);

        await AssertValidationFailureAsync(response, "UserName");
        var row = await ModelStateFailedRowAsync(ip);
        Assert.Equal(LogType.Bones, row.Type);
        Assert.Equal(400, row.ResponseCode);
        Assert.Contains("UserName", row.Message);
    }

    [Fact]
    public async Task Bones_AllowedUsersWithSemicolon_Returns400()
    {
        using var client = _factory.CreateGameClient();
        string ip = NextIp();
        var form = WebsiteTestData.GameApiForm(WebsiteTestData.UniqueUserName("mv_allowed"), WebsiteTestData.Password,
            fields: new Dictionary<string, string> { ["AllowedUsers"] = "a;b" });

        var response = await PostAsync(client, "/bones", form, ip);

        await AssertValidationFailureAsync(response, "AllowedUsers");
        var row = await ModelStateFailedRowAsync(ip);
        Assert.Equal(LogType.Bones, row.Type);
        Assert.Equal(400, row.ResponseCode);
    }

    [Fact]
    public async Task SaveFileTrackingUse_WithoutEncryptedId_Returns400AndLogsTypeOther()
    {
        using var client = _factory.CreateGameClient();
        string ip = NextIp();
        var form = WebsiteTestData.GameApiForm(WebsiteTestData.UniqueUserName("mv_sftuse"), WebsiteTestData.Password,
            fields: WebsiteTestData.SaveFileTrackingFields());

        var response = await PostAsync(client, "/api/savefiletracking/use", form, ip);

        await AssertValidationFailureAsync(response, "EncryptedId");
        var row = await ModelStateFailedRowAsync(ip);
        Assert.Equal(LogType.Other, row.Type);
        Assert.Equal(400, row.ResponseCode);
        Assert.Contains("EncryptedId", row.Message);
    }

    /// <summary>A documentation address (2001:db8::/32) no other test uses, in the form the server prints it.</summary>
    private static string NextIp() =>
        IPAddress.Parse("2001:db8:a2::" + Interlocked.Increment(ref _ipCounter).ToString("x", CultureInfo.InvariantCulture)).ToString();

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, HttpContent content, string ip)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Add(WebsiteWebApplicationFactory.RemoteIpHeader, ip);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>A 400 whose body is the model-state errors, keyed by the failing field.</summary>
    private static async Task AssertValidationFailureAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"" + field + "\"", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<RequestInfo> ModelStateFailedRowAsync(string ip)
    {
        var rows = await _factory.WithScopeAsync(sp =>
            sp.GetRequiredService<ApplicationDbContext>().RequestLogs.AsNoTracking()
                .Where(r => r.UserIPAddress == ip && r.SubType == RequestLogSubType.ModelStateFailed)
                .ToListAsync(TestContext.Current.CancellationToken));
        var row = Assert.Single(rows);
        Assert.StartsWith("ModelState Failed: ", row.Message);
        return row;
    }
}

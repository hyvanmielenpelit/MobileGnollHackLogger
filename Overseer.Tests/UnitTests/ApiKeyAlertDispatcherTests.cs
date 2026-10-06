using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.ApiKeyAlerts;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ApiKeyAlertDispatcherTests
{
    private const string KeyA = "test-key-" + "0123456789abcdefWXYZ";
    private const string KeyB = "test-key-" + "fedcba9876543210QRST";
    private const string Recipient = "alerts@example.test";
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private const string OpenAiKeyRejectedBody =
        """{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error","param":null,"code":"invalid_api_key"}}""";

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string Email, string Subject, string Html)> Sent { get; } = new();
        public bool Fail { get; set; }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (Fail) throw new InvalidOperationException("The mail server is unavailable.");
            Sent.Add((email, subject, htmlMessage));
            return Task.CompletedTask;
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Overseer.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Harness : IDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required CryptoService Crypto { get; init; }
        public required RecordingEmailSender Sender { get; init; }
        public required ApiKeyAlertService Service { get; init; }
        public required ApiKeyAlertDispatcher Dispatcher { get; init; }

        public void Dispose() => Provider.Dispose();
    }

    private static Harness CreateHarness(bool enabled = true)
    {
        string dbName = Guid.NewGuid().ToString();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()) }
            })
            .Build();
        var crypto = new CryptoService(configuration);
        var sender = new RecordingEmailSender();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton(crypto);
        services.AddSingleton<IEmailSender>(sender);
        var provider = services.BuildServiceProvider();

        var options = new ApiKeyAlertOptions
        {
            Enabled = enabled,
            RecipientEmail = Recipient,
            ThrottleHours = 6,
            MaxResponseBodyChars = 4000
        };
        var service = new ApiKeyAlertService(options, NullLogger<ApiKeyAlertService>.Instance);
        var dispatcher = new ApiKeyAlertDispatcher(
            service,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TestHostEnvironment(),
            NullLogger<ApiKeyAlertDispatcher>.Instance,
            timeProvider: null);

        return new Harness { Provider = provider, Crypto = crypto, Sender = sender, Service = service, Dispatcher = dispatcher };
    }

    private static async Task<long> AddConfigAsync(
        Harness h, string displayName, string apiKey, string provider = "OpenAI", string modelId = "gpt-test-model", bool useDefault = false)
    {
        var (ciphertext, nonce, tag) = h.Crypto.Encrypt(apiKey, ApiKeyAlertDispatcher.SystemApiKeyAssociatedData);
        using var scope = h.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var config = new SystemAiApiConfiguration
        {
            DisplayName = displayName,
            Provider = provider,
            ModelId = modelId,
            IsEnabled = true,
            UseDefaultApiKey = useDefault,
            EncryptedApiKey = ciphertext,
            ApiKeyNonce = nonce,
            ApiKeyTag = tag
        };
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return config.Id;
    }

    private static async Task AddDefaultKeyAsync(Harness h, string provider, string apiKey)
    {
        using var scope = h.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.SystemDefaultApiKeys.Add(new SystemDefaultApiKey { Provider = provider, KeyHint = apiKey[^4..] });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> AddSessionAsync(Harness h, string title)
    {
        using var scope = h.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = new ChatSession
        {
            AspNetUserId = "user-1",
            Title = title,
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return session.Id;
    }

    private static async Task<List<ApiKeyFailureAlertState>> StatesAsync(Harness h)
    {
        using var scope = h.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ApiKeyFailureAlertStates.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
    }

    private static ApiKeyFailureReport Report(
        long configId,
        string apiKey,
        DateTime occurredUtc,
        string provider = "OpenAI",
        ApiKeyFailureKind kind = ApiKeyFailureKind.KeyRejected,
        ApiKeyUsage usage = ApiKeyUsage.Chat,
        SessionRef? session = null,
        string? body = OpenAiKeyRejectedBody,
        string? requestUri = "https://api.openai.com/v1/responses",
        IEnumerable<KeyValuePair<string, IEnumerable<string>>>? headers = null) =>
        ApiKeyFailureReport.Create(
            kind,
            provider,
            configId,
            apiKey,
            new ApiKeyAlertContext(usage, session ?? SessionRef.NewEphemeral(), "user-1", "TestUser"),
            occurredUtc,
            httpStatus: 401,
            httpReason: "Unauthorized",
            body,
            requestUri,
            headers,
            elapsedMs: 1234,
            attempt: 1,
            modelId: "gpt-test-model",
            serviceTier: null);

    /// <summary>The decoded value cell of the first table row with this label.</summary>
    private static string? CellValue(string html, string label)
    {
        var match = Regex.Match(html, Regex.Escape(label) + "</th><td[^>]*>([^<]*)</td>");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static void AssertNoKeyRun(string text, string apiKey)
    {
        Assert.DoesNotContain(apiKey, text, StringComparison.Ordinal);
        string lowerText = text.ToLowerInvariant();
        for (int i = 0; i + 8 <= apiKey.Length; i++)
        {
            string run = apiKey.Substring(i, 8);
            Assert.DoesNotContain(run, text, StringComparison.Ordinal);
            Assert.DoesNotContain(run.ToLowerInvariant(), lowerText, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> ReportStrings(ApiKeyFailureReport report)
    {
        foreach (var property in typeof(ApiKeyFailureReport).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            switch (property.GetValue(report))
            {
                case null:
                    break;
                case string s:
                    yield return s;
                    break;
                case IEnumerable<KeyValuePair<string, string>> pairs:
                    foreach (var pair in pairs)
                    {
                        yield return pair.Key;
                        yield return pair.Value;
                    }
                    break;
                case ApiKeyAlertContext context:
                    foreach (var contextProperty in typeof(ApiKeyAlertContext).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (contextProperty.GetValue(context)?.ToString() is string text) yield return text;
                    }
                    break;
                case object other:
                    if (other.ToString() is string otherText) yield return otherText;
                    break;
            }
        }
    }

    /// <summary>A key-rejected report whose body, request URL and headers all echo the key.</summary>
    private static ApiKeyFailureReport KeyEchoingReport(long configId, string apiKey) =>
        Report(
            configId,
            apiKey,
            DateTime.UtcNow,
            body: """{"error":{"message":"Incorrect API key provided: """ + apiKey + """.","type":"invalid_request_error","code":"invalid_api_key"}}""",
            requestUri: "https://generativelanguage.googleapis.com/v1beta/models/gemini-x:streamGenerateContent?alt=sse&key=" + apiKey,
            headers: new List<KeyValuePair<string, IEnumerable<string>>>
            {
                new("request-id", new[] { "req-" + apiKey }),
                new("x-goog-api-key", new[] { apiKey })
            });

    [Fact]
    public async Task RepeatedFailures_OfOneKey_AreThrottled_AndCounted()
    {
        var ct = TestContext.Current.CancellationToken;
        using var h = CreateHarness();
        long id = await AddConfigAsync(h, "Throttle Config", KeyA);
        var now = DateTime.UtcNow;

        Assert.Equal(ApiKeyAlertOutcome.Sent, await h.Dispatcher.ProcessAsync(Report(id, KeyA, now), ct));
        var first = Assert.Single(h.Sender.Sent);
        Assert.Equal(Recipient, first.Email);

        Assert.Equal(ApiKeyAlertOutcome.Suppressed, await h.Dispatcher.ProcessAsync(Report(id, KeyA, now.AddHours(1)), ct));
        Assert.Single(h.Sender.Sent);
        var state = Assert.Single(await StatesAsync(h));
        Assert.Equal(1, state.OccurrencesSinceLastEmail);
        Assert.NotNull(state.LastEmailSentUtc);

        Assert.Equal(ApiKeyAlertOutcome.Sent, await h.Dispatcher.ProcessAsync(Report(id, KeyA, now.AddHours(7)), ct));
        Assert.Equal(2, h.Sender.Sent.Count);
        Assert.Contains("1 further occurrence since the previous email", h.Sender.Sent[1].Html);
        state = Assert.Single(await StatesAsync(h));
        Assert.Equal(0, state.OccurrencesSinceLastEmail);
    }

    [Fact]
    public async Task ConfigsSharingADefaultKey_AreThrottledTogether_AndAllListed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var h = CreateHarness();
        await AddDefaultKeyAsync(h, "OpenAI", KeyA);
        long idA = await AddConfigAsync(h, "Shared Alpha", KeyA, useDefault: true);
        long idB = await AddConfigAsync(h, "Shared Beta", KeyA, modelId: "gpt-test-mini", useDefault: true);
        await AddConfigAsync(h, "Other Gamma", KeyB);
        var now = DateTime.UtcNow;

        Assert.Equal(ApiKeyAlertOutcome.Sent, await h.Dispatcher.ProcessAsync(Report(idA, KeyA, now), ct));
        Assert.Equal(ApiKeyAlertOutcome.Suppressed, await h.Dispatcher.ProcessAsync(Report(idB, KeyA, now.AddMinutes(5)), ct));

        var email = Assert.Single(h.Sender.Sent);
        Assert.Contains("Configurations sharing this key", email.Html);
        Assert.Contains("Shared Alpha", email.Html);
        Assert.Contains("Shared Beta", email.Html);
        Assert.DoesNotContain("Other Gamma", email.Html);
        Assert.Single(await StatesAsync(h));
    }

    [Fact]
    public async Task DifferentKeys_EachGetAnEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        using var h = CreateHarness();
        long idA = await AddConfigAsync(h, "Key A Config", KeyA);
        long idB = await AddConfigAsync(h, "Key B Config", KeyB);
        var now = DateTime.UtcNow;

        Assert.Equal(ApiKeyAlertOutcome.Sent, await h.Dispatcher.ProcessAsync(Report(idA, KeyA, now), ct));
        Assert.Equal(ApiKeyAlertOutcome.Sent, await h.Dispatcher.ProcessAsync(Report(idB, KeyB, now.AddMinutes(1)), ct));

        Assert.Equal(2, h.Sender.Sent.Count);
        Assert.Equal(2, (await StatesAsync(h)).Count);
    }

    [Fact]
    public async Task SendFailure_LeavesTheKeyUnthrottled_AndTheNextReportIsSent()
    {
        var ct = TestContext.Current.CancellationToken;
        using var h = CreateHarness();
        long id = await AddConfigAsync(h, "Flaky Mail Config", KeyA);
        var now = DateTime.UtcNow;

        h.Sender.Fail = true;
        Assert.Equal(ApiKeyAlertOutcome.SendFailed, await h.Dispatcher.ProcessAsync(Report(id, KeyA, now), ct));
        var state = Assert.Single(await StatesAsync(h));
        Assert.Null(state.LastEmailSentUtc);
        Assert.Empty(h.Sender.Sent);

        h.Sender.Fail = false;
        Assert.Equal(ApiKeyAlertOutcome.Sent, await h.Dispatcher.ProcessAsync(Report(id, KeyA, now.AddMinutes(1)), ct));
        var email = Assert.Single(h.Sender.Sent);
        Assert.Contains("1 further occurrence; this is the first email for this key", email.Html);
        state = Assert.Single(await StatesAsync(h));
        Assert.NotNull(state.LastEmailSentUtc);
    }

    [Fact]
    public async Task Disabled_TryReport_QueuesNothing()
    {
        using var h = CreateHarness(enabled: false);
        long id = await AddConfigAsync(h, "Disabled Alerts Config", KeyA);

        Assert.False(h.Service.TryReport(Report(id, KeyA, DateTime.UtcNow)));

        Assert.False(h.Service.Reader.TryRead(out _));
        Assert.Empty(await StatesAsync(h));
        Assert.Empty(h.Sender.Sent);
    }

    [Fact]
    public void Report_HoldsNoCopyOfTheKey()
    {
        var report = KeyEchoingReport(1, KeyA);

        foreach (var text in ReportStrings(report))
        {
            Assert.NotEqual(KeyA, text);
            Assert.DoesNotContain(KeyA, text, StringComparison.Ordinal);
        }
        Assert.Equal("POST https://generativelanguage.googleapis.com/v1beta/models/gemini-x:streamGenerateContent", report.RequestTarget);
        var header = Assert.Single(report.ResponseHeaders);
        Assert.Equal("request-id", header.Key);
    }

    [Fact]
    public async Task Email_NeverContainsTheKeyOrAnyEightCharacterRunOfIt()
    {
        var ct = TestContext.Current.CancellationToken;
        using var h = CreateHarness();
        long id = await AddConfigAsync(h, "Echo Config", KeyA);

        Assert.Equal(ApiKeyAlertOutcome.Sent, await h.Dispatcher.ProcessAsync(KeyEchoingReport(id, KeyA), ct));

        var email = Assert.Single(h.Sender.Sent);
        AssertNoKeyRun(email.Subject, KeyA);
        AssertNoKeyRun(email.Html, KeyA);
    }

    [Fact]
    public async Task Email_ShowsOnlyTheFourCharacterHint()
    {
        var ct = TestContext.Current.CancellationToken;
        using var h = CreateHarness();
        long id = await AddConfigAsync(h, "Hint Config", KeyA);

        Assert.Equal(ApiKeyAlertOutcome.Sent, await h.Dispatcher.ProcessAsync(KeyEchoingReport(id, KeyA), ct));

        var email = Assert.Single(h.Sender.Sent);
        Assert.Contains("••••" + KeyA[^4..], email.Html);
        Assert.Equal("••••" + KeyA[^4..], CellValue(email.Html, "Key hint"));
    }

    [Fact]
    public async Task Email_HtmlEncodesTheProviderBody()
    {
        var ct = TestContext.Current.CancellationToken;
        using var h = CreateHarness();
        long id = await AddConfigAsync(h, "Markup Config", KeyA);
        const string body = """{"error":{"message":"<script>alert(1)</script>","type":"invalid_request_error","code":"invalid_api_key"}}""";

        Assert.Equal(ApiKeyAlertOutcome.Sent, await h.Dispatcher.ProcessAsync(Report(id, KeyA, DateTime.UtcNow, body: body), ct));

        var email = Assert.Single(h.Sender.Sent);
        Assert.Contains("&lt;script&gt;", email.Html);
        Assert.DoesNotContain("<script>", email.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Email_ForACustomKeyChatFailure_DescribesTheConfigAndTheFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        using var h = CreateHarness();
        long id = await AddConfigAsync(h, "Content Check Config", KeyA, provider: "Anthropic", modelId: "claude-test-model");
        var occurred = DateTime.UtcNow;
        var report = Report(
            id, KeyA, occurred,
            provider: "Anthropic",
            body: """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""",
            requestUri: "https://api.anthropic.com/v1/messages");

        Assert.Equal(ApiKeyAlertOutcome.Sent, await h.Dispatcher.ProcessAsync(report, ct));

        var (_, subject, html) = Assert.Single(h.Sender.Sent);
        Assert.Contains("Anthropic", subject);
        Assert.Contains($"System AI Config #{id}", subject);
        Assert.Contains("\"Content Check Config\"", subject);
        Assert.Contains("custom key", subject);

        Assert.Equal("Anthropic", CellValue(html, "Provider"));
        Assert.Equal("Custom key of this config", CellValue(html, "Key source"));
        Assert.Equal("Chat", CellValue(html, "Used for"));
        Assert.Equal("#" + id.ToString(CultureInfo.InvariantCulture), CellValue(html, "Id"));
        Assert.Contains("Content Check Config", html);
        Assert.Contains("claude-test-model", html);
        Assert.Equal(occurred.ToString(UtcFormat, CultureInfo.InvariantCulture), CellValue(html, "Occurred (UTC)"));
    }

    [Fact]
    public async Task Email_ForADefaultKeyTitleFailure_NamesTheDefaultKeyAndTheUsage()
    {
        var ct = TestContext.Current.CancellationToken;
        using var h = CreateHarness();
        await AddDefaultKeyAsync(h, "OpenAI", KeyA);
        long id = await AddConfigAsync(h, "Title Default Config", KeyA, useDefault: true);

        Assert.Equal(ApiKeyAlertOutcome.Sent,
            await h.Dispatcher.ProcessAsync(Report(id, KeyA, DateTime.UtcNow, usage: ApiKeyUsage.TitleGeneration), ct));

        var (_, subject, html) = Assert.Single(h.Sender.Sent);
        Assert.Contains("OpenAI", subject);
        Assert.Contains("default key", subject);
        Assert.Equal("Default key of OpenAI", CellValue(html, "Key source"));
        Assert.Equal("Title generation", CellValue(html, "Used for"));
        Assert.Contains("Title Default Config", html);
        Assert.Contains("gpt-test-model", html);
    }

    [Fact]
    public async Task Email_NeverContainsTheSessionTitle()
    {
        const string title = "Secret Plans Of The Gnome King";
        var ct = TestContext.Current.CancellationToken;
        using var h = CreateHarness();
        long id = await AddConfigAsync(h, "Session Config", KeyA);
        long sessionId = await AddSessionAsync(h, title);

        Assert.Equal(ApiKeyAlertOutcome.Sent,
            await h.Dispatcher.ProcessAsync(Report(id, KeyA, DateTime.UtcNow, session: SessionRef.Persistent(sessionId)), ct));

        var (_, subject, html) = Assert.Single(h.Sender.Sent);
        Assert.Equal("#" + sessionId.ToString(CultureInfo.InvariantCulture), CellValue(html, "Session"));
        Assert.Equal("no", CellValue(html, "Confidential"));
        Assert.DoesNotContain(title, subject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(title, html, StringComparison.OrdinalIgnoreCase);
    }
}

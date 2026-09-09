using MobileGnollHackLogger.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Overseer.Services;
using GnollHackServer.Data;
using Microsoft.AspNetCore.Identity.UI.Services;
using Overseer.Middleware;
using Overseer.Security;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
string? connectionString = builder.Configuration["ConnectionStrings:SqlDatabaseConnection"];

string? emailConnectionString = builder.Configuration["ConnectionStrings:EmailConnection"];
if (!string.IsNullOrEmpty(emailConnectionString))
{
    EmailSender.ConnectionString = emailConnectionString;
}

// NOTE: No MigrationsAssembly needed — Overseer does not run migrations
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));

// Register ASP.NET Identity (API only - no Razor UI pages)
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.User.RequireUniqueEmail = true;

    /* Length carries far more entropy per unit of user annoyance than composition rules do,
       so the minimum rises and the default character classes are kept as they were rather
       than tightened. Existing passwords are unaffected: this is checked on set, not on
       sign-in. */
    options.Password.RequiredLength = 12;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
/* Required for the TOTP sign-in step. AddIdentityCore registers no token providers, so
   without this UserManager.VerifyTwoFactorTokenAsync throws NotSupportedException for the
   "Authenticator" provider and GetValidTwoFactorProvidersAsync returns nothing -- which means
   PasswordSignInAsync never reports RequiresTwoFactor in the first place. The Razor app gets
   these from AddDefaultIdentity, which is why TOTP could be enabled there and then not
   honoured here. */
.AddDefaultTokenProviders()
.AddSignInManager<SignInManager<ApplicationUser>>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        // Explicitly configure session lifetime
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true; // Refresh the cookie if accessed past halfway point
        options.Cookie.MaxAge = options.ExpireTimeSpan; // CRITICAL: Fix for iOS WKWebView dropping cookies when backgrounded

        /* Pinned rather than left to the framework default: the auth cookie must never travel
           over plain HTTP, and Lax is the strictest SameSite that still survives the
           game-client handoff, which arrives as a top-level GET navigation from
           account.gnollhack.com. Strict would drop the cookie on that navigation. */
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.HttpOnly = true;

        options.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync; // CRITICAL: Prevent stale cookies
        // Override default cookie behavior for SPA — return 401/403 instead of HTML redirects
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
    })
    .AddCookie(IdentityConstants.ExternalScheme) // CRITICAL: Required for SignInManager cleanup
    .AddCookie(IdentityConstants.TwoFactorUserIdScheme); // CRITICAL: Required for SignInManager cleanup

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN"; // Expected by Angular
});

/* Data Protection keys sign the auth and antiforgery cookies. Left at the default they live
   in the profile of whatever account the process runs as and are regenerated when that
   profile is not loaded, which silently signs every user out on a restart.

   The path is deliberately configuration-only, with no fallback: where the keys belong is a
   deployment and custody decision, not something to guess at. Absent the setting the
   framework default stands, exactly as before this line existed. */
string? dataProtectionKeyPath = builder.Configuration["PrivacySettings:DataProtectionKeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
    Directory.CreateDirectory(dataProtectionKeyPath);
    builder.Services.AddDataProtection()
        .SetApplicationName("GnollHackOverseer")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));
}

builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, Overseer.Security.AdminHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.AddRequirements(new Overseer.Security.AdminRequirement()));
});
builder.Services.AddControllersWithViews(options => 
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()); // CRITICAL: Enforce CSRF validation globally
});
builder.Services.AddMemoryCache(options => options.SizeLimit = 10000); // Size limit to prevent DoS

// Register Overseer services
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("GitHub", client =>
{
    client.BaseAddress = new Uri("https://api.github.com");
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    client.DefaultRequestHeaders.Add("User-Agent", "GnollHack-Overseer");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    client.Timeout = TimeSpan.FromSeconds(15);
});
// Obsolete: NetHackWiki is protected by Cloudflare WAF which blocks automated bot requests with HTTP 403 Forbidden.
// builder.Services.AddHttpClient("NetHackWiki", client =>
// {
//     client.BaseAddress = new Uri("https://nethackwiki.com/");
//     client.DefaultRequestHeaders.Add("User-Agent", "GnollHackOverseer/1.0 (https://gnollhack.com/)");
//     client.Timeout = TimeSpan.FromSeconds(15);
// });
builder.Services.AddHttpClient("SentryTunnel", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("AiProvider", client =>
{
    // Adaptive thinking makes both time-to-first-token and total stream duration longer,
    // and HttpClient.Timeout bounds the whole streamed response, not just the headers.
    // The framework default of 100s is too tight for high-effort agentic turns, and a
    // timeout here is a hard user-visible failure with no retry (AgentLoopRunner).
    client.Timeout = TimeSpan.FromSeconds(
        builder.Configuration.GetValue<int>("AiRateLimitSettings:RequestTimeoutSeconds", 600));
});
builder.Services.AddSingleton<WikiService>();
builder.Services.AddSingleton<NetHackWikiService>();
builder.Services.AddSingleton<SourceCodeService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SourceCodeService>());
builder.Services.AddSingleton<NetHackSourceCodeService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NetHackSourceCodeService>());


builder.Services.AddSingleton<CryptoService>();

// Privacy: attachment validation and malware scanning (Tier 1 baseline protections).
builder.Services.AddSingleton<Overseer.Services.Privacy.AttachmentValidator>();
builder.Services.AddSingleton<Overseer.Services.Privacy.ConfidentialityPostureService>();
builder.Services.AddSingleton<Overseer.Services.Privacy.EndpointPolicy>();
builder.Services.AddSingleton<Overseer.Services.Privacy.ConfidentialPolicyResolver>();
builder.Services.AddSingleton<Overseer.Services.Privacy.ConfigurationContentKeyRing>();
builder.Services.AddSingleton<Overseer.Services.Privacy.IContentKeyRing>(
    sp => sp.GetRequiredService<Overseer.Services.Privacy.ConfigurationContentKeyRing>());
/* Scoped, not singleton: it caches unwrapped DEKs, and that plaintext key material should live
   for one request or one turn rather than for the process. */
builder.Services.AddScoped<Overseer.Services.Privacy.ContentProtectionService>();
/* Singleton, and it has to be: an incognito conversation outlives any request scope and has no
   row to be reloaded from, so a scoped store would lose the chat between the send and the
   stream. Its own sweeper evicts what the sliding timeout has expired. */
builder.Services.AddSingleton<Overseer.Services.Privacy.EphemeralSessionStore>();
/* Stateless once constructed -- pre-compiled patterns and the administrator's floor -- so a
   singleton. The per-turn state lives in DlpTokenVault, which ChatService creates and discards
   with the turn. */
builder.Services.AddSingleton<Overseer.Services.Privacy.Dlp.DlpScannerService>();
builder.Services.AddSingleton<Overseer.Services.Privacy.IAntiMalwareScanner>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    /* The named engines to try, in order. "Amsi" alone stays the default; "Amsi,ClamAv" runs
       both through the composite, and "ClamAv" alone suits a container where AMSI does not
       exist. An unrecognised name is ignored with a warning rather than failing startup -- but
       it is never treated as "None", because a typo must not silently disable scanning. */
    string configured = builder.Configuration["PrivacySettings:Attachments:MalwareScanner"] ?? "Amsi";
    var requested = configured
        .Split(new[] { ',', ';', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var startupLogger = loggerFactory.CreateLogger("Overseer.Startup.MalwareScanning");

    if (requested.Length == 1 && requested[0].Equals("None", StringComparison.OrdinalIgnoreCase))
    {
        return new Overseer.Services.Privacy.NullAntiMalwareScanner(
            loggerFactory.CreateLogger<Overseer.Services.Privacy.NullAntiMalwareScanner>());
    }

    var engines = new List<Overseer.Services.Privacy.IAntiMalwareScanner>();
    foreach (string name in requested)
    {
        if (name.Equals("Amsi", StringComparison.OrdinalIgnoreCase))
        {
            /* In-process and needs no extra infrastructure, so it is the default wherever it is
               actually reachable. A build that is not on Windows simply has no AMSI to try. */
            if (!OperatingSystem.IsWindows()) continue;

            var amsi = new Overseer.Services.Privacy.WindowsAmsiScanner(
                loggerFactory.CreateLogger<Overseer.Services.Privacy.WindowsAmsiScanner>());
            if (amsi.IsAvailable) engines.Add(amsi);
            else amsi.Dispose();
        }
        else if (name.Equals("ClamAv", StringComparison.OrdinalIgnoreCase))
        {
            var clam = new Overseer.Services.Privacy.ClamAvAntiMalwareScanner(
                builder.Configuration,
                loggerFactory.CreateLogger<Overseer.Services.Privacy.ClamAvAntiMalwareScanner>());
            if (clam.IsAvailable) engines.Add(clam);
        }
        else if (!name.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            startupLogger.LogWarning(
                "PrivacySettings:Attachments:MalwareScanner names an unknown engine \"{Engine}\"; it was ignored.",
                name);
        }
    }

    /* One engine is returned directly: wrapping it would only add a layer to every log line.
       Two or more go through the composite, where any detection refuses the upload and a
       failure is reported only when none of them managed to scan. */
    if (engines.Count == 1)
    {
        startupLogger.LogInformation("Attachment malware scanning: {Engine}.", engines[0].Name);
        return engines[0];
    }

    if (engines.Count > 1)
    {
        var composite = new Overseer.Services.Privacy.CompositeAntiMalwareScanner(
            engines, loggerFactory.CreateLogger<Overseer.Services.Privacy.CompositeAntiMalwareScanner>());
        startupLogger.LogInformation("Attachment malware scanning: {Engine}.", composite.Name);
        return composite;
    }

    /* Nothing configured was reachable. The null scanner accepts every buffer and logs once, so
       a deployment running without scanning says so rather than appearing to scan -- and
       whether that ACCEPTS the upload is still the caller's RejectOnScanFailure policy, which is
       why an unavailable engine is not the same thing as a failed scan. */
    startupLogger.LogWarning(
        "None of the configured malware scanners ({Configured}) is available; uploads will not be scanned.",
        configured);
    return new Overseer.Services.Privacy.NullAntiMalwareScanner(
        loggerFactory.CreateLogger<Overseer.Services.Privacy.NullAntiMalwareScanner>());
});

/* Document ingestion and local retrieval. All four are stateless after construction: the parser
   holds only its bounds, the chunker its sizes, the embedding service its loaded model, and the
   retrieval service its dials. */
builder.Services.AddSingleton<Overseer.Services.Documents.DocumentParserService>();
builder.Services.AddSingleton<Overseer.Services.Rag.DocumentChunker>();
builder.Services.AddSingleton<Overseer.Services.Rag.IEmbeddingService,
    Overseer.Services.Rag.LocalOnnxEmbeddingService>();
builder.Services.AddSingleton<Overseer.Services.Rag.DocumentRagService>();
/* Scoped, because it holds ContentProtectionService, which caches unwrapped DEKs and is scoped
   for exactly that reason. */
builder.Services.AddScoped<Overseer.Services.Rag.RagSidecarStore>();
builder.Services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.OpenAiResponsesProvider>();
builder.Services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.AnthropicProvider>();
builder.Services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.GoogleProvider>();
builder.Services.AddSingleton<Overseer.Services.Providers.AiRequestGovernor>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddSingleton<Overseer.Services.ParallelExecutionResolver>();
builder.Services.AddScoped<Overseer.Services.Agents.AgentLoopRunner>();
builder.Services.AddScoped<SystemAiConfigService>();
builder.Services.AddSingleton<OngoingChatManager>();
builder.Services.AddSingleton<Overseer.Services.Benchmarking.BenchmarkRunManager>();
builder.Services.AddSingleton<Overseer.Services.Benchmarking.BenchmarkDifficultyJobManager>();
builder.Services.AddScoped<Overseer.Services.Benchmarking.BenchmarkComplianceGuard>();
builder.Services.AddScoped<Overseer.Services.Benchmarking.BenchmarkSnapshotImporter>();
builder.Services.AddScoped<Overseer.Services.Benchmarking.BenchmarkScoringProfileService>();
builder.Services.AddScoped<Overseer.Services.Benchmarking.BenchmarkService>();
builder.Services.AddSingleton<Overseer.Services.Benchmarking.BenchmarkGenerationJobManager>();
builder.Services.AddScoped<Overseer.Services.Benchmarking.BenchmarkGenerationService>();
builder.Services.AddSingleton<Overseer.Services.Benchmarking.BenchmarkRubricCheckJobManager>();
builder.Services.AddScoped<Overseer.Services.Benchmarking.BenchmarkRubricCheckService>();
builder.Services.AddSingleton<Overseer.Services.Benchmarking.BenchmarkRubricGapAuthorJobManager>();
builder.Services.AddScoped<Overseer.Services.Benchmarking.BenchmarkRubricGapAuthorService>();
builder.Services.AddScoped<Overseer.Services.Benchmarking.BenchmarkGroupAnalysisService>();
builder.Services.AddScoped<Overseer.Services.Benchmarking.BenchmarkModelComparisonService>();
builder.Services.AddScoped<Overseer.Services.Benchmarking.BenchmarkComparabilityIndexService>();
builder.Services.AddScoped<Overseer.Services.Benchmarking.BenchmarkRunLauncher>();
// Singleton: it drives a series across many requests and outlives every one of them, creating its
// own scope per member.
builder.Services.AddSingleton<Overseer.Services.Benchmarking.BenchmarkSeriesOrchestrator>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<Overseer.Services.ChatRetentionService>();
builder.Services.AddScoped<Overseer.Services.DatabaseStorageMetricsService>();
builder.Services.AddHostedService<Overseer.Services.DatabaseMaintenanceBackgroundService>();
builder.Services.AddSingleton<ModelMetadataService>();
builder.Services.AddScoped<ModelPricingService>();
builder.Services.AddSingleton<RecommendedModelService>();
builder.Services.AddSignalR(options =>
{
    /* Client tool results arrive as ChatHub.SubmitToolResult invocations. The SignalR
       default MaximumReceiveMessageSize is 32 KB, which silently aborts the connection
       for a large refresh_snapshot payload (the client caps snapshots at 60,000 chars).
       Allow that plus UTF-8 and JSON-escaping overhead. */
    options.MaximumReceiveMessageSize = builder.Configuration.GetValue<long>(
        "ToolExecutionLimits:MaxHubReceiveMessageBytes", 262144);
});
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddTransient<EmailSender>();

// Tool Services
builder.Services.AddSingleton<Overseer.Services.Agents.SubAgentCatalogService>();
builder.Services.AddSingleton<Overseer.Services.Tools.SignalRClientToolBridge>();
builder.Services.AddSingleton<Overseer.Services.Tools.IClientToolBridge>(sp => sp.GetRequiredService<Overseer.Services.Tools.SignalRClientToolBridge>());
builder.Services.AddSingleton<Overseer.Services.Tools.ToolRegistry>();
builder.Services.AddSingleton<Overseer.Services.KnowledgeBaseService>();
builder.Services.AddSingleton<Overseer.Services.Tools.ToolExecutor>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.DelegateToSubAgentTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.KnowledgeBaseTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.WikiSearchTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.NetHackWikiSearchTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.NetHackWikiViewTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.MonsterLookupTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.ItemLookupTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetFullMessageHistoryTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetDirectoryListingTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.RefreshSnapshotTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetSaveInfoTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetPlayerLibraryTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetOracleConsultationsTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetPlayerXlogTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetPlayerDumplogsTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetAppLogTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetPanicLogTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.SearchServerDumplogsTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.SourceCodeSearchTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.SourceCodeViewTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.ListIndexedFilesTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetConstantsTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetFunctionDefinitionTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetMonsterStatsTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetItemStatsTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetArtifactStatsTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.WikiViewTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.SearchDefinitionsTool>();

// GitHub API service
builder.Services.AddSingleton<Overseer.Services.GitHubApiService>();
builder.Services.AddSingleton<Overseer.Services.ConfigHealthService>();

// GitHub tools
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetGitHubRepoInfoTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.SearchGitHubTool>();

// Sentry & Tunnel Logging Configuration
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<Sentry.Extensibility.ISentryEventProcessor, AuthSentryEventProcessor>();
builder.WebHost.UseSentry(options =>
{
    // explicitly map our custom configuration key, or disable Sentry if missing
    options.Dsn = builder.Configuration["SentryDSN"] ?? "";

    /* Both of these are already the defaults of the SDK version pinned in Overseer.csproj.
       They are set explicitly so that a later SDK upgrade cannot widen the reported surface
       without this file changing: SendDefaultPii would start attaching the user's address and
       IP, and a request body size other than None would start attaching request payloads,
       which for /api/chat/send is the user's message and their attachments.

       The substantive telemetry work is in AuthSentryEventProcessor, which scrubs headers,
       cookies, named query values and the user record on every event that is actually sent. */
    options.SendDefaultPii = false;
    options.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;
});

// Rate limiters. Every policy partitions per user, so one heavy user cannot throttle another.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(RateLimitPolicies.SentryTunnel, context =>
    {
        var username = context.User.Identity?.Name ?? "anonymous";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(username, partition => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = 10,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1)
        });
    });

    /* A chat turn is expensive but legitimate use is bursty -- re-asking, editing and retrying
       inside a minute is normal. Starts permissive; tighten with evidence rather than on
       principle. Tool calls run inside one request and are therefore unaffected. */
    options.AddPolicy(RateLimitPolicies.Chat, context =>
    {
        var username = context.User.Identity?.Name ?? "anonymous";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(username, partition => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = builder.Configuration.GetValue("PrivacySettings:RateLimits:ChatPermitsPerMinute", 30),
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1)
        });
    });

    /* Distinctly tighter, because this is the enumeration path: attachment ids are sequential
       and sweeping them is the only reason to fetch many in a minute. Opening one chat with
       five attachments costs five. */
    options.AddPolicy(RateLimitPolicies.Attachment, context =>
    {
        var username = context.User.Identity?.Name ?? "anonymous";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(username, partition => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = builder.Configuration.GetValue("PrivacySettings:RateLimits:AttachmentPermitsPerMinute", 60),
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1)
        });
    });

    /* OnRejected is global, so it dispatches on the policy that actually rejected. It used to
       answer every rejection with the tunnel's "Too many log events", which was written when
       the tunnel was the only limited route and is actively misleading on a throttled chat
       turn. UseRateLimiter sits after UseRouting, so the endpoint's own policy name is
       available here. */
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;

        var window = context.Lease.TryGetMetadata(
            System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter)
            ? retryAfter
            : TimeSpan.FromMinutes(1);
        context.HttpContext.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(window.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        string? policy = context.HttpContext.GetEndpoint()?.Metadata
            .GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName;

        await context.HttpContext.Response.WriteAsync(RateLimitPolicies.RejectionMessage(policy), token);
    };
});

var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
{
    // Proactively initialize these singleton services in the background upon startup
    // so their Lucene indexing completes before the first user request arrives.
    _ = app.Services.GetService<WikiService>();
    _ = app.Services.GetService<NetHackWikiService>();
    _ = app.Services.GetService<KnowledgeBaseService>();
    _ = app.Services.GetService<SourceCodeService>();
    _ = app.Services.GetService<NetHackSourceCodeService>();
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error"); // Global exception handler
    app.UseHsts(); // Matches MobileGnollHackLogger; the default max-age is 30 days
}

app.UseHttpsRedirection();
// Before UseStaticFiles so the headers cover static assets and the SPA fallback too.
app.UseSecurityHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();

// Middleware to issue CSRF cookie to SPA (skip APIs)
app.Use((context, next) =>
{
    if (context.Request.Method == "GET" || !context.Request.Path.Value!.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
    {
        var antiforgery = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions { HttpOnly = false, Secure = true, MaxAge = TimeSpan.FromDays(14) });
    }
    return next(context);
});

app.UseAuthorization();
app.UseRateLimiter(); // CRITICAL: Must be after UseAuthorization

app.MapControllers();
app.MapHub<Overseer.Hubs.ChatHub>("/chathub");

// SPA Fallback to Angular index.html
app.MapFallbackToFile("index.html");

// Synchronously resolve ToolRegistry to validate SubAgentCatalog and tool definitions on startup
_ = app.Services.GetRequiredService<Overseer.Services.Tools.ToolRegistry>();

// Restore benchmark runs left mid-flight by a previous process.
using (var benchmarkCleanupScope = app.Services.CreateScope())
{
    var benchmarkService = benchmarkCleanupScope.ServiceProvider
        .GetRequiredService<Overseer.Services.Benchmarking.BenchmarkService>();
    try { await benchmarkService.CleanupOrphanedRunsAsync(); }
    catch (Exception ex) { app.Logger.LogWarning(ex, "Benchmark orphaned-run cleanup failed."); }

    // A series left Running by an unclean shutdown has no live orchestrator advancing it, so
    // reconcile it to Stopped — otherwise the Continue button never appears and the series is
    // unresumable for the reason the database was made its home in the first place.
    try
    {
        var seriesOrchestrator = app.Services
            .GetRequiredService<Overseer.Services.Benchmarking.BenchmarkSeriesOrchestrator>();
        var db = benchmarkCleanupScope.ServiceProvider
            .GetRequiredService<MobileGnollHackLogger.Data.ApplicationDbContext>();
        await seriesOrchestrator.ReconcileOrphanedSeriesAsync(db);
    }
    catch (Exception ex) { app.Logger.LogWarning(ex, "Benchmark orphaned-series reconciliation failed."); }
}

app.Run();

public partial class Program { }

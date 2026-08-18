using MobileGnollHackLogger.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Overseer.Services;
using GnollHackServer.Data;
using Microsoft.AspNetCore.Identity.UI.Services;

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
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddSignInManager<SignInManager<ApplicationUser>>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        // Explicitly configure session lifetime
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true; // Refresh the cookie if accessed past halfway point
        options.Cookie.MaxAge = options.ExpireTimeSpan; // CRITICAL: Fix for iOS WKWebView dropping cookies when backgrounded

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
});
builder.Services.AddSingleton<WikiService>();
builder.Services.AddSingleton<SourceCodeService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SourceCodeService>());


builder.Services.AddSingleton<CryptoService>();
builder.Services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.OpenAiResponsesProvider>();
builder.Services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.AnthropicProvider>();
builder.Services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.GoogleProvider>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<SystemAiConfigService>();
builder.Services.AddSingleton<OngoingChatManager>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddSingleton<ModelMetadataService>();
builder.Services.AddSingleton<RecommendedModelService>();
builder.Services.AddSignalR();
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddTransient<EmailSender>();

// Tool Services
builder.Services.AddSingleton<Overseer.Services.Tools.SignalRClientToolBridge>();
builder.Services.AddSingleton<Overseer.Services.Tools.IClientToolBridge>(sp => sp.GetRequiredService<Overseer.Services.Tools.SignalRClientToolBridge>());
builder.Services.AddSingleton<Overseer.Services.Tools.ToolRegistry>();
builder.Services.AddSingleton<Overseer.Services.KnowledgeBaseService>();
builder.Services.AddSingleton<Overseer.Services.Tools.ToolExecutor>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.KnowledgeBaseTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.WikiSearchTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.NetHackWikiSearchTool>();
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
});

// Rate Limiter for Sentry Tunnel
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("TunnelRateLimit", context =>
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
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.Response.WriteAsync("Too many log events. Please try again later.", token);
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler("/error"); // Global exception handler

app.UseHttpsRedirection();
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

app.Run();

public partial class Program { }

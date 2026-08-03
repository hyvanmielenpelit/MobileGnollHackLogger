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

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews(options => 
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()); // CRITICAL: Enforce CSRF validation globally
});
builder.Services.AddMemoryCache(options => options.SizeLimit = 10000); // Size limit to prevent DoS

// Register Overseer services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<WikiService>();
builder.Services.AddSingleton<SourceCodeService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SourceCodeService>());
builder.Services.AddSingleton<CryptoService>();
builder.Services.AddScoped<ChatService>();
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
builder.Services.AddSingleton<Overseer.Services.Tools.ToolExecutor>();
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
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.WikiViewTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.SearchDefinitionsTool>();

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
        context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions { HttpOnly = false, Secure = true });
    }
    return next(context);
});

app.UseAuthorization();

app.MapControllers();
app.MapHub<Overseer.Hubs.ChatHub>("/chathub");

// SPA Fallback to Angular index.html
app.MapFallbackToFile("index.html");

app.Run();

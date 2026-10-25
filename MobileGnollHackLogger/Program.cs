using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using MobileGnollHackLogger.Data;
using GnollHackServer.Data;
using GnollHackServer.Data.Legal;
using MobileGnollHackLogger.Filters;
using System.Configuration;
using Azure.Communication.Email;
using Azure.Core.Extensions;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Controller;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.AspNetCore.Http.Extensions;
using System.Text;
using Sentry;

var builder = WebApplication.CreateBuilder(args);

string? connectionString = builder.Configuration["ConnectionStrings:SqlDatabaseConnection"];

if(string.IsNullOrEmpty(connectionString))
{
    throw new Exception("SqlDatabaseConnection string is null or empty.");
}

string? emailConnectionString = builder.Configuration["ConnectionStrings:EmailConnection"];

if (string.IsNullOrEmpty(emailConnectionString))
{
    throw new Exception("EmailConnection string is null or empty.");
}

EmailSender.ConnectionString = emailConnectionString;

string contentRootPath = builder.Environment.ContentRootPath;
string confirmAccountEmailPath = Path.Combine(contentRootPath, @"Content\ConfirmAccountEmail.html");

if(!File.Exists(confirmAccountEmailPath))
{
    throw new FileNotFoundException($"{confirmAccountEmailPath} does not exist.");
}

EmailSender.ConfirmAccountEmailHtml = File.ReadAllText(confirmAccountEmailPath);

if(string.IsNullOrEmpty(EmailSender.ConfirmAccountEmailHtml))
{
    throw new Exception("EmailSender.ConfirmAccountEmailHtml is null or empty.");
}

string forgotPasswordEmailPath = Path.Combine(contentRootPath, @"Content\ForgotPasswordEmail.html");

if(!File.Exists(forgotPasswordEmailPath))
{
    throw new FileNotFoundException($"{forgotPasswordEmailPath} does not exist.");
}

EmailSender.ForgotPasswordEmailHtml = File.ReadAllText(forgotPasswordEmailPath);

if(string.IsNullOrEmpty(EmailSender.ForgotPasswordEmailHtml))
{
    throw new Exception("EmailSender.ForgotPasswordEmailHtml is null or empty.");
}

BonesHelper.VersionCompatibilityList = builder.Configuration.GetSection("BonesVersionCompatibilityInfo").Get<List<BonesVersionCompatibilityInfo>>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, b => b.MigrationsAssembly("GnollHackServer.Data")));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.User.RequireUniqueEmail = true;

    // Overseer signs in against the same accounts and their shared lockout fields; both apps read an identical IdentityLockout section.
    options.Lockout.MaxFailedAccessAttempts = builder.Configuration.GetValue("IdentityLockout:MaxFailedAccessAttempts", 10);
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(builder.Configuration.GetValue("IdentityLockout:LockoutMinutes", 15));
    options.Lockout.AllowedForNewUsers = true;
}).AddEntityFrameworkStores<ApplicationDbContext>()
    // Shared with Overseer: refuses banned accounts on every sign-in path and records the last sign-in.
    .AddSignInManager<GnollHackServer.Data.Security.GnollHackSignInManager>();

// How soon a ban, a lockout or a sign-out everywhere ends existing sessions; identical in Overseer.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval =
    TimeSpan.FromMinutes(builder.Configuration.GetValue("IdentityLockout:SecurityStampValidationMinutes", 5)));

// Game API sign-in throttle: in memory and per process, separate from Identity lockout.
builder.Services.AddSingleton(new GnollHackServer.Data.Security.GameApiSignInThrottle(
    GnollHackServer.Data.Security.GameApiSignInThrottleOptions.FromConfiguration(builder.Configuration)));
builder.Services.AddScoped<MobileGnollHackLogger.Areas.API.GameApiAuthenticator>();
builder.Services.AddSingleton(sp => builder.Configuration.GetSection("AccountHold").Get<GnollHackServer.Data.Security.AccountHoldOptions>()
    ?? new GnollHackServer.Data.Security.AccountHoldOptions());

// Per remote IP: the callers are not signed in. A 429 here is never a sign-in failure, and always carries a body.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(MobileGnollHackLogger.Areas.API.GameApiRateLimits.GameApiFlood, context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = builder.Configuration.GetValue("GameApiSecurity:FloodRequestsPerMinute", 600),
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddPolicy(MobileGnollHackLogger.Areas.API.GameApiRateLimits.AccountPages, context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = builder.Configuration.GetValue("GameApiSecurity:AccountPagePermitsPerMinute", 20),
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.OnRejected = async (context, token) =>
    {
        var http = context.HttpContext;
        http.Response.StatusCode = 429;
        var window = context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter)
            ? retryAfter
            : TimeSpan.FromMinutes(1);
        http.Response.Headers.RetryAfter = ((int)Math.Ceiling(window.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        http.Response.ContentType = "text/plain; charset=utf-8";

        string? policy = http.GetEndpoint()?.Metadata
            .GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName;
        var db = http.RequestServices.GetRequiredService<ApplicationDbContext>();
        await GnollHackServer.Data.Security.SecurityEventWriter.RecordAsync(db, SecurityEventKind.RateLimitRejected,
            ipAddress: http.Connection.RemoteIpAddress?.ToString(), code: policy ?? "unknown", ct: token);

        await http.Response.WriteAsync("Too many requests. Please wait a minute and try again.", token);
    };
});

// Terms documents are read from the wiki folder named by the WikiPath key.
builder.Services.Configure<LegalDocumentOptions>(builder.Configuration.GetSection(LegalDocumentOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LegalDocumentReader>();
builder.Services.AddScoped<TermsAcceptanceStore>();

builder.Services.AddRazorPages()
    // Signed-in users who have not accepted the current account Terms are sent to the acceptance page.
    .AddMvcOptions(o => o.Filters.Add<RequireAccountTermsPageFilter>())
    .ConfigureApiBehaviorOptions(options =>
    options.InvalidModelStateResponseFactory = context =>
    {
        var dbContext = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
        var dbLogger = new DbLogger(dbContext);
        LogType logType = LogType.Other;
        KeyValuePair<string,string?>? kvpController = context.ActionDescriptor.RouteValues.FirstOrDefault(rv => rv.Key == "controller");
        if(kvpController.HasValue)
        {
            if (kvpController.Value.Value == "Log")
            {
                logType = LogType.GameLog;
            }
            else if(kvpController.Value.Value == "Bones")
            {
                logType = LogType.Bones;
            }
        }
        dbLogger.LogType = logType;
        dbLogger.LogSubType = RequestLogSubType.ModelStateFailed;
        dbLogger.RequestPath = context.HttpContext.Request.GetEncodedUrl();
        dbLogger.RequestMethod = context.HttpContext.Request.Method;
        dbLogger.UserIPAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString();
        StringBuilder sbErr = new StringBuilder();
        foreach(var kvp in context.ModelState)
        {
            foreach(var err in kvp.Value.Errors)
            {
                if (sbErr.Length > 0)
                {
                    sbErr.Append("; ");
                }
                sbErr.Append(err.ErrorMessage);
            }
        }
        dbLogger.LogRequest("ModelState Failed: " + sbErr.ToString(), 
            MobileGnollHackLogger.Data.LogLevel.Error, 400);
        return new BadRequestObjectResult(context.ModelState);
    });

builder.Services.AddTransient<IEmailSender, EmailSender>();

// Sentry: backend errors only. An empty SentryDSN disables the SDK.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<Sentry.Extensibility.ISentryEventProcessor, MobileGnollHackLogger.Monitoring.WebsiteSentryEventProcessor>();
builder.WebHost.UseSentry(options =>
{
    options.Dsn = builder.Configuration["SentryDSN"] ?? "";

    /* Already the SDK defaults; pinned so an upgrade cannot start attaching the user's IP and
       address, or request bodies, which for the game API carry the player's password. */
    options.SendDefaultPii = false;
    options.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;

    options.SetBeforeBreadcrumb(MobileGnollHackLogger.Monitoring.WebsiteSentryEventProcessor.ScrubBreadcrumb);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();

app.UseAuthorization();

app.MapRazorPages();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    // Sentry check: reports a handled exception, or throws one with ?throw=true.
    app.MapGet("/dev/sentry-test", (bool? @throw) =>
    {
        if (@throw == true)
        {
            throw new InvalidOperationException("Sentry test: unhandled exception from /dev/sentry-test.");
        }

        try
        {
            throw new InvalidOperationException("Sentry test: handled exception from /dev/sentry-test.");
        }
        catch (Exception ex)
        {
            var eventId = SentrySdk.CaptureException(ex);
            return Results.Text($"Sentry event {eventId} captured.");
        }
    });
}

app.Run();

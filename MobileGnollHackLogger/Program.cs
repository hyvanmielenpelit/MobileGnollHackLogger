using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using MobileGnollHackLogger.Data;
using System.Configuration;
using Azure.Communication.Email;
using Azure.Core.Extensions;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Controller;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.AspNetCore.Http.Extensions;
using System.Text;

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

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
}).AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddRazorPages()
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

app.UseAuthorization();

app.MapRazorPages();

app.MapControllers();

app.Run();

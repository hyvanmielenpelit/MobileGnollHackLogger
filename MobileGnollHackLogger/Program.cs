using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using MobileGnollHackLogger.Data;
using System.Configuration;
using Azure.Communication.Email;
using Azure.Core.Extensions;
using Microsoft.AspNetCore.Identity.UI.Services;

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
string confirmAccountEmailPath = Path.Combine(contentRootPath, @"Areas\Identity\Pages\Account\ConfirmAccountEmail.html");
EmailSender.ConfirmAccountEmailHtml = File.ReadAllText(confirmAccountEmailPath);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
}).AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddRazorPages();

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

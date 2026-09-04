using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using System.Linq;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Overseer.Tests.Helpers
{
    public class OverseerWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((context, configBuilder) =>
            {
                // Inject fake admins and test encryption key for CryptoService dependency in BenchmarkService cleanup
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Admins", "TestAdmin" },
                    { "AesEncryptionKey", System.Convert.ToBase64String(new byte[32]) }
                });
            });

            builder.ConfigureServices(services =>
            {
                // 1. Swap Database to InMemory
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<System.Data.Common.DbConnection>();

                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase("AdminSecurityTestDb")
                    .Options;
                services.AddSingleton(options);

                // 2. Remove all hosted services to avoid hitting file system or database during tests
                services.RemoveAll<IHostedService>();

                // 3. Remove existing Authentication and replace with TestAuthHandler
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "TestAuthType";
                    options.DefaultChallengeScheme = "TestAuthType";
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestAuthType", options => { });

                // 4. Disable Antiforgery filter (CSRF) for API tests
                services.PostConfigure<MvcOptions>(options =>
                {
                    var antiforgeryFilter = options.Filters.FirstOrDefault(f => f.GetType().Name == "AutoValidateAntiforgeryTokenAttribute");
                    if (antiforgeryFilter != null)
                    {
                        options.Filters.Remove(antiforgeryFilter);
                    }
                });
            });
        }
    }
}

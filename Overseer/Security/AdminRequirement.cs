using Microsoft.AspNetCore.Authorization;
using Overseer.Extensions;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace Overseer.Security;

public class AdminRequirement : IAuthorizationRequirement
{
}

public class AdminHandler : AuthorizationHandler<AdminRequirement>
{
    private readonly IConfiguration _configuration;

    public AdminHandler(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (_configuration.IsAdmin(context.User.Identity?.Name))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

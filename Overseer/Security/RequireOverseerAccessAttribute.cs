using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Overseer.Services.Security;

namespace Overseer.Security;

/// <summary>
/// Refuses the action with 403 <c>overseer_blocked</c> when an administrator has blocked the signed-in user
/// from the Overseer AI chat. Anonymous requests pass through to the action's own authorization.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireOverseerAccessAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        string? userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var access = context.HttpContext.RequestServices.GetService<OverseerAccessService>();
        if (!string.IsNullOrEmpty(userId) && access != null)
        {
            var block = await access.GetBlockAsync(userId, context.HttpContext.RequestAborted);
            if (block != null)
            {
                context.Result = new ObjectResult(OverseerAccessService.BlockedBody(block)) { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
        }

        await next();
    }
}

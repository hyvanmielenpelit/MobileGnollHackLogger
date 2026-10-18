using System.Security.Claims;
using GnollHackServer.Data.Legal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Overseer.Security;

/// <summary>
/// Refuses the action with 403 <c>terms_not_accepted</c> when the signed-in user has not accepted the
/// required version of the Gnoll Overseer Terms of Service. Anonymous requests, and hosts without a
/// <see cref="TermsAcceptanceStore"/>, pass through. Placed after <see cref="RequireOverseerAccessAttribute"/>
/// so a blocked user sees the block.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireTermsAcceptedAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
    public const string NotAcceptedCode = "terms_not_accepted";
    public const string NotAcceptedMessage = "To use Gnoll Overseer, accept the Terms of Service.";

    // Runs after RequireOverseerAccessAttribute, whose order is the default 0.
    public int Order => 1;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        string? userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var terms = context.HttpContext.RequestServices.GetService<TermsAcceptanceStore>();
        if (!string.IsNullOrEmpty(userId) && terms != null)
        {
            var status = await terms.GetStatusAsync(userId, LegalDocumentKeys.OverseerTerms, context.HttpContext.RequestAborted);
            if (!status.IsAccepted)
            {
                context.Result = new ObjectResult(NotAcceptedBody(status.RequiredVersion)) { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
        }

        await next();
    }

    public static object NotAcceptedBody(string? requiredVersion)
        => new { error = NotAcceptedCode, requiredVersion };
}

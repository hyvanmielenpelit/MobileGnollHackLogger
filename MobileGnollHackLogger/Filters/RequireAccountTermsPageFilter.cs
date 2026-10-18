using System.Reflection;
using GnollHackServer.Data.Legal;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Filters;

/// <summary>
/// Sends a signed-in user who has not accepted the required version of the account Terms to the
/// acceptance page, with the requested URL as the return URL. Anonymous requests and page models
/// marked <see cref="AllowWithoutAccountTermsAttribute"/> pass through; controllers are unaffected.
/// </summary>
public sealed class RequireAccountTermsPageFilter : IAsyncPageFilter
{
    public const string AcceptTermsPath = "/Identity/Account/AcceptTerms";

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true || IsExempt(context))
        {
            await next();
            return;
        }

        var userManager = http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        string? userId = userManager.GetUserId(http.User);
        if (string.IsNullOrEmpty(userId))
        {
            await next();
            return;
        }

        var store = http.RequestServices.GetRequiredService<TermsAcceptanceStore>();
        var status = await store.GetStatusAsync(userId, LegalDocumentKeys.AccountTerms, http.RequestAborted);
        if (status.IsAccepted)
        {
            await next();
            return;
        }

        var request = http.Request;
        string returnUrl = request.PathBase.Add(request.Path).Value + request.QueryString.Value;
        context.Result = new RedirectResult(request.PathBase.Value + AcceptTermsPath + "?returnUrl=" + Uri.EscapeDataString(returnUrl));
    }

    private static bool IsExempt(PageHandlerExecutingContext context)
    {
        Type type = context.HandlerInstance?.GetType()
            ?? context.ActionDescriptor.ModelTypeInfo?.AsType()
            ?? context.ActionDescriptor.HandlerTypeInfo.AsType();
        return type.GetCustomAttribute<AllowWithoutAccountTermsAttribute>(inherit: true) != null;
    }
}

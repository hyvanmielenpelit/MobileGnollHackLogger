using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Overseer.Controllers;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.IntegrationTests;

/// <summary>
/// Every routed endpoint under <c>api/admin</c> refuses an anonymous caller with 401 and a signed-in non-administrator
/// with 403, and every admin controller carries the <c>AdminOnly</c> policy on its class with no anonymous action.
/// The endpoints are read from the running host, so an endpoint added later is covered without a test change.
/// </summary>
public sealed class AdminEndpointAuthorizationSweepTests : IClassFixture<OverseerWebApplicationFactory>
{
    private readonly OverseerWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminEndpointAuthorizationSweepTests(OverseerWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private sealed record AdminRequest(string Method, string Route, string Path, string? ContentType);

    /// <summary>One request per admin endpoint and HTTP method, with every route parameter filled to satisfy its constraints.</summary>
    private List<AdminRequest> AdminRequests()
    {
        var source = _factory.Services.GetRequiredService<EndpointDataSource>();
        var requests = new List<AdminRequest>();
        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            string raw = endpoint.RoutePattern.RawText ?? "";
            string route = raw.TrimStart('~', '/');
            if (!route.StartsWith("api/admin", StringComparison.OrdinalIgnoreCase)) continue;

            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" };
            string? contentType = endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.FirstOrDefault();
            string path = "/" + string.Join("/", endpoint.RoutePattern.PathSegments.Select(Segment));
            foreach (var method in methods)
                requests.Add(new AdminRequest(method, route, path, contentType));
        }
        return requests
            .DistinctBy(r => (r.Method, r.Path))
            .OrderBy(r => r.Path, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal)
            .ToList();
    }

    private static string Segment(RoutePatternPathSegment segment)
        => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart parameter => ParameterValue(parameter),
            _ => ""
        }));

    private static string ParameterValue(RoutePatternParameterPart parameter)
    {
        if (parameter.IsCatchAll) return "x";
        foreach (var policy in parameter.ParameterPolicies)
        {
            string content = policy.Content ?? "";
            if (content.StartsWith("guid", StringComparison.OrdinalIgnoreCase)) return "00000000-0000-0000-0000-000000000001";
            if (content.StartsWith("bool", StringComparison.OrdinalIgnoreCase)) return "true";
            if (content.StartsWith("datetime", StringComparison.OrdinalIgnoreCase)) return "2026-01-01";
            if (content.StartsWith("alpha", StringComparison.OrdinalIgnoreCase)) return "x";
        }
        return "1";
    }

    private static HttpRequestMessage Build(AdminRequest r, string? user)
    {
        var request = new HttpRequestMessage(new HttpMethod(r.Method), r.Path);
        if (r.Method is "POST" or "PUT" or "PATCH")
        {
            if (r.ContentType != null && r.ContentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
                request.Content = new MultipartFormDataContent { { new StringContent("x"), "x" } };
            else if (r.ContentType != null && r.ContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["x"] = "x" });
            else
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }
        if (user != null) request.Headers.Add("X-Test-User", user);
        return request;
    }

    private async Task<List<string>> FailuresAsync(string? user, HttpStatusCode expected)
    {
        var failures = new List<string>();
        foreach (var r in AdminRequests())
        {
            using var request = Build(r, user);
            using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            if (response.StatusCode != expected)
                failures.Add($"{r.Method} {r.Route} -> {(int)response.StatusCode}");
        }
        return failures;
    }

    [Fact]
    public async Task EveryAdminEndpoint_Anonymous_Returns401()
    {
        var failures = await FailuresAsync(null, HttpStatusCode.Unauthorized);

        Assert.True(failures.Count == 0, "Admin endpoints not refused with 401:\n" + string.Join("\n", failures));
    }

    [Fact]
    public async Task EveryAdminEndpoint_NonAdmin_Returns403()
    {
        var failures = await FailuresAsync("NormalUser", HttpStatusCode.Forbidden);

        Assert.True(failures.Count == 0, "Admin endpoints not refused with 403:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Sweep_SeesTheFeedbackAndBenchmarkEndpoints()
    {
        var routes = AdminRequests().Select(r => r.Route).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("api/admin/feedback/export", routes);
        Assert.Contains("api/admin/feedback/resolutions/apply", routes);
        Assert.Contains(routes, r => r.StartsWith("api/admin/benchmark", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(routes, r => r.StartsWith("api/Admin/", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryAdminController_IsAdminOnly_AndNoActionAllowsAnonymous()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t == typeof(AdminController)
                || (t.GetCustomAttribute<RouteAttribute>()?.Template ?? "").TrimStart('~', '/').StartsWith("api/admin", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        Assert.True(controllers.Count >= 16, $"Expected at least 16 admin controllers, found {controllers.Count}.");
        var failures = new List<string>();
        foreach (var controller in controllers)
        {
            if (!controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(a => a.Policy == "AdminOnly"))
                failures.Add($"{controller.Name} has no [Authorize(Policy = \"AdminOnly\")] on the class.");
            if (controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null)
                failures.Add($"{controller.Name} allows anonymous callers.");
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (action.GetCustomAttribute<AllowAnonymousAttribute>() != null)
                    failures.Add($"{controller.Name}.{action.Name} allows anonymous callers.");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}

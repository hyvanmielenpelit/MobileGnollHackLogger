using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services.Providers;

namespace Overseer.Services.Privacy;

/// <param name="IsValid">False when the configuration must be refused.</param>
/// <param name="Error">The reason, phrased for an administrator. Null when valid.</param>
public sealed record EndpointValidationResult(bool IsValid, string? Error)
{
    public static readonly EndpointValidationResult Valid = new(true, null);

    public static EndpointValidationResult Invalid(string error) => new(false, error);
}

/// <summary>
/// The guard between a configured endpoint and the server's outbound requests.
/// </summary>
/// <remarks>
/// A base URL decides where the server sends an authenticated request, so an unvalidated one is
/// a server-side request forgery primitive: it reaches whatever the deployment's network can
/// reach, including a cloud metadata service, and it arrives carrying a real credential. That
/// is why this class exists before any provider consults a base URL, and why
/// <c>AllowUserSuppliedBaseUrl</c> defaults to false — only an administrator may direct the
/// server's outbound traffic.
///
/// Everything is fail-closed. An empty host allowlist means **no** custom endpoint may be
/// configured, and an empty header allowlist means **no** custom headers are accepted, so
/// enabling either is a deliberate operator act rather than a default nobody chose.
/// </remarks>
public class EndpointPolicy
{
    /* Never settable from configuration, whatever an operator lists. The first three are
       credentials the provider itself owns -- overriding them would let a custom endpoint
       impersonate a different caller or strip authentication. Host rewrites the request's
       target independently of the URL, which is the whole attack this class prevents. The rest
       are hop-by-hop headers per RFC 7230 § 6.1, which belong to the connection and not to the
       message. */
    private static readonly HashSet<string> NeverAllowedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "x-api-key", "api-key",
        "Host", "Cookie", "Set-Cookie",
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade",
        "Content-Length"
    };

    private readonly ILogger<EndpointPolicy>? _logger;
    private readonly string[] _allowedHostPatterns;
    private readonly HashSet<string> _allowedHeaderNames;
    private readonly bool _allowLoopback;

    public EndpointPolicy(IConfiguration configuration, ILogger<EndpointPolicy>? logger = null)
    {
        _logger = logger;

        /* Parsed by hand for the same reason as the confidential floor:
           ConfigurationBinder.GetValue throws on a value it cannot convert, so a typo here
           would fail the application at startup from inside DI construction. Both defaults are
           the closed end, so a malformed value cannot open the SSRF surface. */
        AllowUserSuppliedBaseUrl = ReadBool(
            configuration, "PrivacySettings:CustomEndpoints:AllowUserSuppliedBaseUrl");

        _allowedHostPatterns = configuration
            .GetSection("PrivacySettings:CustomEndpoints:AllowedHostPatterns").Get<string[]>()
            ?? Array.Empty<string>();

        _allowedHeaderNames = new HashSet<string>(
            configuration.GetSection("PrivacySettings:CustomEndpoints:AllowedHeaderNames").Get<string[]>()
                ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        _allowLoopback = ReadBool(configuration, "PrivacySettings:CustomEndpoints:AllowLoopback");
    }

    /// <summary>A missing, empty or unparseable setting reads as false.</summary>
    private static bool ReadBool(IConfiguration configuration, string key)
        => bool.TryParse(configuration[key]?.Trim(), out bool value) && value;

    /// <summary>
    /// Whether a user may set a base URL on their own key. False for this framework version:
    /// users may declare a posture for a key they own, but may not direct where the server
    /// sends its outbound requests.
    /// </summary>
    public bool AllowUserSuppliedBaseUrl { get; }

    /// <summary>
    /// The literal hosts an operator has allowlisted, for the Sentry suppression set. Wildcard
    /// patterns are excluded because there is no host to name.
    /// </summary>
    public IReadOnlyList<string> AllowedLiteralHosts
        => _allowedHostPatterns.Where(p => !p.Contains('*')).Select(p => p.Trim()).ToList();

    /// <summary>
    /// Full validation, including a DNS lookup. Run when an administrator saves a
    /// configuration and from <c>ConfigHealthService</c> — not per request, which
    /// <see cref="Resolve(string?, string?, string?)"/> covers with the cheap checks.
    /// </summary>
    public EndpointValidationResult Validate(string? baseUrl, string? customHeadersJson, string? apiVersion)
    {
        var shallow = ValidateWithoutDns(baseUrl, customHeadersJson, apiVersion);
        if (!shallow.IsValid)
            return shallow;

        if (string.IsNullOrWhiteSpace(baseUrl))
            return EndpointValidationResult.Valid;

        var uri = new Uri(baseUrl.Trim(), UriKind.Absolute);

        /* An address check is only meaningful for a host the operator named literally. A
           wildcard pattern cannot carry this permission: the operator does not know what every
           matching subdomain resolves to, and letting a wildcard grant private-range access is
           how "*.example.com" becomes a route to the metadata service. */
        bool literallyAllowlisted = AllowedLiteralHosts
            .Any(h => string.Equals(h, uri.Host, StringComparison.OrdinalIgnoreCase));

        if (literallyAllowlisted)
            return EndpointValidationResult.Valid;

        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(uri.Host);
        }
        catch (SocketException)
        {
            return EndpointValidationResult.Invalid(
                $"The host '{uri.Host}' could not be resolved. Check the endpoint URL.");
        }
        catch (ArgumentException)
        {
            return EndpointValidationResult.Invalid($"The host '{uri.Host}' is not a valid host name.");
        }

        foreach (var address in addresses)
        {
            if (IsInternalAddress(address))
            {
                return EndpointValidationResult.Invalid(
                    $"The host '{uri.Host}' resolves to the internal address {address}, which is refused. "
                    + "To reach an internal endpoint deliberately, add its exact host name to "
                    + "PrivacySettings:CustomEndpoints:AllowedHostPatterns — a wildcard pattern cannot grant this.");
            }
        }

        return EndpointValidationResult.Valid;
    }

    /// <summary>
    /// The checks that cost nothing: scheme, host pattern, headers and version. Applied on
    /// every resolve, so removing a host from the allowlist stops it being used without
    /// needing the stored rows rewritten.
    /// </summary>
    /// <remarks>
    /// The DNS half deliberately does not run here. It would add a lookup to every turn, and it
    /// cannot defend against a rebind between check and use anyway — the host allowlist is what
    /// actually bounds this, and it is enforced on every resolve. See the framework document.
    /// </remarks>
    public EndpointValidationResult ValidateWithoutDns(string? baseUrl, string? customHeadersJson, string? apiVersion)
    {
        if (!string.IsNullOrWhiteSpace(apiVersion) && apiVersion.Length > 64)
            return EndpointValidationResult.Invalid("The API version cannot exceed 64 characters.");

        var headerResult = ValidateHeaders(customHeadersJson);
        if (!headerResult.IsValid)
            return headerResult;

        if (string.IsNullOrWhiteSpace(baseUrl))
            return EndpointValidationResult.Valid;

        string trimmed = baseUrl.Trim();

        if (trimmed.Length > 2048)
            return EndpointValidationResult.Invalid("The endpoint URL cannot exceed 2048 characters.");

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return EndpointValidationResult.Invalid($"'{trimmed}' is not an absolute URL.");

        if (_allowedHostPatterns.Length == 0)
        {
            return EndpointValidationResult.Invalid(
                "No custom endpoint hosts are allowlisted. Add the endpoint's host to "
                + "PrivacySettings:CustomEndpoints:AllowedHostPatterns before configuring it.");
        }

        bool isLoopback = uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            /* Plain HTTP sends the API key in clear. Tolerated only for loopback, and only when
               an operator has said so: a local model server on 127.0.0.1 with no certificate is
               a real deployment, and a credential that never leaves the machine is a different
               risk from one crossing a network. */
            if (!(isLoopback && _allowLoopback))
            {
                return EndpointValidationResult.Invalid(
                    $"The endpoint must use https (got '{uri.Scheme}'). Plain http is permitted only for a "
                    + "loopback address, and only with PrivacySettings:CustomEndpoints:AllowLoopback enabled.");
            }
        }

        if (isLoopback && !_allowLoopback)
        {
            return EndpointValidationResult.Invalid(
                "Loopback endpoints are not enabled. Set PrivacySettings:CustomEndpoints:AllowLoopback to use one.");
        }

        if (!_allowedHostPatterns.Any(pattern => HostMatches(uri.Host, pattern)))
        {
            return EndpointValidationResult.Invalid(
                $"The host '{uri.Host}' is not in PrivacySettings:CustomEndpoints:AllowedHostPatterns.");
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return EndpointValidationResult.Invalid(
                "The endpoint URL must not carry a query string or fragment; the provider appends its own.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return EndpointValidationResult.Invalid("The endpoint URL must not embed credentials.");

        return EndpointValidationResult.Valid;
    }

    /// <summary>
    /// Parses and checks <c>CustomHeadersJson</c>: a flat JSON object of string values, each
    /// name on the configured allowlist and none of them a header the provider owns.
    /// </summary>
    public EndpointValidationResult ValidateHeaders(string? customHeadersJson)
    {
        if (string.IsNullOrWhiteSpace(customHeadersJson))
            return EndpointValidationResult.Valid;

        if (customHeadersJson.Length > 4096)
            return EndpointValidationResult.Invalid("Custom headers cannot exceed 4096 characters.");

        Dictionary<string, string>? headers;
        try
        {
            headers = JsonSerializer.Deserialize<Dictionary<string, string>>(customHeadersJson);
        }
        catch (JsonException)
        {
            return EndpointValidationResult.Invalid(
                "Custom headers must be a JSON object mapping header names to string values.");
        }

        if (headers == null || headers.Count == 0)
            return EndpointValidationResult.Valid;

        foreach (var (name, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(name))
                return EndpointValidationResult.Invalid("A custom header name cannot be empty.");

            if (NeverAllowedHeaders.Contains(name))
            {
                return EndpointValidationResult.Invalid(
                    $"The header '{name}' cannot be set on a custom endpoint. Credential headers belong to the "
                    + "provider, and Host and hop-by-hop headers belong to the connection.");
            }

            if (!_allowedHeaderNames.Contains(name))
            {
                return EndpointValidationResult.Invalid(
                    $"The header '{name}' is not in PrivacySettings:CustomEndpoints:AllowedHeaderNames.");
            }

            /* A newline in a value is header injection: it ends the header and starts another,
               which is how an allowlisted name smuggles a denied one. */
            if (value != null && (value.Contains('\r') || value.Contains('\n')))
                return EndpointValidationResult.Invalid($"The value of '{name}' cannot contain a line break.");
        }

        return EndpointValidationResult.Valid;
    }

    /// <summary>The endpoint a system AI configuration resolves to.</summary>
    public AiEndpointDescriptor Resolve(SystemAiApiConfiguration? configuration)
        => configuration == null
            ? AiEndpointDescriptor.Official
            : Resolve(configuration.BaseUrl, configuration.CustomHeadersJson, configuration.ApiVersion);

    /// <summary>
    /// The endpoint a user's own key resolves to. Always the official endpoint while
    /// <see cref="AllowUserSuppliedBaseUrl"/> is false, whatever the row happens to hold —
    /// which is what makes the columns safe to carry before the policy allows them.
    /// </summary>
    public AiEndpointDescriptor Resolve(UserAiApiKey? key)
    {
        if (key == null || !AllowUserSuppliedBaseUrl)
            return AiEndpointDescriptor.Official;

        return Resolve(key.BaseUrl, key.CustomHeadersJson, key.ApiVersion);
    }

    /// <summary>
    /// Builds a descriptor from stored values, **falling back to the official endpoint** when
    /// they no longer validate.
    /// </summary>
    /// <remarks>
    /// Falling back rather than throwing is deliberate: removing a host from the allowlist is
    /// how an operator withdraws an endpoint, and that must take effect without anyone
    /// rewriting the stored rows. It is logged at warning, because a turn silently going
    /// somewhere other than where it was configured to go is worth noticing.
    /// </remarks>
    public AiEndpointDescriptor Resolve(string? baseUrl, string? customHeadersJson, string? apiVersion)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) && string.IsNullOrWhiteSpace(customHeadersJson))
            return AiEndpointDescriptor.Official;

        var result = ValidateWithoutDns(baseUrl, customHeadersJson, apiVersion);
        if (!result.IsValid)
        {
            _logger?.LogWarning(
                "Configured custom endpoint '{BaseUrl}' is not usable and the official endpoint is being used instead: {Error}",
                baseUrl, result.Error);
            return AiEndpointDescriptor.Official;
        }

        IReadOnlyDictionary<string, string>? headers = null;
        if (!string.IsNullOrWhiteSpace(customHeadersJson))
        {
            try
            {
                headers = JsonSerializer.Deserialize<Dictionary<string, string>>(customHeadersJson);
            }
            catch (JsonException)
            {
                headers = null;
            }
        }

        return new AiEndpointDescriptor(
            string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/'),
            string.IsNullOrWhiteSpace(apiVersion) ? null : apiVersion.Trim(),
            headers,
            InferAuthStyle(apiVersion));
    }

    /* Azure is the one style identifiable from the configuration itself: api-version is
       mandatory there and meaningless to the others. Everything else custom gets a bearer
       token, which is what an OpenAI-compatible gateway, a LiteLLM proxy and a Vertex endpoint
       all expect -- and which for Google also drops the ?key= query parameter its public API
       uses and a gateway rejects. */
    private static AiEndpointAuthStyle InferAuthStyle(string? apiVersion)
        => string.IsNullOrWhiteSpace(apiVersion)
            ? AiEndpointAuthStyle.BearerToken
            : AiEndpointAuthStyle.AzureApiKey;

    /// <summary>Exact host match, or a single leading <c>*.</c> wildcard over one or more labels.</summary>
    internal static bool HostMatches(string host, string pattern)
    {
        string trimmed = (pattern ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return false;

        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            string suffix = trimmed[1..]; // keeps the leading dot, so "*.a.com" does not match "a.com"
            return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(host, trimmed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether an address is one the server should not be directed at: loopback, private,
    /// link-local (which covers the cloud metadata address 169.254.169.254), carrier-grade NAT,
    /// or IPv6 unique-local.
    /// </summary>
    internal static bool IsInternalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal)
                return true;

            // An IPv4 address wearing an IPv6 mapping is still that IPv4 address.
            if (address.IsIPv4MappedToIPv6)
                return IsInternalAddress(address.MapToIPv4());

            return address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None);
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
            return true; // An address family nobody here understands is not reached.

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            0 => true,                                          // 0.0.0.0/8
            10 => true,                                         // 10.0.0.0/8
            127 => true,                                        // loopback
            169 when octets[1] == 254 => true,                  // link-local, incl. 169.254.169.254
            172 when octets[1] >= 16 && octets[1] <= 31 => true, // 172.16.0.0/12
            192 when octets[1] == 168 => true,                  // 192.168.0.0/16
            100 when octets[1] >= 64 && octets[1] <= 127 => true, // carrier-grade NAT
            255 => true,                                        // broadcast
            _ => false
        };
    }
}

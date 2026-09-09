using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The SSRF surface. A base URL decides where the server sends an authenticated outbound
/// request, so every one of these is about refusing a destination rather than about formatting.
/// </summary>
public class EndpointPolicyTests
{
    private static EndpointPolicy CreatePolicy(
        string[]? hostPatterns = null,
        string[]? headerNames = null,
        bool allowLoopback = false,
        bool allowUserSupplied = false)
    {
        var settings = new Dictionary<string, string?>
        {
            { "PrivacySettings:CustomEndpoints:AllowUserSuppliedBaseUrl", allowUserSupplied ? "true" : "false" },
            { "PrivacySettings:CustomEndpoints:AllowLoopback", allowLoopback ? "true" : "false" }
        };

        for (int i = 0; i < (hostPatterns?.Length ?? 0); i++)
            settings[$"PrivacySettings:CustomEndpoints:AllowedHostPatterns:{i}"] = hostPatterns![i];

        for (int i = 0; i < (headerNames?.Length ?? 0); i++)
            settings[$"PrivacySettings:CustomEndpoints:AllowedHeaderNames:{i}"] = headerNames![i];

        return new EndpointPolicy(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    // ── Fail-closed defaults ────────────────────────────────────────────────────

    [Fact]
    public void NoConfiguration_MeansNoCustomEndpointIsAccepted()
    {
        /* The default posture. An empty host allowlist is not "allow anything" — enabling a
           custom endpoint has to be a deliberate operator act. */
        var policy = CreatePolicy();

        var result = policy.ValidateWithoutDns("https://azure-openai.example.com", null, null);

        Assert.False(result.IsValid);
        Assert.Contains("AllowedHostPatterns", result.Error);
    }

    [Fact]
    public void NoConfiguration_MeansNoCustomHeadersAreAccepted()
    {
        var policy = CreatePolicy();

        var result = policy.ValidateHeaders("{\"X-Gateway-Tenant\":\"acme\"}");

        Assert.False(result.IsValid);
        Assert.Contains("AllowedHeaderNames", result.Error);
    }

    [Fact]
    public void NullAndEmptyBaseUrl_AreValidAndMeanTheOfficialEndpoint()
    {
        var policy = CreatePolicy();

        Assert.True(policy.ValidateWithoutDns(null, null, null).IsValid);
        Assert.True(policy.ValidateWithoutDns("", null, null).IsValid);
        Assert.True(policy.ValidateWithoutDns("   ", null, null).IsValid);

        Assert.False(policy.Resolve(null, null, null).IsCustom);
    }

    // ── Scheme ──────────────────────────────────────────────────────────────────

    [Fact]
    public void PlainHttp_IsRefusedForANonLoopbackHost()
    {
        // The API key would cross the network in clear.
        var policy = CreatePolicy(hostPatterns: new[] { "gateway.example.com" });

        var result = policy.ValidateWithoutDns("http://gateway.example.com", null, null);

        Assert.False(result.IsValid);
        Assert.Contains("https", result.Error);
    }

    [Theory]
    [InlineData("ftp://gateway.example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://gateway.example.com")]
    public void NonHttpSchemes_AreRefused(string url)
    {
        var policy = CreatePolicy(hostPatterns: new[] { "gateway.example.com", "*.example.com" });

        Assert.False(policy.ValidateWithoutDns(url, null, null).IsValid);
    }

    [Fact]
    public void NotAnAbsoluteUrl_IsRefused()
    {
        var policy = CreatePolicy(hostPatterns: new[] { "gateway.example.com" });

        Assert.False(policy.ValidateWithoutDns("/v1/responses", null, null).IsValid);
        Assert.False(policy.ValidateWithoutDns("gateway.example.com", null, null).IsValid);
    }

    // ── Loopback ────────────────────────────────────────────────────────────────

    [Fact]
    public void Loopback_IsRefusedUnlessExplicitlyEnabled()
    {
        var refused = CreatePolicy(hostPatterns: new[] { "localhost" });
        Assert.False(refused.ValidateWithoutDns("http://localhost:11434", null, null).IsValid);
        Assert.False(refused.ValidateWithoutDns("https://localhost:11434", null, null).IsValid);

        /* A local model server on loopback with no certificate is a real deployment, and a
           credential that never leaves the machine is a different risk from one crossing a
           network — but the operator has to say so. */
        var allowed = CreatePolicy(hostPatterns: new[] { "localhost" }, allowLoopback: true);
        Assert.True(allowed.ValidateWithoutDns("http://localhost:11434", null, null).IsValid);
    }

    [Fact]
    public void Loopback_StillNeedsAHostPattern()
    {
        var policy = CreatePolicy(hostPatterns: new[] { "gateway.example.com" }, allowLoopback: true);

        Assert.False(policy.ValidateWithoutDns("http://localhost:11434", null, null).IsValid);
    }

    // ── Host allowlist ──────────────────────────────────────────────────────────

    [Fact]
    public void AHostOutsideTheAllowlist_IsRefused()
    {
        var policy = CreatePolicy(hostPatterns: new[] { "gateway.example.com" });

        var result = policy.ValidateWithoutDns("https://attacker.example.net", null, null);

        Assert.False(result.IsValid);
        Assert.Contains("attacker.example.net", result.Error);
    }

    [Theory]
    [InlineData("gateway.example.com", "gateway.example.com", true)]
    [InlineData("GATEWAY.EXAMPLE.COM", "gateway.example.com", true)]
    [InlineData("gateway.example.com", "other.example.com", false)]
    [InlineData("a.example.com", "*.example.com", true)]
    [InlineData("deep.a.example.com", "*.example.com", true)]
    // The wildcard keeps its dot, so the bare parent domain does not match it.
    [InlineData("example.com", "*.example.com", false)]
    // And it must not match a host that merely ends with the same text.
    [InlineData("notexample.com", "*.example.com", false)]
    /* A wildcard must be spelled "*.suffix". A bare "*example.com" is not a supported
       wildcard, so it is compared literally and matches nothing — which is the safe way for a
       malformed pattern to fail. */
    [InlineData("evilexample.com", "*example.com", false)]
    [InlineData("*example.com", "*example.com", true)]
    // A bare "*" is likewise not a wildcard; there is no "allow every host" pattern.
    [InlineData("anything.example.com", "*", false)]
    public void HostMatches_HandlesExactAndWildcardPatterns(string host, string pattern, bool expected)
    {
        Assert.Equal(expected, EndpointPolicy.HostMatches(host, pattern));
    }

    [Fact]
    public void AUrlCarryingAQueryOrFragmentOrCredentials_IsRefused()
    {
        var policy = CreatePolicy(hostPatterns: new[] { "gateway.example.com" });

        // The provider appends its own query; a base URL bringing one would corrupt it.
        Assert.False(policy.ValidateWithoutDns("https://gateway.example.com/?key=leak", null, null).IsValid);
        Assert.False(policy.ValidateWithoutDns("https://gateway.example.com/#frag", null, null).IsValid);
        Assert.False(policy.ValidateWithoutDns("https://user:pass@gateway.example.com", null, null).IsValid);
    }

    [Fact]
    public void AnAllowlistedHost_IsAccepted()
    {
        var policy = CreatePolicy(hostPatterns: new[] { "*.openai.azure.com" });

        var result = policy.ValidateWithoutDns("https://acme.openai.azure.com", null, "2026-05-01");

        Assert.True(result.IsValid, result.Error);
    }

    // ── Private address ranges ──────────────────────────────────────────────────

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.5.4")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    // The cloud metadata address, which is the reason this check exists at all.
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void IsInternalAddress_RejectsEveryInternalRange(string address)
    {
        Assert.True(EndpointPolicy.IsInternalAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("192.167.0.1")]
    [InlineData("2606:4700::1111")]
    public void IsInternalAddress_AcceptsPublicAddresses(string address)
    {
        Assert.False(EndpointPolicy.IsInternalAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public void Validate_RefusesAHostResolvingIntoAPrivateRange()
    {
        /* A wildcard pattern cannot carry permission to reach an internal address: the
           operator does not know what every matching subdomain resolves to, and letting it
           through is how "*.example.com" becomes a route to the metadata service.

           The pattern is artificial so that the host is an IP literal and the lookup touches
           no network — what matters is that the host matched by a *wildcard* and was still
           refused on its address. */
        var policy = CreatePolicy(hostPatterns: new[] { "*.0.0.1" }, allowLoopback: true);

        var result = policy.Validate("https://127.0.0.1", null, null);

        Assert.False(result.IsValid);
        Assert.Contains("internal address", result.Error);
    }

    [Fact]
    public void Validate_AllowsAnInternalAddressNamedLiterallyInTheAllowlist()
    {
        // An operator naming the exact host has said what they mean.
        var policy = CreatePolicy(hostPatterns: new[] { "127.0.0.1" }, allowLoopback: true);

        var result = policy.Validate("http://127.0.0.1:11434", null, null);

        Assert.True(result.IsValid, result.Error);
    }

    // ── Header allowlist ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Authorization")]
    [InlineData("authorization")]
    [InlineData("x-api-key")]
    [InlineData("api-key")]
    [InlineData("Host")]
    [InlineData("Cookie")]
    [InlineData("Connection")]
    [InlineData("Transfer-Encoding")]
    [InlineData("Upgrade")]
    [InlineData("Content-Length")]
    public void AHeaderTheProviderOrConnectionOwns_IsRefusedEvenWhenAllowlisted(string headerName)
    {
        /* The unconditional denylist outranks the operator's allowlist. Host in particular
           rewrites the request's target independently of the URL, which is the whole attack
           this class exists to prevent. */
        var policy = CreatePolicy(headerNames: new[] { headerName });

        var result = policy.ValidateHeaders("{\"" + headerName + "\":\"anything\"}");

        Assert.False(result.IsValid);
        Assert.Contains("cannot be set", result.Error);
    }

    [Fact]
    public void AnAllowlistedHeader_IsAccepted()
    {
        var policy = CreatePolicy(headerNames: new[] { "X-Gateway-Tenant" });

        Assert.True(policy.ValidateHeaders("{\"X-Gateway-Tenant\":\"acme\"}").IsValid);
        // Matched case-insensitively, as HTTP header names are.
        Assert.True(policy.ValidateHeaders("{\"x-gateway-tenant\":\"acme\"}").IsValid);
    }

    [Fact]
    public void AHeaderValueContainingALineBreak_IsRefused()
    {
        /* Header injection: the value ends the header and starts another, which is how an
           allowlisted name smuggles a denied one. */
        var policy = CreatePolicy(headerNames: new[] { "X-Gateway-Tenant" });

        Assert.False(policy.ValidateHeaders("{\"X-Gateway-Tenant\":\"acme\\r\\nAuthorization: Bearer stolen\"}").IsValid);
        Assert.False(policy.ValidateHeaders("{\"X-Gateway-Tenant\":\"acme\\nHost: evil.test\"}").IsValid);
    }

    [Fact]
    public void MalformedHeaderJson_IsRefused()
    {
        var policy = CreatePolicy(headerNames: new[] { "X-Gateway-Tenant" });

        Assert.False(policy.ValidateHeaders("not json").IsValid);
        Assert.False(policy.ValidateHeaders("[\"an\",\"array\"]").IsValid);
    }

    [Fact]
    public void OversizedValues_AreRefused()
    {
        var policy = CreatePolicy(hostPatterns: new[] { "*.example.com" }, headerNames: new[] { "X-Big" });

        Assert.False(policy.ValidateHeaders("{\"X-Big\":\"" + new string('x', 5000) + "\"}").IsValid);
        Assert.False(policy.ValidateWithoutDns(
            "https://" + new string('a', 2100) + ".example.com", null, null).IsValid);
        Assert.False(policy.ValidateWithoutDns(
            "https://a.example.com", null, new string('v', 100)).IsValid);
    }

    // ── Resolution ──────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_FallsBackToTheOfficialEndpointWhenAStoredValueNoLongerValidates()
    {
        /* Removing a host from the allowlist is how an operator withdraws an endpoint, and it
           has to take effect without anyone rewriting the stored rows. */
        var policy = CreatePolicy(hostPatterns: new[] { "gateway.example.com" });

        var descriptor = policy.Resolve("https://withdrawn.example.net", null, null);

        Assert.False(descriptor.IsCustom);
        Assert.Same(AiEndpointDescriptor.Official.BaseUrl, descriptor.BaseUrl);
    }

    [Fact]
    public void Resolve_TrimsTheTrailingSlashSoPathCompositionCannotDouble()
    {
        var policy = CreatePolicy(hostPatterns: new[] { "gateway.example.com" });

        var descriptor = policy.Resolve("https://gateway.example.com/", null, null);

        Assert.Equal("https://gateway.example.com", descriptor.BaseUrl);
        Assert.Equal("https://gateway.example.com/v1/responses", descriptor.ComposeUrl("unused", "/v1/responses"));
    }

    [Fact]
    public void Resolve_TreatsAnApiVersionAsTheMarkerOfAnAzureEndpoint()
    {
        var policy = CreatePolicy(hostPatterns: new[] { "*.openai.azure.com", "gateway.example.com" });

        var azure = policy.Resolve("https://acme.openai.azure.com", null, "2026-05-01");
        Assert.Equal(AiEndpointAuthStyle.AzureApiKey, azure.AuthStyle);
        Assert.Equal("2026-05-01", azure.ApiVersion);

        var gateway = policy.Resolve("https://gateway.example.com", null, null);
        Assert.Equal(AiEndpointAuthStyle.BearerToken, gateway.AuthStyle);
    }

    [Fact]
    public void Resolve_ForASystemConfiguration_ReadsItsColumns()
    {
        var policy = CreatePolicy(hostPatterns: new[] { "gateway.example.com" }, headerNames: new[] { "X-Tenant" });

        var descriptor = policy.Resolve(new SystemAiApiConfiguration
        {
            BaseUrl = "https://gateway.example.com",
            CustomHeadersJson = "{\"X-Tenant\":\"acme\"}"
        });

        Assert.True(descriptor.IsCustom);
        Assert.Equal("acme", descriptor.CustomHeaders!["X-Tenant"]);
    }

    [Fact]
    public void Resolve_ForAUserKey_IgnoresTheColumnsWhileUserEndpointsAreDisabled()
    {
        /* The columns carry the shape so enabling user endpoints later is a policy change and
           a form rather than a migration — but while the policy is off, a value in the row has
           no effect whatever put it there. */
        var policy = CreatePolicy(hostPatterns: new[] { "gateway.example.com" }, allowUserSupplied: false);

        var descriptor = policy.Resolve(new UserAiApiKey
        {
            Provider = "OpenAI",
            BaseUrl = "https://gateway.example.com"
        });

        Assert.False(descriptor.IsCustom);
        Assert.False(policy.AllowUserSuppliedBaseUrl);
    }

    [Fact]
    public void AMalformedSwitchFallsBackToClosedInsteadOfThrowingAtStartup()
    {
        /* ConfigurationBinder.GetValue throws on a value it cannot convert, and this class is a
           DI singleton -- so "yes" used to fail startup from inside its constructor. Falling
           back to false is also the safe direction here: a typo cannot open the SSRF surface. */
        var policy = new EndpointPolicy(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                { "PrivacySettings:CustomEndpoints:AllowUserSuppliedBaseUrl", "yes" },
                { "PrivacySettings:CustomEndpoints:AllowLoopback", "sure" }
            }).Build());

        Assert.False(policy.AllowUserSuppliedBaseUrl);
        Assert.False(policy.Validate("https://127.0.0.1/v1", null, null).IsValid);
    }

    [Fact]
    public void Resolve_ForAUserKey_HonoursTheColumnsOnceUserEndpointsAreEnabled()
    {
        var policy = CreatePolicy(hostPatterns: new[] { "gateway.example.com" }, allowUserSupplied: true);

        var descriptor = policy.Resolve(new UserAiApiKey
        {
            Provider = "OpenAI",
            BaseUrl = "https://gateway.example.com"
        });

        Assert.True(descriptor.IsCustom);
    }

    [Fact]
    public void AllowedLiteralHosts_ExcludesWildcardPatterns()
    {
        // These feed the Sentry suppression set, where a wildcard has no host to name.
        var policy = CreatePolicy(hostPatterns: new[] { "gateway.example.com", "*.openai.azure.com", "localhost" });

        Assert.Equal(new[] { "gateway.example.com", "localhost" }, policy.AllowedLiteralHosts);
    }
}

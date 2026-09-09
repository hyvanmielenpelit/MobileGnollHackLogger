using System;
using System.Collections.Generic;

namespace Overseer.Services.Providers;

/// <summary>
/// How a request to an endpoint carries its credential. The URL alone is not enough: the same
/// base URL means different things depending on what is listening behind it.
/// </summary>
public enum AiEndpointAuthStyle
{
    /// <summary>
    /// The provider's own public-API scheme — bearer for OpenAI, <c>x-api-key</c> for
    /// Anthropic, and a <c>?key=</c> query parameter for Google.
    /// </summary>
    ProviderDefault = 0,

    /// <summary>
    /// <c>Authorization: Bearer</c>. For Google this also **removes** the <c>?key=</c> query
    /// parameter, which a Vertex or gateway endpoint rejects.
    /// </summary>
    BearerToken = 1,

    /// <summary>
    /// Azure OpenAI: an <c>api-key</c> header and a mandatory <c>?api-version=</c>, not a
    /// bearer token.
    /// </summary>
    AzureApiKey = 2,

    /// <summary>No credential at all — a local server with authentication disabled.</summary>
    None = 3
}

/// <summary>
/// Where a provider request goes and how it authenticates. Produced only by
/// <c>EndpointPolicy</c>, which validates the stored values before building one.
/// </summary>
/// <param name="BaseUrl">
/// Scheme and authority, with no trailing slash. Null means the provider's official public
/// endpoint, which is what every existing key resolves to.
/// </param>
/// <param name="ApiVersion">The <c>api-version</c> Azure requires. Ignored by the other styles.</param>
/// <param name="CustomHeaders">
/// Extra headers, already checked against the configured allowlist. Never contains
/// <c>Host</c>, a credential header the provider owns, or a hop-by-hop header.
/// </param>
public sealed record AiEndpointDescriptor(
    string? BaseUrl,
    string? ApiVersion,
    IReadOnlyDictionary<string, string>? CustomHeaders,
    AiEndpointAuthStyle AuthStyle)
{
    /// <summary>
    /// The provider's official public endpoint with its default auth. What null or empty
    /// configuration resolves to, and what a stored value that fails validation falls back to.
    /// </summary>
    public static readonly AiEndpointDescriptor Official =
        new(null, null, null, AiEndpointAuthStyle.ProviderDefault);

    /// <summary>Whether this descriptor points anywhere other than the official endpoint.</summary>
    public bool IsCustom => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>
    /// Joins <see cref="BaseUrl"/> and a provider-relative path, or returns the official
    /// absolute URL unchanged when this is not a custom endpoint.
    /// </summary>
    /// <param name="officialUrl">The absolute URL used when no base URL is configured.</param>
    /// <param name="relativePath">The path to append to a custom base, starting with '/'.</param>
    public string ComposeUrl(string officialUrl, string relativePath)
        => IsCustom ? BaseUrl!.TrimEnd('/') + relativePath : officialUrl;
}

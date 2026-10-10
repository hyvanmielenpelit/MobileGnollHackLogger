using System.Text.Json;

namespace Overseer.Services.Providers;

/// <summary>
/// Decides whether a provider's error response means it no longer serves the requested model.
/// Only an HTTP 404 with a well-formed JSON error body qualifies: either the provider's own
/// not-found marker, or an error message that names the model ID. A bare or HTML 404, such as a
/// custom endpoint answering a wrong path, never matches.
/// </summary>
public static class ModelNotFoundClassifier
{
    /// <param name="provider">The provider name; a custom endpoint uses its provider's rules.</param>
    /// <param name="status">The HTTP response status.</param>
    /// <param name="body">The response body.</param>
    /// <param name="modelId">The model ID the request named.</param>
    public static bool IsModelNotFound(string? provider, int status, string? body, string? modelId)
    {
        if (status != 404 || string.IsNullOrWhiteSpace(body)) return false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            bool hasErrorObject = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object;

            if (hasErrorObject)
            {
                (string? field, string? expected) = CanonicalProvider(provider) switch
                {
                    "Google" => ("status", "NOT_FOUND"),
                    "OpenAI" => ("code", "model_not_found"),
                    "Anthropic" => ("type", "not_found_error"),
                    _ => ((string?)null, (string?)null)
                };
                if (field != null && string.Equals(StringProperty(error, field), expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (string.IsNullOrWhiteSpace(modelId)) return false;

            // The message fields only: a path echoed elsewhere in the body does not name a missing model.
            string? message = hasErrorObject
                ? StringProperty(error, "message")
                : root.TryGetProperty("error", out var errorText) && errorText.ValueKind == JsonValueKind.String
                    ? errorText.GetString()
                    : StringProperty(root, "message");
            return message != null && message.Contains(modelId.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? CanonicalProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;
        string trimmed = provider.Trim();
        foreach (var name in new[] { "Anthropic", "Google", "OpenAI" })
        {
            if (string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return null;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Overseer.Tests
{
    public static class LiveTestSecrets
    {
        public const string DocPath = "docs/overseer/test-configuration.md";

        /// <summary>A required secret: its key and what it is for.</summary>
        public readonly record struct Required(string Key, string Purpose);

        /// <summary>
        /// Returns null when every required secret is present, or a complete human-readable
        /// report naming ALL missing ones. Reporting them together matters: fixing five
        /// secrets across five failed runs is a bad first hour on a codebase.
        /// </summary>
        public static string? DescribeMissing(IConfiguration config, string testName, params Required[] required)
        {
            var missing = required
                .Where(r => string.IsNullOrWhiteSpace(config[r.Key]))
                .ToList();
            if (missing.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine($"{testName} is not configured.");
            sb.AppendLine();
            sb.AppendLine($"These User Secrets are required but missing or empty ({missing.Count}):");
            sb.AppendLine();
            foreach (var r in missing)
            {
                sb.AppendLine($"  {r.Key}");
                sb.AppendLine($"      {r.Purpose}");
                sb.AppendLine($"      dotnet user-secrets set \"{r.Key}\" \"<value>\" --project Overseer.Tests");
                sb.AppendLine();
            }
            sb.AppendLine("These tests call external AI APIs and are excluded from a normal run.");
            sb.AppendLine("To skip them entirely:");
            sb.AppendLine("  dotnet test MobileGnollHackLogger.slnx --filter \"Category!=UsesExternalApi\"");
            sb.AppendLine();
            sb.AppendLine($"Setup instructions and the full secrets schema: {DocPath}");
            return sb.ToString();
        }

        /// <summary>
        /// Diagnoses a Google error response that indicates a developer configuration problem.
        /// Returns null when the response is not one of those cases, so the caller can fall
        /// through to its normal handling.
        /// </summary>
        public static string? DescribeGoogleConfigError(
            HttpStatusCode status, string body, string model, string modelKey, string apiKeyKey)
        {
            string? errorStatus = null;
            string? errorMessage = null;
            bool hasApiKeyInvalid = false;

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("error", out var errorEl) &&
                        errorEl.ValueKind == JsonValueKind.Object)
                    {
                        if (errorEl.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String)
                        {
                            errorStatus = statusEl.GetString();
                        }
                        if (errorEl.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.String)
                        {
                            errorMessage = messageEl.GetString();
                        }
                        if (errorEl.TryGetProperty("details", out var detailsEl) && detailsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var detail in detailsEl.EnumerateArray())
                            {
                                if (detail.ValueKind == JsonValueKind.Object &&
                                    detail.TryGetProperty("reason", out var reasonEl) &&
                                    reasonEl.ValueKind == JsonValueKind.String &&
                                    string.Equals(reasonEl.GetString(), "API_KEY_INVALID", StringComparison.OrdinalIgnoreCase))
                                {
                                    hasApiKeyInvalid = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Non-JSON response body (e.g. the empty text/html 404 Google returns for
                    // some malformed model paths). Fall through to the status-code checks.
                    // Every property access above is ValueKind-guarded, so JsonException is the
                    // only exception this block can produce - catching more would hide real bugs.
                }
            }

            // 1. Retired / Unknown model: 404 NotFound or error.status == "NOT_FOUND"
            if (status == HttpStatusCode.NotFound || string.Equals(errorStatus, "NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"The Gemini model '{model}' (from {modelKey}) is not available.");
                sb.AppendLine();
                sb.AppendLine($"Google returned HTTP {(int)status} {errorStatus ?? "NOT_FOUND"}:");
                sb.AppendLine($"  {errorMessage ?? (string.IsNullOrWhiteSpace(body) ? "Resource not found" : body.Trim())}");
                sb.AppendLine();
                sb.AppendLine("Models are retired over time, so a value that worked before can stop working.");
                sb.AppendLine();
                sb.AppendLine("To list the models your API key can use, follow \"Finding a replacement model\"");
                sb.AppendLine($"in {DocPath}.");
                sb.AppendLine();
                sb.AppendLine("Then set a replacement:");
                sb.AppendLine($"  dotnet user-secrets set \"{modelKey}\" \"<model-id>\" --project Overseer.Tests");
                sb.AppendLine();
                sb.AppendLine($"The replacement must also be on the live-test allow-list ({LiveApiModelPolicy.AllowedModelsKey}) and");
                sb.AppendLine("should be a fast model - see docs/overseer/gemini-service-tier-measurements.md.");
                sb.AppendLine();
                sb.AppendLine($"Setup and troubleshooting: {DocPath}");
                return sb.ToString();
            }

            // 2. Invalid API key: 400 BadRequest with API_KEY_INVALID detail reason
            if (status == HttpStatusCode.BadRequest && hasApiKeyInvalid)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"The Google API key in {apiKeyKey} was rejected.");
                sb.AppendLine();
                sb.AppendLine("Google returned HTTP 400:");
                sb.AppendLine($"  {errorMessage ?? "API key not valid. Please pass a valid API key."}");
                sb.AppendLine();
                sb.AppendLine("Create or check a key at https://aistudio.google.com/apikey, then:");
                sb.AppendLine($"  dotnet user-secrets set \"{apiKeyKey}\" \"<your-key>\" --project Overseer.Tests");
                sb.AppendLine();
                sb.AppendLine($"Setup and troubleshooting: {DocPath}");
                return sb.ToString();
            }

            // 3. Permission denied: 403 Forbidden or PERMISSION_DENIED
            if (status == HttpStatusCode.Forbidden || string.Equals(errorStatus, "PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Permission denied for Google API key in {apiKeyKey}.");
                sb.AppendLine();
                sb.AppendLine("Google returned HTTP 403 PERMISSION_DENIED:");
                sb.AppendLine($"  {errorMessage ?? (string.IsNullOrWhiteSpace(body) ? "Permission denied" : body.Trim())}");
                sb.AppendLine();
                sb.AppendLine("The API key is valid but does not have access to this model or project.");
                sb.AppendLine("Check permissions or create a new key at https://aistudio.google.com/apikey, then:");
                sb.AppendLine($"  dotnet user-secrets set \"{apiKeyKey}\" \"<your-key>\" --project Overseer.Tests");
                sb.AppendLine();
                sb.AppendLine($"Setup and troubleshooting: {DocPath}");
                return sb.ToString();
            }

            // Other status codes (e.g. 429, 503, 400 INVALID_ARGUMENT without API_KEY_INVALID, 500) return null
            return null;
        }
    }
}

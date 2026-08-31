using System;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Overseer.Tests
{
    /// <summary>
    /// Chooses and vets the Gemini model that live (UsesExternalApi) tests are allowed to call.
    ///
    /// The newest Gemini flash models are the most heavily used and therefore the slowest
    /// and least available: gemini-3.6-flash was measured at a ~2.4 s median with a 59 s
    /// tail, and gemini-3.7-flash at zero successes in 24 attempts, while
    /// gemini-3.5-flash-lite ran at a ~0.7 s median with a 0.9 s worst case. Running the
    /// suite against a slow model makes it take minutes instead of seconds.
    ///
    /// This is an ALLOW-list rather than a deny-list, deliberately: a deny-list would let
    /// the next newest model (which will also be slow) straight through.
    ///
    /// See docs/overseer/gemini-service-tier-measurements.md.
    /// </summary>
    public static class LiveApiModelPolicy
    {
        /// <summary>Used when no model is configured.</summary>
        public const string DefaultModel = "gemini-3.5-flash-lite";

        /// <summary>Comma-separated override, e.g. "gemini-3.5-flash-lite,gemini-4.0-flash-lite".</summary>
        public const string AllowedModelsKey = "AI:LiveTests:AllowedModels";

        public static string ResolveModel(IConfiguration config, string modelKey)
        {
            var configured = config[modelKey];
            return string.IsNullOrWhiteSpace(configured) ? DefaultModel : configured.Trim();
        }

        public static string[] AllowedModels(IConfiguration config)
        {
            var raw = config[AllowedModelsKey];
            if (string.IsNullOrWhiteSpace(raw)) return new[] { DefaultModel };
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public static bool IsAllowed(IConfiguration config, string model)
            => AllowedModels(config).Any(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase));

        public static string DisallowedMessage(IConfiguration config, string model, string modelKey)
            => $"Model '{model}' (from {modelKey}) is not in the live-test allow-list " +
               $"[{string.Join(", ", AllowedModels(config))}]. The newer Gemini flash models are too slow " +
               $"for the test suite. To use it anyway, set {AllowedModelsKey} in User Secrets. " +
               $"See {LiveTestSecrets.DocPath}.";
    }
}

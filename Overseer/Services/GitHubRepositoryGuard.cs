using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services
{
    /// <summary>
    /// Validates the owner and repository names a model passes to the GitHub tools before any
    /// request is built, and limits them to the configured owners (<c>GitHub:AllowedOwners</c>).
    /// </summary>
    internal static class GitHubRepositoryGuard
    {
        internal static readonly string[] DefaultAllowedOwners = { "hyvanmielenpelit", "dotnet", "microsoft", "mono" };

        private static readonly Regex OwnerPattern = new(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$", RegexOptions.CultureInvariant);
        private static readonly Regex RepoPattern = new(@"^[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant);

        /// <summary>Returns null when the owner and repository are valid and allowed, otherwise an error text for the model.</summary>
        public static string? ValidateOwnerRepo(string owner, string? repo, IReadOnlyCollection<string> allowedOwners)
        {
            if (string.IsNullOrEmpty(owner) || !OwnerPattern.IsMatch(owner))
            {
                return $"'{owner}' is not a valid GitHub owner name. {DescribeAllowed(allowedOwners)}";
            }

            if (repo != null && (!RepoPattern.IsMatch(repo) || repo == "." || repo == ".."))
            {
                return $"'{repo}' is not a valid GitHub repository name. {DescribeAllowed(allowedOwners)}";
            }

            if (!allowedOwners.Any(a => string.Equals(a, owner, StringComparison.OrdinalIgnoreCase)))
            {
                return $"The GitHub owner '{owner}' is not available to this tool. {DescribeAllowed(allowedOwners)}";
            }

            return null;
        }

        /// <summary>Returns null when <paramref name="repoFilter"/> is an allowed <c>owner/repo</c>, otherwise an error text for the model.</summary>
        public static string? ValidateRepoFilter(string repoFilter, IReadOnlyCollection<string> allowedOwners)
        {
            var parts = (repoFilter ?? string.Empty).Split('/');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                return $"The repository filter '{repoFilter}' must have the form owner/repo. {DescribeAllowed(allowedOwners)}";
            }

            return ValidateOwnerRepo(parts[0], parts[1], allowedOwners);
        }

        /// <summary>Reads <c>GitHub:AllowedOwners</c>; an absent or empty value gives <see cref="DefaultAllowedOwners"/>.</summary>
        public static IReadOnlyCollection<string> ResolveAllowedOwners(IConfiguration configuration)
        {
            var configured = configuration.GetSection("GitHub:AllowedOwners")
                .GetChildren()
                .Select(c => c.Value?.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return configured.Length > 0 ? configured : DefaultAllowedOwners;
        }

        private static string DescribeAllowed(IReadOnlyCollection<string> allowedOwners)
            => $"Allowed owners: {string.Join(", ", allowedOwners)}.";
    }
}

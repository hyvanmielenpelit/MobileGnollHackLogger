using System;
using System.Linq;

namespace Overseer.Services.Tools
{
    public static class ExternalToolHosts
    {
        public static readonly string[] GitHubHosts = new[]
        {
            "api.github.com",
            "github.com"
        };

        // Obsolete: nethackwiki.com is protected by Cloudflare WAF/Bot Management which blocks automated requests with 403 Forbidden.
        // public static readonly string[] NetHackWikiHosts = new[]
        // {
        //     "nethackwiki.com"
        // };

        public static readonly string[] AllHosts = GitHubHosts;
    }
}

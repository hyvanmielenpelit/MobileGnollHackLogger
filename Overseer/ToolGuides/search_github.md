Search across GitHub for issues, pull requests, or commits matching a query.

- Use this to find GnollHack-related issues in upstream repos (e.g., searching
  dotnet/maui for "SkiaSharp" bugs, or dotnet/android for "gesture" fixes).
- Consult the **tech_stack_and_repositories** knowledge article to identify
  which repositories are relevant for the user's problem.
- Use `repo_filter` to limit search to a specific repository for more
  targeted results.
- The GitHub Search API has stricter rate limits than other endpoints.
  Use sparingly — prefer `get_github_repo_info` for browsing a known repo.
- Results are cached for 5 minutes.
- Prefer `state_filter: "closed"` when looking for fixes that have already
  been resolved.

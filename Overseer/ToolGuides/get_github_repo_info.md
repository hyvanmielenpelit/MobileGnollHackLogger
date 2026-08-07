Retrieve information about a public GitHub repository — commits, issues,
pull requests, releases, or a general summary.

- Use this tool to check the state of GnollHack repositories or upstream
  dependency repositories (e.g., dotnet/maui, mono/SkiaSharp).
- Consult the **tech_stack_and_repositories** knowledge article for the full
  list of relevant repositories and which to check for different problem types.
- For `issue_detail`, you must provide the `issue_number` parameter.
- Results are cached for 5 minutes — repeated identical calls return cached data.
- Rate limit info is included in the response. If the rate limit is low,
  be conservative with further GitHub calls and inform the user.
- Use `search_github` instead when you need to search across repositories
  or find issues matching specific keywords.

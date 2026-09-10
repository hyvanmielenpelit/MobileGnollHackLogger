Search the GnollHack specific wiki for information. Use this before nethack_wiki_search.

**PREFERRED TOOL** — Fast and cheap. Use this as your first tool for general game information,
mechanics, class/race descriptions, and any well-documented feature. Only fall back to
source_code_search or nethack_wiki_search if the GnollHack wiki does not have the answer or you need exact code-level details.

Results are heading-scoped excerpts (snippets) from the most relevant sections of matching articles. If a result ends with an omission marker indicating further sections were omitted, use `wiki_view` (optionally specifying the section name) to retrieve the full article or section text.

`category` is **not** a taxonomy field. It compiles to a wildcard match against the matching
file's path inside the wiki repository, so a plausible value that appears in no path — `spell`,
`class` — silently excludes every hit rather than narrowing them. Omit it unless you know the
articles you want live under a directory of that name; a miss with a category set is more often
the filter than the query.

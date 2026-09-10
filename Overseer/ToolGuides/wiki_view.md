View a specific wiki article by name. Use when you already know which
article you want. Faster and more reliable than wiki_search for known articles.

Use section parameter to extract a specific section (e.g., section: "Strategy").

`article` accepts a title, a filename with or without its `.md` extension, or a
repository-relative path such as `Races/Gnoll` — and the path form is what this tool's result
header and `wiki_search`'s snippet headers show, so a header can be copied straight back in.

Article titles collide, because races, roles, monsters, items, skills and spells share names: a
race and a monster are both called `Gnoll`, a weapon and a weapon skill are both called `Dagger`.
When the tool answers with a list of candidate paths instead of an article, call it again with the
path form of the one you want.

When **no** indexed title equals the name you asked for, the single best Lucene hit over title and
filename is returned with **no relevance floor** — a misspelled or invented name therefore returns
whatever article scored highest rather than a miss. Check the header of the returned article
against what you asked for before quoting it, and use `wiki_search` when you are not certain of
the title.

Leading emoji and other symbols in a heading are ignored when a `section` is matched, so
`section: "Elbereth"` finds `## 🔮 Elbereth`; an exact heading match is preferred over a
normalised one. A `section` that still matches no heading is **not** an error and does not cost you
the result: the marker line `[Section '...' not found in article. Headings: ...]` lists the
article's headings as written, so one can be copied straight back in, and the whole article follows
it.
